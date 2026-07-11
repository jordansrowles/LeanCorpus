using System.Diagnostics;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Schedules and manages background segment merges.
/// All methods are static — operates via a single <see cref="IndexWriter"/> parameter.
/// </summary>
internal static class MergeScheduler
{
    public static void ScheduleBackgroundMerge(IndexWriter writer)
    {
        lock (writer.MergeLock)
        {
            if (writer.MergeTask is not null && !writer.MergeTask.IsCompleted)
                return;

            var ct = writer.MergeCts.Token;
            writer.MergeTask = Task.Run(() =>
            {
                if (ct.IsCancellationRequested) return;

                try
                {
                    lock (writer.MergeIoLock)
                    {
                        if (ct.IsCancellationRequested) return;

                        using var mergeActivity = Diagnostics.LeanCorpusActivitySource.Source
                            .StartActivity(Diagnostics.LeanCorpusActivitySource.Merge);
                        var mergeSw = Stopwatch.StartNew();

                        SegmentInfo[] sourceSegments;
                        HashSet<string> protectedSegments;
                        int localNextOrd;
                        lock (writer.WriteLock)
                        {
                            if (ct.IsCancellationRequested) return;
                            // A prepared commit is pending, defer this merge until
                            // the prepared commit is published or rolled back, so we
                            // don't collide on the commit generation number.
                            if (writer.PreparedGeneration >= 0) return;
                            sourceSegments = writer.CommittedSegments.ToArray();
                            protectedSegments = SnapshotManager.GetSnapshotProtectedSegments(writer);
                            int reservation = Math.Max(8, sourceSegments.Length);
                            localNextOrd = writer.NextSegmentOrdinal;
                            writer.NextSegmentOrdinal += reservation;
                        }

                        var merger = new SegmentMerger(writer.Directory, writer.Config.MergePolicy, writer.Config.PostingsSkipInterval,
                            writer.Config.SoftDeleteRetentionSeconds, writer.Config.HnswBuildConfig);
                        var sourceList = sourceSegments.ToList();
                        var merged = merger.MaybeMerge(sourceList, ref localNextOrd, protectedSegments, writer.CommitGeneration);

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
                            writer.Config.Metrics.RecordMerge(mergeSw.Elapsed, segmentsMerged);

                        lock (writer.WriteLock)
                        {
                            if (ct.IsCancellationRequested) return;

                            writer.CommittedSegments.RemoveAll(s => consumedIds.Contains(s.SegmentId));
                            writer.CommittedSegments.AddRange(newSegments);
                            writer.NextSegmentOrdinal = Math.Max(writer.NextSegmentOrdinal, localNextOrd);

                            writer.ContentToken++;
                            writer.CommitGeneration++;
                            CommitManager.WriteCommitFile(writer);
                            CommitManager.WriteCommitStats(writer);
                            writer.Config.DeletionPolicy.OnCommit(writer.Directory.DirectoryPath, writer.CommitGeneration,
                                protectedSegments);

                            var activeSegments = new HashSet<string>(
                                writer.CommittedSegments.Select(static segment => segment.SegmentId),
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
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "background-merge");
                    writer.MarkIndexingFailed();
                }
            }, ct);
        }
    }
}
