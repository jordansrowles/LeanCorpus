using Rowles.LeanCorpus.Index.Backup;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Manages index snapshots, NRT segment access, and backup operations.
/// All methods are static — operates on <see cref="SnapshotState"/> and parameters.
/// </summary>
internal static class SnapshotManager
{
    public static HashSet<string> GetSnapshotProtectedSegments(SnapshotState state)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snap in state.HeldSnapshots)
        {
            foreach (var seg in snap.Segments)
                ids.Add(seg.SegmentId);
        }
        return ids;
    }

    public static IReadOnlyList<SegmentInfo> GetNrtSegments(
        SnapshotState snapshotState,
        CommitState commitState,
        DocumentBufferState buffer,
        IndexWriterConfig config,
        string directoryPath,
        Lock writeLock,
        SemaphoreSlim? backpressureSemaphore,
        ref int semaphoreSlotsHeld)
    {
        lock (writeLock)
        {
            if (buffer.DocCount > 0)
                IndexWriter.FlushSegmentStatic(buffer, config, directoryPath, commitState,
                    backpressureSemaphore, ref semaphoreSlotsHeld);
            return commitState.CommittedSegments.ToList().AsReadOnly();
        }
    }

    public static IndexSnapshot CreateSnapshot(
        SnapshotState snapshotState,
        CommitState commitState,
        DocumentBufferState buffer,
        IndexWriterConfig config,
        string directoryPath,
        Lock writeLock,
        SemaphoreSlim? backpressureSemaphore,
        ref int semaphoreSlotsHeld)
    {
        lock (writeLock)
        {
            if (buffer.DocCount > 0)
                IndexWriter.FlushSegmentStatic(buffer, config, directoryPath, commitState,
                    backpressureSemaphore, ref semaphoreSlotsHeld);

            var snapshot = new IndexSnapshot(
                commitState.CommitGeneration,
                commitState.CommittedSegments.Select(s => new SegmentInfo
                {
                    SegmentId = s.SegmentId,
                    DocCount = s.DocCount,
                    LiveDocCount = s.LiveDocCount,
                    CommitGeneration = s.CommitGeneration,
                    DelGeneration = s.DelGeneration,
                    FieldNames = [.. s.FieldNames],
                    IndexSortFields = s.IndexSortFields is null ? null : [.. s.IndexSortFields],
                    VectorFields = [.. s.VectorFields]
                }).ToList().AsReadOnly());

            snapshotState.HeldSnapshots.Add(snapshot);
            return snapshot;
        }
    }

    public static void ReleaseSnapshot(SnapshotState state, IndexSnapshot snapshot, Lock writeLock)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (writeLock)
        {
            state.HeldSnapshots.Remove(snapshot);
        }
    }

    public static IndexBackupManifest CreateBackupManifest(
        IndexSnapshot snapshot,
        string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return IndexBackup.CreateManifest(
            directoryPath,
            new IndexBackupOptions { CommitGeneration = snapshot.CommitGeneration });
    }

    public static IndexBackupResult BackupSnapshot(
        IndexSnapshot snapshot,
        string backupDirectoryPath,
        string directoryPath,
        IndexBackupOptions? options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var effectiveOptions = new IndexBackupOptions
        {
            CommitGeneration = snapshot.CommitGeneration,
            OverwriteBackupDirectory = options?.OverwriteBackupDirectory ?? false,
            IncludeCommitStats = options?.IncludeCommitStats ?? true
        };
        return IndexBackup.Backup(directoryPath, backupDirectoryPath, effectiveOptions);
    }
}
