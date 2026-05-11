namespace Rowles.LeanCorpus.Index.Backup;

/// <summary>
/// Represents the result of restoring an index backup.
/// </summary>
public sealed class IndexRestoreResult
{
    /// <summary>Gets the manifest used for restore.</summary>
    public required IndexBackupManifest Manifest { get; init; }

    /// <summary>Gets the full path of the restored index directory.</summary>
    public required string TargetDirectoryPath { get; init; }

    /// <summary>Gets the files restored into the target index directory.</summary>
    public required IReadOnlyList<string> RestoredFiles { get; init; }

    /// <summary>Gets the validation result produced after restore, or <c>null</c> when validation was skipped.</summary>
    public IndexCheckResult? ValidationResult { get; init; }
}
