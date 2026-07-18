using Rowles.LeanCorpus.Index.Segment;

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
                int outputOrdinal;
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

                    sources = selected.ToArray();
                    foreach (var source in sources)
                        writer.ReservedMergeSegments.Add(source.SegmentId);

                    outputOrdinal = writer.NextSegmentOrdinal;
                    writer.NextSegmentOrdinal += Math.Max(8, sources.Length);
                }

                long pendingBytes = sources.Sum(static segment => segment.TotalBytes);
                writer.Config.Metrics.RecordMergeBacklog(pendingBytes, TimeSpan.Zero);
                var task = Task.Run(() => RunMerge(writer, sources, outputOrdinal), writer.MergeCts.Token);
                writer.MergeTasks.Add(task);
            }

            writer.MergeTask = writer.MergeTasks.Count == 0
                ? null
                : Task.WhenAll(writer.MergeTasks.ToArray());
        }
    }

    private static void RunMerge(IndexWriter writer, SegmentInfo[] sources, int outputOrdinal)
    {
        try
        {
            if (writer.MergeCts.IsCancellationRequested)
                return;

            int nextOrdinal = outputOrdinal;
            var merger = new SegmentMerger(writer.Directory, writer.Config.MergePolicy,
                writer.Config.PostingsSkipInterval, writer.Config.SoftDeleteRetentionSeconds,
                writer.Config.HnswBuildConfig);
            var merged = merger.MergeAll(sources.ToList(), ref nextOrdinal, writer.CommitGeneration);
            if (merged is null)
                return;

            lock (writer.WriteLock)
            {
                if (writer.MergeCts.IsCancellationRequested)
                    return;

                var currentIds = new HashSet<string>(
                    writer.CommittedSegments.Select(static segment => segment.SegmentId),
                    StringComparer.Ordinal);
                if (sources.Any(source => !currentIds.Contains(source.SegmentId)))
                    return;

                var sourceIds = new HashSet<string>(
                    sources.Select(static source => source.SegmentId), StringComparer.Ordinal);
                writer.CommittedSegments.RemoveAll(segment => sourceIds.Contains(segment.SegmentId));
                writer.CommittedSegments.Add(merged);
                foreach (string sourceId in sourceIds)
                    writer.ObsoleteMergeSegments.Add(sourceId);
                writer.ContentChangedSinceCommit = true;
                writer.NextSegmentOrdinal = Math.Max(writer.NextSegmentOrdinal, nextOrdinal);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "background-merge");
            writer.MarkIndexingFailed();
        }
        finally
        {
            lock (writer.MergeLock)
            {
                foreach (var source in sources)
                    writer.ReservedMergeSegments.Remove(source.SegmentId);
            }
        }
    }
}
