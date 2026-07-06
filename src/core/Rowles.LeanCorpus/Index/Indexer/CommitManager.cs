using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Static helpers for the two-phase commit lifecycle, commit-file I/O, and
/// recovery. All state is accessed via the single <see cref="IndexWriter"/> parameter.
/// </summary>
internal static class CommitManager
{
    public static void CommitWithLocks(IndexWriter writer)
    {
        lock (writer.MergeIoLock)
        lock (writer.WriteLock)
        {
            if (writer.PreparedGeneration >= 0)
            {
                PublishPreparedCommit(writer);
                return;
            }

            using var activity = Diagnostics.LeanCorpusActivitySource.Source
                .StartActivity(Diagnostics.LeanCorpusActivitySource.Commit);
            activity?.SetTag("index.commit_generation", writer.CommitGeneration + 1);

            var sw = Stopwatch.StartNew();
            CommitCore(writer);
            sw.Stop();
            writer.Config.Metrics.RecordCommit(sw.Elapsed);

            activity?.SetTag("index.segment_count", writer.CommittedSegments.Count);
        }
    }

    private static void PublishPreparedCommit(IndexWriter writer)
    {
        var dirPath = writer.Directory.DirectoryPath;
        var pendingPath = Path.Combine(dirPath, $"segments_{writer.PreparedGeneration}.pending");
        var finalPath = Path.Combine(dirPath, $"segments_{writer.PreparedGeneration}");

        File.Move(pendingPath, finalPath, overwrite: false);

        writer.CommitGeneration = writer.PreparedGeneration;
        writer.ContentToken = writer.PreparedContentToken;
        writer.ContentChangedSinceCommit = false;

        WriteCommitStats(writer);
        writer.Config.DeletionPolicy.OnCommit(dirPath, writer.CommitGeneration,
            SnapshotManager.GetSnapshotProtectedSegments(writer));

        MergeScheduler.ScheduleBackgroundMerge(writer);

        writer.PreparedGeneration = -1;
        writer.PreparedSegments = null;
    }

    private static void CommitCore(IndexWriter writer)
    {
        var preFlushSegmentCount = writer.CommittedSegments.Count;

        if (preFlushSegmentCount > 0 && writer.PendingDeletes.Count > 0)
            DeletionApplier.ApplyPendingDeletions(
                writer.PendingDeletes, writer.CommittedSegments.GetRange(0, preFlushSegmentCount),
                writer.Directory, writer.CommitGeneration,
                writer.Config.DurableCommits);

        DwptManager.FlushDwptPool(writer);

        IndexWriter.FlushSegmentStatic(writer);

        if (writer.PendingDeletes.Count > 0)
            DeletionApplier.ApplyPendingDeletions(
                writer.PendingDeletes, writer.CommittedSegments,
                writer.Directory, writer.CommitGeneration,
                writer.Config.DurableCommits);

        if (writer.ContentChangedSinceCommit)
            writer.ContentToken++;

        writer.CommitGeneration++;
        WriteCommitFile(writer);
        writer.ContentChangedSinceCommit = false;

        WriteCommitStats(writer);
        writer.Config.DeletionPolicy.OnCommit(writer.Directory.DirectoryPath, writer.CommitGeneration,
            SnapshotManager.GetSnapshotProtectedSegments(writer));

        // Schedule background merge after commit is fully written — segment files must
        // remain intact while WriteCommitStats opens them for scanning.
        MergeScheduler.ScheduleBackgroundMerge(writer);
    }

    public static void WriteCommitFile(IndexWriter writer, bool pending = false, int? generationOverride = null)
    {
        int gen = generationOverride ?? writer.CommitGeneration;
        var dirPath = writer.Directory.DirectoryPath;
        var commitFile = Path.Combine(dirPath, $"segments_{gen}");
        if (pending)
            commitFile += ".pending";

        var segmentIds = new List<string>(writer.CommittedSegments.Count);
        foreach (var seg in writer.CommittedSegments)
            segmentIds.Add(seg.SegmentId);
        var commitData = new CommitData
        {
            Segments = segmentIds,
            Generation = gen,
            ContentToken = writer.ContentToken
        };
        var commitJson = JsonSerializer.Serialize(commitData, LeanCorpusJsonContext.Default.CommitData);

        var fileContent = CommitFileFormat.Wrap(commitJson);

        if (writer.Config.DurableCommits)
        {
            // Segment data files are immutable once written. They are flushed to disk
            // at creation time (via Stream.Flush(flushToDisk: true) in each writer or
            // IndexOutput with durable: true). Only the directory entry sync is needed
            // here to make file-name metadata durable before the commit marker rename.
            DirectoryFsync.Sync(dirPath, strict: true);
            IndexAtomicFileWriter.WriteText(commitFile, fileContent, durable: true);
        }
        else
        {
            IndexAtomicFileWriter.WriteText(commitFile, fileContent, durable: false);
        }
    }

