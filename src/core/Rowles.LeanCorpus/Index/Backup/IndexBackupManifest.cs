namespace Rowles.LeanCorpus.Index.Backup;

/// <summary>
/// Describes a complete, restorable backup of a single LeanCorpus commit point.
/// </summary>
public sealed class IndexBackupManifest
{
    /// <summary>Gets the manifest format version.</summary>
    public string FormatVersion { get; init; } = string.Empty;

    /// <summary>Gets the backed-up commit generation.</summary>
    public int CommitGeneration { get; init; }

    /// <summary>Gets the index content token recorded in the backed-up commit.</summary>
    public long ContentToken { get; init; }

    /// <summary>Gets the UTC time at which the manifest was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>Gets the selected <c>segments_N</c> commit file name.</summary>
    public string CommitFileName { get; init; } = string.Empty;

    /// <summary>Gets the files required to restore the backed-up commit point.</summary>
    public List<IndexBackupFileEntry> Files { get; init; } = [];
}
