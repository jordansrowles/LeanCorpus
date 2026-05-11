namespace Rowles.LeanCorpus.Index.Backup;

/// <summary>
/// Represents the result of creating an index backup.
/// </summary>
public sealed class IndexBackupResult
{
    /// <summary>Gets the manifest written to the backup directory.</summary>
    public required IndexBackupManifest Manifest { get; init; }

    /// <summary>Gets the full path of the backup directory.</summary>
    public required string BackupDirectoryPath { get; init; }

    /// <summary>Gets the files copied into the backup directory.</summary>
    public required IReadOnlyList<string> CopiedFiles { get; init; }
}
