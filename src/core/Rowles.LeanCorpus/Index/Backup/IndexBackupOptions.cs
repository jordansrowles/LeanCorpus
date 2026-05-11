namespace Rowles.LeanCorpus.Index.Backup;

/// <summary>
/// Configures index backup creation.
/// </summary>
public sealed class IndexBackupOptions
{
    /// <summary>Gets or sets the commit generation to back up. When <c>null</c>, the latest readable commit is used.</summary>
    public int? CommitGeneration { get; set; }

    /// <summary>Gets or sets whether an existing backup directory may be cleared before the backup is written.</summary>
    public bool OverwriteBackupDirectory { get; set; }

    /// <summary>Gets or sets whether <c>stats_N.json</c> is included when present. Defaults to <c>true</c>.</summary>
    public bool IncludeCommitStats { get; set; } = true;
}
