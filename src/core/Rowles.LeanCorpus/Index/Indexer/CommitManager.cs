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
/// recovery. All state is passed explicitly — no coupling back to <see cref="IndexWriter"/>.
/// </summary>
internal static class CommitManager
{
    public static void CommitWithLocks(
        CommitState state,
        MMapDirectory directory,
        IndexWriterConfig config,
        DocumentBufferState buffer,
        Lock writeLock,
        Lock mergeIoLock,
        Lock mergeLock,
        DwptState? dwptState,
        SnapshotState snapshotState,
        MergeState mergeState,
        SemaphoreSlim? backpressureSemaphore,
        ref int semaphoreSlotsHeld)
    {
        // Lock ordering: _mergeIoLock first (so a running merge can finish before we
        // mutate .del files), then _writeLock. AddDocument holds only _writeLock and
        // continues to run while a merge IO phase is in progress.
        lock (mergeIoLock)
        lock (writeLock)
        {
            if (state.PreparedGeneration >= 0)
            {
                PublishPreparedCommit(state, directory, config, mergeState, snapshotState, writeLock);
                return;
            }

            using var activity = Diagnostics.LeanCorpusActivitySource.Source
                .StartActivity(Diagnostics.LeanCorpusActivitySource.Commit);
            activity?.SetTag("index.commit_generation", state.CommitGeneration + 1);

            var sw = Stopwatch.StartNew();
            CommitCore(state, directory, config, buffer, dwptState, snapshotState, mergeState,
                backpressureSemaphore, ref semaphoreSlotsHeld, writeLock);
            sw.Stop();
            config.Metrics.RecordCommit(sw.Elapsed);

            activity?.SetTag("index.segment_count", state.CommittedSegments.Count);
        }

        // Wait for any background merge triggered by CommitCore to finish so the
        // on-disk state is consistent before a reader opens the index.
        // Read mergeState.MergeTask AFTER releasing locks — CommitCore may have
        // scheduled a new merge that hasn't started yet.
        Task? merge;
        lock (mergeLock) { merge = mergeState.MergeTask; }
        if (merge is { IsCompleted: false })
        {
            try { merge.Wait(); }
            catch (AggregateException) { }
        }
    }

    private static void PublishPreparedCommit(
        CommitState state,
        MMapDirectory directory,
        IndexWriterConfig config,
        MergeState mergeState,
        SnapshotState snapshotState,
        Lock writeLock)
    {
        var dirPath = directory.DirectoryPath;
        var pendingPath = Path.Combine(dirPath, $"segments_{state.PreparedGeneration}.pending");
        var finalPath = Path.Combine(dirPath, $"segments_{state.PreparedGeneration}");

        File.Move(pendingPath, finalPath, overwrite: false);

        state.CommitGeneration = state.PreparedGeneration;
        state.ContentToken = state.PreparedContentToken;
        state.ContentChangedSinceCommit = false;

        WriteCommitStats(state, directory);
        config.DeletionPolicy.OnCommit(dirPath, state.CommitGeneration,
            SnapshotManager.GetSnapshotProtectedSegments(snapshotState));

        MergeScheduler.ScheduleBackgroundMerge(mergeState, directory, config, snapshotState, state, writeLock);

        state.PreparedGeneration = -1;
        state.PreparedSegments = null;
    }

    private static void CommitCore(
        CommitState state,
        MMapDirectory directory,
        IndexWriterConfig config,
        DocumentBufferState buffer,
        DwptState? dwptState,
        SnapshotState snapshotState,
        MergeState mergeState,
        SemaphoreSlim? backpressureSemaphore,
        ref int semaphoreSlotsHeld,
        Lock writeLock)
    {
        var preFlushSegmentCount = state.CommittedSegments.Count;

        if (preFlushSegmentCount > 0 && state.PendingDeletes.Count > 0)
            DeletionApplier.ApplyPendingDeletions(
                state.PendingDeletes, state.CommittedSegments.GetRange(0, preFlushSegmentCount),
                directory, state.CommitGeneration,
                config.DurableCommits);

        if (dwptState is not null)
            DwptManager.FlushDwptPool(dwptState, directory, config, state);

        IndexWriter.FlushSegmentStatic(buffer, config, directory.DirectoryPath, state,
            backpressureSemaphore, ref semaphoreSlotsHeld);

        if (state.PendingDeletes.Count > 0)
            DeletionApplier.ApplyPendingDeletions(
                state.PendingDeletes, state.CommittedSegments,
                directory, state.CommitGeneration,
                config.DurableCommits);

        if (state.ContentChangedSinceCommit)
            state.ContentToken++;

        state.CommitGeneration++;
        WriteCommitFile(state, directory, config);
        state.ContentChangedSinceCommit = false;

        WriteCommitStats(state, directory);
        config.DeletionPolicy.OnCommit(directory.DirectoryPath, state.CommitGeneration,
            SnapshotManager.GetSnapshotProtectedSegments(snapshotState));

        MergeScheduler.ScheduleBackgroundMerge(mergeState, directory, config, snapshotState, state, writeLock);
    }

