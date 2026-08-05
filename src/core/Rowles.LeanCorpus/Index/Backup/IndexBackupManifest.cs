namespace Rowles.LeanCorpus.Index.Backup;

/// <summary>
/// Describes a complete, restorable backup of a single LeanCorpus commit point.
/// </summary>
public sealed class IndexBackupManifest
{
    /// <summary>Gets the manifest format version.</summary>
    public string FormatVersion { get; init; } = string.Empty;

    /// <summary>Gets the kind of backup represented by this manifest.</summary>
    public IndexBackupKind Kind { get; init; } = IndexBackupKind.Full;

    /// <summary>Gets the SHA-256 fingerprint of the immediately preceding manifest, or <c>null</c> for a full backup.</summary>
    public string? ParentManifestSha256 { get; init; }

    /// <summary>Gets the number of manifests in the chain ending at this manifest.</summary>
    public int ChainDepth { get; init; } = 1;

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

/// <summary>Describes whether a backup is self-contained or linked to a previous backup.</summary>
public enum IndexBackupKind
{
    /// <summary>A self-contained backup containing every required file.</summary>
    Full,

    /// <summary>A manifest-linked backup containing only files changed since its parent.</summary>
    Incremental
}
