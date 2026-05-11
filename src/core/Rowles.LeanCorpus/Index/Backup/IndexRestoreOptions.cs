namespace Rowles.LeanCorpus.Index.Backup;

/// <summary>
/// Configures index restore operations.
/// </summary>
public sealed class IndexRestoreOptions
{
    /// <summary>Gets or sets whether an existing target index directory may be cleared before files are restored.</summary>
    public bool OverwriteTargetDirectory { get; set; }

    /// <summary>Gets or sets whether the restored index is validated after restore. Defaults to <c>true</c>.</summary>
    public bool ValidateAfterRestore { get; set; } = true;

    /// <summary>Gets or sets whether commit statistics are restored when present in the manifest. Defaults to <c>true</c>.</summary>
    public bool RestoreCommitStats { get; set; } = true;
}