    public static void WriteCommitFile(CommitState state, MMapDirectory directory, IndexWriterConfig config, bool pending = false, int? generationOverride = null)
    {
        int gen = generationOverride ?? state.CommitGeneration;
        var dirPath = directory.DirectoryPath;
        var commitFile = Path.Combine(dirPath, $"segments_{gen}");
        if (pending)
            commitFile += ".pending";

        var segmentIds = new List<string>(state.CommittedSegments.Count);
        foreach (var seg in state.CommittedSegments)
            segmentIds.Add(seg.SegmentId);
        var commitData = new CommitData
        {
            Segments = segmentIds,
            Generation = gen,
            ContentToken = state.ContentToken
        };
        var commitJson = JsonSerializer.Serialize(commitData, LeanCorpusJsonContext.Default.CommitData);

        var fileContent = CommitFileFormat.Wrap(commitJson);

        if (config.DurableCommits)
        {
            var fsyncCutoff = state.LastCommitFsyncUtc == DateTime.MinValue
                ? DateTime.MinValue
                : state.LastCommitFsyncUtc - TimeSpan.FromSeconds(2);
            foreach (var path in Directory.EnumerateFiles(dirPath))
            {
                var name = Path.GetFileName(path);
                if (name.StartsWith("segments_", StringComparison.Ordinal)) continue;
                if (string.Equals(name, "write.lock", StringComparison.Ordinal)) continue;
                if (name.EndsWith(".tmp", StringComparison.Ordinal)) continue;
                if (fsyncCutoff != DateTime.MinValue && File.GetLastWriteTimeUtc(path) <= fsyncCutoff) continue;
                DirectoryFsync.SyncFile(path, strict: true);
            }

            DirectoryFsync.Sync(dirPath, strict: true);
            IndexAtomicFileWriter.WriteText(commitFile, fileContent, durable: true);
            state.LastCommitFsyncUtc = DateTime.UtcNow;
        }
        else
        {
            IndexAtomicFileWriter.WriteText(commitFile, fileContent, durable: false);
        }
    }

    public static void WriteCommitStats(CommitState state, MMapDirectory directory)
    {
        var dirPath = directory.DirectoryPath;
        int totalDocCount = 0;
        int liveDocCount = 0;
        var fieldLengthSums = new Dictionary<string, long>(StringComparer.Ordinal);
        var fieldDocCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var seg in state.CommittedSegments)
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

