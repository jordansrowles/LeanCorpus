using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>Schedules bounded background merges without publishing user commits.</summary>
internal static class MergeScheduler
{
    public static void ScheduleBackgroundMerge(IndexWriter writer)
    {
        lock (writer.MergeLock)
        {
            writer.MergeTasks.RemoveAll(static task => task.IsCompleted);

            while (writer.MergeTasks.Count < writer.Config.MaxConcurrentMerges)
            {
                SegmentInfo[] sources;
                FileSnapshotLease sourceFiles;
                int outputOrdinal;
                int commitGeneration;
                lock (writer.WriteLock)
                {
                    if (writer.PreparedGeneration >= 0)
                        break;

                    var protectedSegments = SnapshotManager.GetSnapshotProtectedSegments(writer);
                    foreach (string reserved in writer.ReservedMergeSegments)
                        protectedSegments.Add(reserved);

                    var selected = writer.Config.MergePolicy.FindMerges(writer.CommittedSegments, protectedSegments);
                    if (selected.Count < 2)
                        break;

                    var selectedIds = new HashSet<string>(
                        selected.Select(static segment => segment.SegmentId),
                        StringComparer.Ordinal);
                    sources = writer.CommittedSegments
                        .Where(segment => selectedIds.Contains(segment.SegmentId))
                        .Select(static segment => segment.DeepCopy())
                        .ToArray();
                    outputOrdinal = writer.ReserveSegmentOrdinalRange(Math.Max(8, sources.Length));
                    commitGeneration = writer.CommitGeneration;
                    sourceFiles = writer.Directory.AcquireSnapshot(
                        name => sources.Any(source => SegmentReader.IsSegmentFile(source.SegmentId, name)), out _);
                    foreach (var source in sources)
                        writer.ReservedMergeSegments.Add(source.SegmentId);
                }

                try
                {
                    long pendingBytes = sources.Sum(static segment => segment.TotalBytes);
                    writer.Config.Metrics.RecordMergeBacklog(pendingBytes, TimeSpan.Zero);
                    // RunMerge must release the snapshot even when cancellation precedes execution.
                    var task = Task.Run(() => RunMerge(writer, sources, sourceFiles, outputOrdinal, commitGeneration));
                    writer.MergeTasks.Add(task);
                }
                catch
                {
                    sourceFiles.Dispose();
                    foreach (var source in sources)
                        writer.ReservedMergeSegments.Remove(source.SegmentId);
                    throw;
                }
            }

            writer.MergeTask = writer.MergeTasks.Count == 0
                ? null
                : Task.WhenAll(writer.MergeTasks.ToArray());
        }
    }

    private static void RunMerge(
        IndexWriter writer,
        SegmentInfo[] sources,
        FileSnapshotLease sourceFiles,
        int outputOrdinal,
        int commitGeneration)
    {
        try
        {
            if (writer.MergeCts.IsCancellationRequested)
                return;

            writer.Config.MergeStarting?.Invoke();
            int nextOrdinal = outputOrdinal;
            var merger = new SegmentMerger(writer.Directory, writer.Config.MergePolicy,
                writer.Config.PostingsSkipInterval, writer.Config.SoftDeleteRetentionSeconds,
                writer.Config.HnswBuildConfig, useCompoundFile: writer.Config.UseCompoundFile,
                destinationVectorQuantisation: writer.Config.VectorQuantisation)
            {
                FileCatalog = writer.Config.CodecCatalog
            };
            var merged = merger.MergeAll(sources.ToList(), ref nextOrdinal, commitGeneration);
            if (merged is null)
                return;

            lock (writer.WriteLock)
            {
                if (writer.MergeCts.IsCancellationRequested)
                    return;

                var currentSegments = writer.CommittedSegments.ToDictionary(
                    static segment => segment.SegmentId, StringComparer.Ordinal);

                // A delete may have committed while this merge was reading the source
                // segments. Publishing that stale merge would resurrect deleted docs.
                for (int i = 0; i < sources.Length; i++)
                {
                    if (!currentSegments.TryGetValue(sources[i].SegmentId, out var current) ||
                        current.DelGeneration != sources[i].DelGeneration ||
                        current.LiveDocCount != sources[i].LiveDocCount ||
                        current.EarliestSoftDeleteTimestamp != sources[i].EarliestSoftDeleteTimestamp)
                    {
                        return;
                    }
                }

                var sourceIds = new HashSet<string>(
                    sources.Select(static source => source.SegmentId), StringComparer.Ordinal);
                writer.CommittedSegments.RemoveAll(segment => sourceIds.Contains(segment.SegmentId));
                writer.CommittedSegments.Add(merged);
                foreach (string sourceId in sourceIds)
                    writer.ObsoleteMergeSegments.Add(sourceId);
                writer.ContentChangedSinceCommit = true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "background-merge");
            writer.MarkIndexingFailed(ex);
        }
        finally
        {
            sourceFiles.Dispose();
            lock (writer.MergeLock)
            {
                foreach (var source in sources)
                    writer.ReservedMergeSegments.Remove(source.SegmentId);
            }
        }
    }
}