    public static void WriteCommitStats(IndexWriter writer)
    {
        var dirPath = writer.Directory.DirectoryPath;
        int totalDocCount = 0;
        int liveDocCount = 0;
        var fieldLengthSums = new Dictionary<string, long>(StringComparer.Ordinal);
        var fieldDocCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var seg in writer.CommittedSegments)
        {
            var segmentStats = SegmentStats.TryLoadFrom(SegmentStats.GetStatsPath(dirPath, seg.SegmentId));
            if (segmentStats is not null &&
                segmentStats.TotalDocCount == seg.DocCount &&
                segmentStats.LiveDocCount == seg.LiveDocCount)
            {
                AccumulateSegmentStats(segmentStats, fieldLengthSums, fieldDocCounts);
                totalDocCount += segmentStats.TotalDocCount;
                liveDocCount += segmentStats.LiveDocCount;
                continue;
            }

            AccumulateSegmentStatsByScan(seg, writer.Directory, fieldLengthSums, fieldDocCounts,
                ref totalDocCount, ref liveDocCount);
        }

        var avgFieldLengths = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (field, sum) in fieldLengthSums)
        {
            int count = fieldDocCounts.GetValueOrDefault(field, 1);
            avgFieldLengths[field] = count > 0 ? (float)sum / count : 1.0f;
        }

        var stats = new IndexStats(totalDocCount, liveDocCount, avgFieldLengths, fieldDocCounts, fieldLengthSums);
        stats.WriteTo(IndexStats.GetStatsPath(dirPath, writer.CommitGeneration));
    }

    private static void AccumulateSegmentStats(
        SegmentStats segmentStats,
        Dictionary<string, long> fieldLengthSums,
        Dictionary<string, int> fieldDocCounts)
    {
        foreach (var (field, sum) in segmentStats.FieldLengthSums)
            fieldLengthSums[field] = fieldLengthSums.GetValueOrDefault(field) + sum;

        foreach (var (field, count) in segmentStats.FieldDocCounts)
            fieldDocCounts[field] = fieldDocCounts.GetValueOrDefault(field) + count;
    }

    private static void AccumulateSegmentStatsByScan(
        SegmentInfo segment,
        MMapDirectory directory,
        Dictionary<string, long> fieldLengthSums,
        Dictionary<string, int> fieldDocCounts,
        ref int totalDocCount,
        ref int liveDocCount)
    {
        SegmentReader? reader = null;
        try
        {
            reader = new SegmentReader(directory, segment);
        }
        catch (FileNotFoundException)
        {
            // A background merge may have deleted this segment's files.
            // Skip the segment rather than failing the commit.
            return;
        }

        using (reader)
        {
            totalDocCount += reader.MaxDoc;
            for (int docId = 0; docId < reader.MaxDoc; docId++)
            {
                if (!reader.IsLive(docId))
                    continue;

                liveDocCount++;
                foreach (var field in segment.FieldNames)
                {
                    int length = reader.GetFieldLength(docId, field);
                    fieldLengthSums[field] = fieldLengthSums.GetValueOrDefault(field) + length;
                    fieldDocCounts[field] = fieldDocCounts.GetValueOrDefault(field) + 1;
                }
            }
        }
    }

    public static void LoadLatestCommit(IndexWriter writer)
    {
        var directory = writer.Directory;
        var config = writer.Config;
        IndexOpenGuard.EnsureNoBlockingMigration(directory, config.CompatibilityMode);
        var recovery = IndexRecovery.RecoverLatestCommit(directory.DirectoryPath);
        if (recovery is null) return;
        IndexOpenGuard.EnsureCanOpenSegments(directory, recovery.SegmentIds, config.CompatibilityMode, forWriting: true);

        writer.CommitGeneration = recovery.Generation;
        writer.ContentToken = recovery.ContentToken;
        writer.NextSegmentOrdinal = recovery.SegmentIds.Count;

        var dirPath = directory.DirectoryPath;
        foreach (var segId in recovery.SegmentIds)
        {
            var segPath = Path.Combine(dirPath, segId + ".seg");
            if (!File.Exists(segPath))
                continue;

            var seg = SegmentInfo.ReadFrom(segPath);

            var basePath = Path.Combine(dirPath, segId);
            var delPath = seg.DelGeneration.HasValue
                ? basePath + $"_gen_{seg.DelGeneration.Value}.del"
                : basePath + ".del";
            if (File.Exists(delPath))
            {
                var liveDocs = LiveDocs.Deserialise(delPath, seg.DocCount);
                seg.LiveDocCount = liveDocs.LiveCount;
                seg.EarliestSoftDeleteTimestamp = liveDocs.EarliestSoftDeleteTimestamp;
            }
            else
            {
                seg.LiveDocCount = seg.DocCount;
            }

            writer.CommittedSegments.Add(seg);
        }

        if (config.TrackSequenceNumbers)
        {
            long maxSeq = 0;
            foreach (var seg in writer.CommittedSegments)
            {
                if (seg.MaxSequenceNumber.HasValue && seg.MaxSequenceNumber.Value > maxSeq)
                    maxSeq = seg.MaxSequenceNumber.Value;
            }
            writer.NextSequenceNumberMut = maxSeq + 1;
            writer.FlushSeqNoStart = writer.NextSequenceNumber;
        }
    }

    public static void DeleteSegmentFiles(string segId, LeanDirectory directory)
    {
        var directoryPath = directory.DirectoryPath;
        foreach (var file in Directory.GetFiles(directoryPath, segId + ".*"))
        {
            try { directory.DeleteFile(Path.GetFileName(file)); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "segment file delete"); }
        }
        foreach (var file in Directory.GetFiles(directoryPath, segId + "_v_*.*"))
        {
            try { directory.DeleteFile(Path.GetFileName(file)); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "vector file delete"); }
        }
    }

    public static int CompactWithLocks(IndexWriter writer)
    {
        lock (writer.MergeIoLock)
        lock (writer.WriteLock)
        {
            if (writer.PreparedGeneration >= 0)
                throw new InvalidOperationException(
                    "Cannot compact while a prepared commit is pending. Call Commit() or Rollback() first.");

            var dirPath = writer.Directory.DirectoryPath;

            if (writer.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    writer.PendingDeletes, writer.CommittedSegments,
                    writer.Directory, writer.CommitGeneration,
                    writer.Config.DurableCommits);

            if (writer.Buffer.DocCount > 0)
                IndexWriter.FlushSegmentStatic(writer);

            if (writer.CommittedSegments.Count <= 1)
                return 0;

            var segmentsToMerge = writer.CommittedSegments.ToList();
            var protectedSegments = SnapshotManager.GetSnapshotProtectedSegments(writer);

            var mergeable = segmentsToMerge
                .Where(s => !protectedSegments.Contains(s.SegmentId))
                .ToList();

            if (mergeable.Count < 2)
                return 0;

            int mergeableCount = mergeable.Count;

            var merger = new SegmentMerger(writer.Directory, writer.Config.MergePolicy, writer.Config.PostingsSkipInterval,
                writer.Config.SoftDeleteRetentionSeconds);
            int localOrdinal = writer.NextSegmentOrdinal;
            var merged = merger.MergeAll(mergeable, ref localOrdinal);

            if (merged is null)
            {
                foreach (var seg in mergeable)
                    writer.CommittedSegments.Remove(seg);
            }
            else
            {
                foreach (var seg in mergeable)
                    writer.CommittedSegments.Remove(seg);
                writer.CommittedSegments.Add(merged);
            }

            writer.ContentToken++;
            writer.CommitGeneration++;
            writer.NextSegmentOrdinal = Math.Max(writer.NextSegmentOrdinal, localOrdinal);
            WriteCommitFile(writer);
            WriteCommitStats(writer);
            writer.Config.DeletionPolicy.OnCommit(dirPath, writer.CommitGeneration, protectedSegments);

            var activeSegments = new HashSet<string>(
                writer.CommittedSegments.Select(static s => s.SegmentId), StringComparer.Ordinal);
            foreach (var seg in segmentsToMerge)
            {
                if (!activeSegments.Contains(seg.SegmentId) &&
                    !protectedSegments.Contains(seg.SegmentId))
                {
                    merger.CleanupSegmentFiles(seg);
                }
            }

            return mergeableCount;
        }
    }

    public static int ForceMerge(IndexWriter writer, int maxSegments)
    {
        int totalMerged = 0;
        lock (writer.MergeIoLock)
        lock (writer.WriteLock)
        {
            if (writer.PreparedGeneration >= 0)
                throw new InvalidOperationException(
                    "Cannot force-merge while a prepared commit is pending. Call Commit() or Rollback() first.");

            var dirPath = writer.Directory.DirectoryPath;

            if (writer.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    writer.PendingDeletes, writer.CommittedSegments,
                    writer.Directory, writer.CommitGeneration,
                    writer.Config.DurableCommits);

            if (writer.Buffer.DocCount > 0)
                IndexWriter.FlushSegmentStatic(writer);

            var protectedSegments = SnapshotManager.GetSnapshotProtectedSegments(writer);

            while (writer.CommittedSegments.Count > maxSegments)
            {
                var mergeable = writer.CommittedSegments
                    .Where(s => !protectedSegments.Contains(s.SegmentId))
                    .ToList();

                if (mergeable.Count < 2)
                    break;

                mergeable.Sort(static (a, b) => a.DocCount.CompareTo(b.DocCount));
                int count = Math.Min(mergeable.Count, writer.CommittedSegments.Count - maxSegments + 1);
                var toMerge = mergeable.GetRange(0, count);

                var merger = new SegmentMerger(writer.Directory, writer.Config.MergePolicy, writer.Config.PostingsSkipInterval,
                    writer.Config.SoftDeleteRetentionSeconds);
                int localOrdinal = writer.NextSegmentOrdinal;
                var merged = merger.MergeAll(toMerge, ref localOrdinal);
                writer.NextSegmentOrdinal = Math.Max(writer.NextSegmentOrdinal, localOrdinal);

                if (merged is null)
                {
                    foreach (var seg in toMerge)
                        writer.CommittedSegments.Remove(seg);
                }
                else
                {
                    foreach (var seg in toMerge)
                        writer.CommittedSegments.Remove(seg);
                    writer.CommittedSegments.Add(merged);
                }

                totalMerged += toMerge.Count;
            }

            if (totalMerged > 0)
            {
                writer.ContentToken++;
                writer.CommitGeneration++;
                WriteCommitFile(writer);
                WriteCommitStats(writer);
                writer.Config.DeletionPolicy.OnCommit(dirPath, writer.CommitGeneration, protectedSegments);
            }
        }
        return totalMerged;
    }

    public static int PrepareCommit(IndexWriter writer)
    {
        lock (writer.MergeIoLock)
        lock (writer.WriteLock)
        {
            var preFlushSegmentCount = writer.CommittedSegments.Count;
            if (preFlushSegmentCount > 0 && writer.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    writer.PendingDeletes, writer.CommittedSegments.GetRange(0, preFlushSegmentCount),
                    writer.Directory, writer.CommitGeneration,
                    writer.Config.DurableCommits);

            DwptManager.FlushDwptPool(writer);

            IndexWriter.FlushSegmentStatic(writer);

            if (writer.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    writer.PendingDeletes, writer.CommittedSegments,
                    writer.Directory, writer.CommitGeneration,
                    writer.Config.DurableCommits);

            if (writer.ContentChangedSinceCommit)
                writer.ContentToken++;

            int gen = writer.CommitGeneration + 1;
            WriteCommitFile(writer, pending: true, generationOverride: gen);
            writer.ContentChangedSinceCommit = false;

            writer.PreparedGeneration = gen;
            writer.PreparedSegments = new List<SegmentInfo>(writer.CommittedSegments);
            writer.PreparedContentToken = writer.ContentToken;

            return gen;
        }
    }

    public static void RollbackPrepared(IndexWriter writer)
    {
        var directoryPath = writer.Directory.DirectoryPath;
        if (writer.PreparedGeneration < 0)
            return;

        var pendingPath = Path.Combine(directoryPath,
            $"segments_{writer.PreparedGeneration}.pending");
        try { File.Delete(pendingPath); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "rollback pending-file delete"); }

        if (writer.PreparedSegments is not null)
        {
            var committedIds = new HashSet<string>(
                writer.CommittedSegments.Select(static s => s.SegmentId),
                StringComparer.Ordinal);
            foreach (var seg in writer.PreparedSegments)
            {
                if (!committedIds.Contains(seg.SegmentId))
                {
                    writer.CommittedSegments.Remove(seg);
                    DeleteSegmentFiles(seg.SegmentId, writer.Directory);
                }
            }
        }

        writer.PreparedGeneration = -1;
        writer.PreparedSegments = null;
    }
}
