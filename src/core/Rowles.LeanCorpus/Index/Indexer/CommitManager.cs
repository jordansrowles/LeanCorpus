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

        FileOpenRetry.Move(pendingPath, finalPath, overwrite: false);
        // The rename is the irreversible publication boundary. Establish the published
        // state before doing anything that can throw, so a later failure cannot be rolled
        // back over the commit file that readers can already observe.
        writer.CommitGeneration = writer.PreparedGeneration;
        writer.ContentToken = writer.PreparedContentToken;
        writer.ContentChangedSinceCommit = false;
        writer.PreparedGeneration = -1;
        writer.PreparedSegments = null;

        try
        {
            if (writer.Config.DurableCommits)
            {
                var publicationSync = writer.Config.PreparedCommitPublicationSync;
                if (publicationSync is not null)
                    publicationSync(dirPath);
                else
                    DirectoryFsync.Sync(dirPath, strict: true);
            }

            writer.Config.DeletionPolicy.OnCommit(dirPath, writer.CommitGeneration,
                SnapshotManager.GetSnapshotProtectedSegments(writer));
            CleanupObsoleteMergeSegments(writer);

            MergeScheduler.ScheduleBackgroundMerge(writer);
        }
        catch
        {
            // Publication already happened. Continuing would permit file deletion or a
            // second publication attempt against an in-memory state that cannot be rolled back.
            writer.MarkIndexingFailed();
            throw;
        }
    }

    private static void CommitCore(IndexWriter writer)
    {
        DwptManager.WaitForPendingFlushes(writer);
        DwptManager.FlushDwptPool(writer);

        IndexWriter.FlushSegmentStatic(writer);

        // Apply pending deletes to all committed segments after flush.
        // This covers both: queued deletes targeting previously committed
        // docs, and deletes queued before any segment existed (preFlushCount
        // was 0, so the old pre-flush pass would have skipped them).
        if (writer.PendingDeletes.Count > 0)
            DeletionApplier.ApplyPendingDeletions(
                writer.DeleteQueue, writer.CommittedSegments,
                writer.Directory, writer.CommitGeneration,
                writer.Config.DurableCommits, writer.Config.Metrics);

        if (writer.ContentChangedSinceCommit)
            writer.ContentToken++;

        writer.CommitGeneration++;
        WriteCommitStats(writer);
        WriteCommitFile(writer);
        writer.ContentChangedSinceCommit = false;
        writer.Config.DeletionPolicy.OnCommit(writer.Directory.DirectoryPath, writer.CommitGeneration,
            SnapshotManager.GetSnapshotProtectedSegments(writer));
        CleanupObsoleteMergeSegments(writer);

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
            SyncChangedFiles(writer);
            DirectoryFsync.Sync(dirPath, strict: true);
            IndexAtomicFileWriter.WriteText(commitFile, fileContent, durable: true);
        }
        else
        {
            IndexAtomicFileWriter.WriteText(commitFile, fileContent, durable: false);
        }
    }

    private static void SyncChangedFiles(IndexWriter writer)
    {
        var stopwatch = Stopwatch.StartNew();
        long bytes = 0;
        int count = 0;

        var currentFiles = GetCurrentSegmentFiles(writer);
        foreach (var filePath in currentFiles)
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var metadata = FileOpenRetry.GetFileMetadata(filePath);
            var current = new FileSyncState(metadata.Length, metadata.LastWriteTimeUtc.Ticks);
            if (writer.SyncedFileStates.TryGetValue(filePath, out var synced) && synced == current)
                continue;

            DirectoryFsync.SyncFile(filePath, strict: true);
            writer.SyncedFileStates[filePath] = current;
            bytes += metadata.Length;
            count++;
        }

        stopwatch.Stop();
        writer.Config.Metrics.RecordFileSync(stopwatch.Elapsed, bytes, count);
    }

    private static string[] GetCurrentSegmentFiles(IndexWriter writer)
    {
        var files = new HashSet<string>(StringComparer.Ordinal);
        var directoryPath = writer.Directory.DirectoryPath;

        foreach (var segment in writer.CommittedSegments)
        {
            foreach (var filePath in FileOpenRetry.EnumerateFiles(directoryPath, segment.SegmentId + ".*"))
                files.Add(filePath);
            foreach (var filePath in FileOpenRetry.EnumerateFiles(directoryPath, segment.SegmentId + "_gen_*.del"))
                files.Add(filePath);
            foreach (var filePath in FileOpenRetry.EnumerateFiles(directoryPath, segment.SegmentId + "_v_*.*"))
                files.Add(filePath);
        }

        var statsPath = IndexStats.GetStatsPath(directoryPath, writer.CommitGeneration);
        if (FileOpenRetry.FileExists(statsPath))
            files.Add(statsPath);

        return files.ToArray();
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
        var recovery = IndexRecovery.RecoverLatestCommit(
            directory.DirectoryPath,
            catalog: config.CodecCatalog);
        if (recovery is null) return;
        IndexOpenGuard.EnsureCanOpenSegments(
            directory,
            recovery.SegmentIds,
            config.CompatibilityMode,
            forWriting: true,
            config.CodecCatalog);

        writer.CommitGeneration = recovery.Generation;
        writer.ContentToken = recovery.ContentToken;
        writer.NextSegmentOrdinal = GetNextSegmentOrdinal(recovery.SegmentIds);

        var dirPath = directory.DirectoryPath;
        foreach (var segId in recovery.SegmentIds)
        {
            var segPath = Path.Combine(dirPath, segId + ".seg");
            if (!FileOpenRetry.FileExists(segPath))
                continue;

            var seg = SegmentInfo.ReadFrom(segPath);

            var basePath = Path.Combine(dirPath, segId);
            var delPath = seg.DelGeneration.HasValue
                ? basePath + $"_gen_{seg.DelGeneration.Value}.del"
                : basePath + ".del";
            if (FileOpenRetry.FileExists(delPath))
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
        foreach (var file in FileOpenRetry.GetFiles(directoryPath, segId + ".*"))
        {
            try { directory.DeleteFile(Path.GetFileName(file)); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "segment file delete"); }
        }
        foreach (var file in FileOpenRetry.GetFiles(directoryPath, segId + "_v_*.*"))
        {
            try { directory.DeleteFile(Path.GetFileName(file)); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "vector file delete"); }
        }
    }

    private static int GetNextSegmentOrdinal(IEnumerable<string> segmentIds)
    {
        int nextOrdinal = 0;
        foreach (string segmentId in segmentIds)
        {
            if (!segmentId.StartsWith("seg_", StringComparison.Ordinal) ||
                !int.TryParse(segmentId.AsSpan("seg_".Length), out int ordinal))
            {
                continue;
            }

            nextOrdinal = Math.Max(nextOrdinal, ordinal + 1);
        }

        return nextOrdinal;
    }

    public static int CompactWithLocks(IndexWriter writer)
    {
        WaitForBackgroundMerges(writer);
        lock (writer.MergeIoLock)
        lock (writer.WriteLock)
        {
            if (writer.PreparedGeneration >= 0)
                throw new InvalidOperationException(
                    "Cannot compact while a prepared commit is pending. Call Commit() or Rollback() first.");

            var dirPath = writer.Directory.DirectoryPath;

            DwptManager.FlushDwptPool(writer);
            if (writer.Buffer.DocCount > 0)
                IndexWriter.FlushSegmentStatic(writer);

            if (writer.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    writer.DeleteQueue, writer.CommittedSegments,
                    writer.Directory, writer.CommitGeneration,
                    writer.Config.DurableCommits, writer.Config.Metrics);

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
                writer.Config.SoftDeleteRetentionSeconds, writer.Config.HnswBuildConfig,
                useCompoundFile: writer.Config.UseCompoundFile);
            int localOrdinal = writer.NextSegmentOrdinal;
            var merged = merger.MergeAll(mergeable, ref localOrdinal, writer.CommitGeneration);

            if (merged is null)
            {
                // All docs in the source segments are dead. Preserve source
                // segments rather than silently dropping them.
                return 0;
            }

            foreach (var seg in mergeable)
                writer.CommittedSegments.Remove(seg);
            writer.CommittedSegments.Add(merged);

            writer.ContentToken++;
            writer.CommitGeneration++;
            writer.NextSegmentOrdinal = Math.Max(writer.NextSegmentOrdinal, localOrdinal);
            WriteCommitStats(writer);
            WriteCommitFile(writer);
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
        WaitForBackgroundMerges(writer);
        lock (writer.MergeIoLock)
        lock (writer.WriteLock)
        {
            if (writer.PreparedGeneration >= 0)
                throw new InvalidOperationException(
                    "Cannot force-merge while a prepared commit is pending. Call Commit() or Rollback() first.");

            var dirPath = writer.Directory.DirectoryPath;

            DwptManager.FlushDwptPool(writer);
            if (writer.Buffer.DocCount > 0)
                IndexWriter.FlushSegmentStatic(writer);

            if (writer.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    writer.DeleteQueue, writer.CommittedSegments,
                    writer.Directory, writer.CommitGeneration,
                    writer.Config.DurableCommits, writer.Config.Metrics);

            var protectedSegments = SnapshotManager.GetSnapshotProtectedSegments(writer);

            var allConsumed = new List<SegmentInfo>();
            SegmentMerger? lastMerger = null;

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
                    writer.Config.SoftDeleteRetentionSeconds, writer.Config.HnswBuildConfig,
                    useCompoundFile: writer.Config.UseCompoundFile);
                lastMerger = merger;
                int localOrdinal = writer.NextSegmentOrdinal;
                var merged = merger.MergeAll(toMerge, ref localOrdinal, writer.CommitGeneration);
                writer.NextSegmentOrdinal = Math.Max(writer.NextSegmentOrdinal, localOrdinal);

                if (merged is null)
                {
                    // All docs in the source segments are dead (deleted or soft-deleted
                    // past retention). Preserve source segments rather than silently
                    // dropping them with no replacement.
                    break;
                }

                foreach (var seg in toMerge)
                    writer.CommittedSegments.Remove(seg);
                writer.CommittedSegments.Add(merged);

                allConsumed.AddRange(toMerge);
                totalMerged += toMerge.Count;
            }

            if (totalMerged > 0)
            {
                // Clean up files for segments that were consumed by merges and
                // are no longer referenced by any active segment.
                var activeSegments = new HashSet<string>(
                    writer.CommittedSegments.Select(static s => s.SegmentId), StringComparer.Ordinal);
                foreach (var seg in allConsumed)
                {
                    if (!activeSegments.Contains(seg.SegmentId) &&
                        !protectedSegments.Contains(seg.SegmentId))
                    {
                        lastMerger!.CleanupSegmentFiles(seg);
                    }
                }

                writer.ContentToken++;
                writer.CommitGeneration++;
                WriteCommitStats(writer);
                WriteCommitFile(writer);
                writer.Config.DeletionPolicy.OnCommit(dirPath, writer.CommitGeneration, protectedSegments);
            }
        }
        return totalMerged;
    }

    public static int PrepareCommit(IndexWriter writer)
    {
        WaitForBackgroundMerges(writer);
        lock (writer.MergeIoLock)
        lock (writer.WriteLock)
        {
            var (rollbackSegments, rollbackContentToken) = CapturePublishedState(writer);

            DwptManager.WaitForPendingFlushes(writer);
            DwptManager.FlushDwptPool(writer);

            IndexWriter.FlushSegmentStatic(writer);

            if (writer.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    writer.DeleteQueue, writer.CommittedSegments,
                    writer.Directory, writer.CommitGeneration,
                    writer.Config.DurableCommits, writer.Config.Metrics);

            if (writer.ContentChangedSinceCommit)
                writer.ContentToken++;

            int gen = writer.CommitGeneration + 1;
            WriteCommitStats(writer);
            WriteCommitFile(writer, pending: true, generationOverride: gen);
            writer.ContentChangedSinceCommit = false;

            writer.PreparedGeneration = gen;
            writer.PreparedSegments = rollbackSegments;
            writer.PreparedContentToken = writer.ContentToken;
            writer.PreparedRollbackContentToken = rollbackContentToken;

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
        try { FileOpenRetry.Delete(pendingPath); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "rollback pending-file delete"); }

        if (writer.PreparedSegments is not null)
        {
            var rollbackSegments = writer.PreparedSegments;
            var rollbackIds = new HashSet<string>(
                rollbackSegments.Select(static s => s.SegmentId),
                StringComparer.Ordinal);
            foreach (var seg in writer.CommittedSegments)
            {
                if (!rollbackIds.Contains(seg.SegmentId))
                    DeleteSegmentFiles(seg.SegmentId, writer.Directory);
            }

            writer.CommittedSegments.Clear();
            writer.CommittedSegments.AddRange(rollbackSegments);
            foreach (var segment in rollbackSegments)
                segment.WriteTo(Path.Combine(directoryPath, segment.SegmentId + ".seg"));
        }

        writer.ContentToken = writer.PreparedRollbackContentToken;
        writer.ContentChangedSinceCommit = false;
        writer.PreparedGeneration = -1;
        writer.PreparedSegments = null;
    }

    private static SegmentInfo CloneSegmentInfo(SegmentInfo segment) => new()
    {
        SegmentId = segment.SegmentId,
        DocCount = segment.DocCount,
        LiveDocCount = segment.LiveDocCount,
        TotalBytes = segment.TotalBytes,
        CodecBytes = new Dictionary<string, long>(segment.CodecBytes, StringComparer.Ordinal),
        CommitGeneration = segment.CommitGeneration,
        IsCompoundFile = segment.IsCompoundFile,
        FieldNames = [.. segment.FieldNames],
        IndexSortFields = segment.IndexSortFields is null ? null : [.. segment.IndexSortFields],
        VectorFields = [.. segment.VectorFields],
        DelGeneration = segment.DelGeneration,
        MinSequenceNumber = segment.MinSequenceNumber,
        MaxSequenceNumber = segment.MaxSequenceNumber,
        EarliestSoftDeleteTimestamp = segment.EarliestSoftDeleteTimestamp
    };

    private static (List<SegmentInfo> Segments, long ContentToken) CapturePublishedState(IndexWriter writer)
    {
        var recovery = IndexRecovery.RecoverLatestCommit(
            writer.Directory.DirectoryPath,
            cleanupOrphans: false,
            writer.Config.CodecCatalog);
        if (recovery is null)
            return ([], 0);

        var currentSegments = writer.CommittedSegments.ToDictionary(
            static segment => segment.SegmentId,
            StringComparer.Ordinal);
        var publishedSegments = new List<SegmentInfo>(recovery.SegmentIds.Count);
        foreach (string segmentId in recovery.SegmentIds)
        {
            if (currentSegments.TryGetValue(segmentId, out var segment))
            {
                publishedSegments.Add(CloneSegmentInfo(segment));
                continue;
            }

            var segmentPath = Path.Combine(
                writer.Directory.DirectoryPath,
                segmentId + ".seg");
            publishedSegments.Add(SegmentInfo.ReadFrom(segmentPath));
        }

        return (publishedSegments, recovery.ContentToken);
    }

    private static void WaitForBackgroundMerges(IndexWriter writer)
    {
        Task? pending;
        lock (writer.MergeLock)
            pending = writer.MergeTask;
        pending?.GetAwaiter().GetResult();
    }

    private static void CleanupObsoleteMergeSegments(IndexWriter writer)
    {
        if (writer.ObsoleteMergeSegments.Count == 0)
            return;
        foreach (string segmentId in writer.ObsoleteMergeSegments)
            DeleteSegmentFiles(segmentId, writer.Directory);
        writer.ObsoleteMergeSegments.Clear();
    }
}
