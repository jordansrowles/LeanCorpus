using Rowles.LeanCorpus.Index.Backup;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Manages index snapshots, NRT segment access, and backup operations.
/// All methods are static — operates via a single <see cref="IndexWriter"/> parameter.
/// </summary>
internal static class SnapshotManager
{
    public static HashSet<string> GetSnapshotProtectedSegments(IndexWriter writer)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snap in writer.HeldSnapshots)
        {
            foreach (var seg in snap.Segments)
                ids.Add(seg.SegmentId);
        }
        return ids;
    }

    public static IReadOnlyList<SegmentInfo> GetNrtSegments(IndexWriter writer)
    {
        lock (writer.WriteLock)
        {
            if (writer.Buffer.DocCount > 0)
                IndexWriter.FlushSegmentStatic(writer);
            return writer.CommittedSegments.ToList().AsReadOnly();
        }
    }

    public static IndexSnapshot CreateSnapshot(IndexWriter writer)
    {
        lock (writer.WriteLock)
        {
            if (writer.Buffer.DocCount > 0)
                IndexWriter.FlushSegmentStatic(writer);

            var snapshot = new IndexSnapshot(
                writer.CommitGeneration,
                writer.CommittedSegments.Select(s => new SegmentInfo
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

            writer.HeldSnapshots.Add(snapshot);
            return snapshot;
        }
    }

    public static void ReleaseSnapshot(IndexWriter writer, IndexSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (writer.WriteLock)
        {
            writer.HeldSnapshots.Remove(snapshot);
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