            AccumulateSegmentStatsByScan(seg, directory, fieldLengthSums, fieldDocCounts,
                ref totalDocCount, ref liveDocCount);
        }

        var avgFieldLengths = new Dictionary<string, float>(StringComparer.Ordinal);
        foreach (var (field, sum) in fieldLengthSums)
        {
            int count = fieldDocCounts.GetValueOrDefault(field, 1);
            avgFieldLengths[field] = count > 0 ? (float)sum / count : 1.0f;
        }

        var stats = new IndexStats(totalDocCount, liveDocCount, avgFieldLengths, fieldDocCounts, fieldLengthSums);
        stats.WriteTo(IndexStats.GetStatsPath(dirPath, state.CommitGeneration));
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
        using var reader = new SegmentReader(directory, segment);
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

    public static void LoadLatestCommit(
        CommitState state,
        MMapDirectory directory,
        IndexWriterConfig config)
    {
        IndexOpenGuard.EnsureNoBlockingMigration(directory, config.CompatibilityMode);
        var recovery = IndexRecovery.RecoverLatestCommit(directory.DirectoryPath);
        if (recovery is null) return;
        IndexOpenGuard.EnsureCanOpenSegments(directory, recovery.SegmentIds, config.CompatibilityMode, forWriting: true);

        state.CommitGeneration = recovery.Generation;
        state.ContentToken = recovery.ContentToken;
        state.NextSegmentOrdinal = recovery.SegmentIds.Count;

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

            state.CommittedSegments.Add(seg);
        }

        if (config.TrackSequenceNumbers)
        {
            long maxSeq = 0;
            foreach (var seg in state.CommittedSegments)
            {
                if (seg.MaxSequenceNumber.HasValue && seg.MaxSequenceNumber.Value > maxSeq)
                    maxSeq = seg.MaxSequenceNumber.Value;
            }
            state.NextSequenceNumber = maxSeq + 1;
            state.FlushSeqNoStart = state.NextSequenceNumber;
        }
    }

    public static void DeleteSegmentFiles(string segId, string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath, segId + ".*"))
        {
            try { File.Delete(file); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "segment file delete"); }
        }
        foreach (var file in Directory.GetFiles(directoryPath, segId + "_v_*.*"))
        {
            try { File.Delete(file); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "vector file delete"); }
        }
    }

    public static int CompactWithLocks(
        CommitState state,
        MMapDirectory directory,
        IndexWriterConfig config,
        DocumentBufferState buffer,
        Lock writeLock,
        Lock mergeIoLock,
        DwptState? dwptState,
        SnapshotState snapshotState,
        SemaphoreSlim? backpressureSemaphore,
        ref int semaphoreSlotsHeld)
    {
        lock (mergeIoLock)
        lock (writeLock)
        {
            var dirPath = directory.DirectoryPath;

            if (state.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    state.PendingDeletes, state.CommittedSegments,
                    directory, state.CommitGeneration,
                    config.DurableCommits);

            if (buffer.DocCount > 0)
                IndexWriter.FlushSegmentStatic(buffer, config, dirPath, state,
                    backpressureSemaphore, ref semaphoreSlotsHeld);

            if (state.CommittedSegments.Count <= 1)
                return 0;

            var segmentsToMerge = state.CommittedSegments.ToList();
            var protectedSegments = SnapshotManager.GetSnapshotProtectedSegments(snapshotState);

            var mergeable = segmentsToMerge
                .Where(s => !protectedSegments.Contains(s.SegmentId))
                .ToList();

            if (mergeable.Count < 2)
                return 0;

            int mergeableCount = mergeable.Count;

            var merger = new SegmentMerger(directory, config.MergePolicy, config.PostingsSkipInterval,
                config.SoftDeleteRetentionSeconds);
            int localOrdinal = state.NextSegmentOrdinal;
            var merged = merger.MergeAll(mergeable, ref localOrdinal);

            if (merged is null)
            {
                foreach (var seg in mergeable)
                    state.CommittedSegments.Remove(seg);
            }
            else
            {
                foreach (var seg in mergeable)
                    state.CommittedSegments.Remove(seg);
                state.CommittedSegments.Add(merged);
            }

            state.ContentToken++;
            state.CommitGeneration++;
            state.NextSegmentOrdinal = Math.Max(state.NextSegmentOrdinal, localOrdinal);
            WriteCommitFile(state, directory, config);
            WriteCommitStats(state, directory);
            config.DeletionPolicy.OnCommit(dirPath, state.CommitGeneration, protectedSegments);

            var activeSegments = new HashSet<string>(
                state.CommittedSegments.Select(static s => s.SegmentId), StringComparer.Ordinal);
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

    public static int ForceMerge(
        CommitState state,
        MMapDirectory directory,
        IndexWriterConfig config,
        DocumentBufferState buffer,
        Lock writeLock,
        Lock mergeIoLock,
        SnapshotState snapshotState,
        SemaphoreSlim? backpressureSemaphore,
        ref int semaphoreSlotsHeld,
        int maxSegments)
    {
        int totalMerged = 0;
        lock (mergeIoLock)
        lock (writeLock)
        {
            var dirPath = directory.DirectoryPath;

            if (state.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    state.PendingDeletes, state.CommittedSegments,
                    directory, state.CommitGeneration,
                    config.DurableCommits);

            if (buffer.DocCount > 0)
                IndexWriter.FlushSegmentStatic(buffer, config, dirPath, state,
                    backpressureSemaphore, ref semaphoreSlotsHeld);

            var protectedSegments = SnapshotManager.GetSnapshotProtectedSegments(snapshotState);

            while (state.CommittedSegments.Count > maxSegments)
            {
                var mergeable = state.CommittedSegments
                    .Where(s => !protectedSegments.Contains(s.SegmentId))
                    .ToList();

                if (mergeable.Count < 2)
                    break;

                mergeable.Sort(static (a, b) => a.DocCount.CompareTo(b.DocCount));
                int count = Math.Min(mergeable.Count, state.CommittedSegments.Count - maxSegments + 1);
                var toMerge = mergeable.GetRange(0, count);

                var merger = new SegmentMerger(directory, config.MergePolicy, config.PostingsSkipInterval,
                    config.SoftDeleteRetentionSeconds);
                int localOrdinal = state.NextSegmentOrdinal;
                var merged = merger.MergeAll(toMerge, ref localOrdinal);
                state.NextSegmentOrdinal = Math.Max(state.NextSegmentOrdinal, localOrdinal);

                if (merged is null)
                {
                    foreach (var seg in toMerge)
                        state.CommittedSegments.Remove(seg);
                }
                else
                {
                    foreach (var seg in toMerge)
                        state.CommittedSegments.Remove(seg);
                    state.CommittedSegments.Add(merged);
                }

                totalMerged += toMerge.Count;
            }

            if (totalMerged > 0)
            {
                state.ContentToken++;
                state.CommitGeneration++;
                WriteCommitFile(state, directory, config);
                WriteCommitStats(state, directory);
                config.DeletionPolicy.OnCommit(dirPath, state.CommitGeneration, protectedSegments);
            }
        }
        return totalMerged;
    }

    public static int PrepareCommit(
        CommitState state,
        MMapDirectory directory,
        IndexWriterConfig config,
        DocumentBufferState buffer,
        Lock writeLock,
        Lock mergeIoLock,
        DwptState? dwptState,
        SemaphoreSlim? backpressureSemaphore,
        ref int semaphoreSlotsHeld)
    {
        lock (mergeIoLock)
        lock (writeLock)
        {
            var preFlushSegmentCount = state.CommittedSegments.Count;
            if (preFlushSegmentCount > 0 && state.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    state.PendingDeletes, state.CommittedSegments.GetRange(0, preFlushSegmentCount),
                    directory, state.CommitGeneration,
                    config.DurableCommits);

            if (dwptState is not null)
                DwptManager.FlushDwptPool(dwptState, directory, config, state);

            IndexWriter.FlushSegmentStatic(buffer, config, directory.DirectoryPath, state,
                backpressureSemaphore, ref semaphoreSlotsHeld);

            if (state.PendingDeletes.Count > 0)
                DeletionApplier.ApplyPendingDeletions(
                    state.PendingDeletes, state.CommittedSegments,
                    directory, state.CommitGeneration,
                    config.DurableCommits);

            if (state.ContentChangedSinceCommit)
                state.ContentToken++;

            int gen = state.CommitGeneration + 1;
            WriteCommitFile(state, directory, config, pending: true, generationOverride: gen);
            state.ContentChangedSinceCommit = false;

            state.PreparedGeneration = gen;
            state.PreparedSegments = new List<SegmentInfo>(state.CommittedSegments);
            state.PreparedContentToken = state.ContentToken;

            return gen;
        }
    }

    public static void RollbackPrepared(
        CommitState state,
        string directoryPath)
    {
        if (state.PreparedGeneration < 0)
            return;

        var pendingPath = Path.Combine(directoryPath,
            $"segments_{state.PreparedGeneration}.pending");
        try { File.Delete(pendingPath); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "rollback pending-file delete"); }

        if (state.PreparedSegments is not null)
        {
            var committedIds = new HashSet<string>(
                state.CommittedSegments.Select(static s => s.SegmentId),
                StringComparer.Ordinal);
            foreach (var seg in state.PreparedSegments)
            {
                if (!committedIds.Contains(seg.SegmentId))
                {
                    state.CommittedSegments.Remove(seg);
                    DeleteSegmentFiles(seg.SegmentId, directoryPath);
                }
            }
        }

        state.PreparedGeneration = -1;
        state.PreparedSegments = null;
    }
}
