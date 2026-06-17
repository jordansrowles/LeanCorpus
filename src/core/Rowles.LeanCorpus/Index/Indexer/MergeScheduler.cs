using System.Diagnostics;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Schedules and manages background segment merges.
/// All methods are static — operates on <see cref="MergeState"/> and parameters.
/// </summary>
internal static class MergeScheduler
{
    /// <summary>
    /// Schedules a background merge if one is not already running.
    /// The merge task acquires <paramref name="writeLock"/> only to snapshot/swap
    /// segment state; heavy I/O runs without it so <c>AddDocument</c> can proceed.
    /// </summary>
    public static void ScheduleBackgroundMerge(
        MergeState mergeState,
        MMapDirectory directory,
        IndexWriterConfig config,
        SnapshotState snapshotState,
        CommitState commitState,
        Lock writeLock)
    {
        lock (mergeState.MergeLock)
        {
            if (mergeState.MergeTask is not null && !mergeState.MergeTask.IsCompleted)
                return;

            var ct = mergeState.MergeCts.Token;
            mergeState.MergeTask = Task.Factory.StartNew(() =>
            {
                if (ct.IsCancellationRequested) return;

                // Lock ordering: _mergeIoLock before _writeLock. _mergeIoLock is held for
                // the entire merge so Commit (which also acquires it before _writeLock)
                // cannot mutate .del files of segments being merged.
                lock (mergeState.MergeIoLock)
                {
                    if (ct.IsCancellationRequested) return;

                    using var mergeActivity = Diagnostics.LeanCorpusActivitySource.Source
                        .StartActivity(Diagnostics.LeanCorpusActivitySource.Merge);
                    var mergeSw = Stopwatch.StartNew();

                    SegmentInfo[] sourceSegments;
                    HashSet<string> protectedSegments;
                    int localNextOrd;
                    lock (writeLock)
                    {
                        if (ct.IsCancellationRequested) return;
                        sourceSegments = commitState.CommittedSegments.ToArray();
                        protectedSegments = SnapshotManager.GetSnapshotProtectedSegments(snapshotState);
                        int reservation = Math.Max(8, sourceSegments.Length);
                        localNextOrd = commitState.NextSegmentOrdinal;
                        commitState.NextSegmentOrdinal += reservation;
                    }

                    // Heavy IO phase runs without _writeLock so AddDocument can buffer.
                    var merger = new SegmentMerger(directory, config.MergePolicy, config.PostingsSkipInterval,
                        config.SoftDeleteRetentionSeconds);
                    var sourceList = sourceSegments.ToList();
                    var merged = merger.MaybeMerge(sourceList, ref localNextOrd, protectedSegments);

                    bool didMerge = !ReferenceEquals(merged, sourceList) && merged.Count != sourceSegments.Length;
                    mergeSw.Stop();

                    if (!didMerge)
                    {
                        mergeActivity?.SetTag("index.segments_merged", 0);
                        return;
                    }

                    var sourceSet = new HashSet<string>(
                        sourceSegments.Select(static s => s.SegmentId), StringComparer.Ordinal);
                    var mergedSet = new HashSet<string>(
                        merged.Select(static s => s.SegmentId), StringComparer.Ordinal);
                    var consumedIds = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var s in sourceSegments)
                        if (!mergedSet.Contains(s.SegmentId))
                            consumedIds.Add(s.SegmentId);
                    var newSegments = new List<SegmentInfo>();
                    foreach (var s in merged)
                        if (!sourceSet.Contains(s.SegmentId))
                            newSegments.Add(s);

                    int segmentsMerged = consumedIds.Count - newSegments.Count + 1;
                    mergeActivity?.SetTag("index.segments_merged", segmentsMerged);
                    if (segmentsMerged > 0)
                        config.Metrics.RecordMerge(mergeSw.Elapsed, segmentsMerged);

                    lock (writeLock)
                    {
                        if (ct.IsCancellationRequested) return;

                        commitState.CommittedSegments.RemoveAll(s => consumedIds.Contains(s.SegmentId));
                        commitState.CommittedSegments.AddRange(newSegments);
                        commitState.NextSegmentOrdinal = Math.Max(commitState.NextSegmentOrdinal, localNextOrd);

                        commitState.ContentToken++;
                        commitState.CommitGeneration++;
                        CommitManager.WriteCommitFile(commitState, directory, config);
                        CommitManager.WriteCommitStats(commitState, directory);
                        config.DeletionPolicy.OnCommit(directory.DirectoryPath, commitState.CommitGeneration,
                            protectedSegments);

                        var activeSegments = new HashSet<string>(
                            commitState.CommittedSegments.Select(static segment => segment.SegmentId),
                            StringComparer.Ordinal);
                        foreach (var segment in sourceSegments)
                        {
                            if (!activeSegments.Contains(segment.SegmentId) &&
                                !protectedSegments.Contains(segment.SegmentId))
                            {
                                merger.CleanupSegmentFiles(segment);
                            }
                        }
                    }
                }
            }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}
