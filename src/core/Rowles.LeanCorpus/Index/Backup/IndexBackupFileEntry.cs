namespace Rowles.LeanCorpus.Index.Backup;

/// <summary>
/// Describes one file captured in an index backup manifest.
/// </summary>
public sealed class IndexBackupFileEntry
{
    /// <summary>Gets the file name relative to the backup or index directory.</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Gets the file length in bytes.</summary>
    public long Length { get; init; }

    /// <summary>Gets the CRC-32 checksum of the file contents.</summary>
    public uint Crc32 { get; init; }

    /// <summary>Gets the owning segment ID, or <c>null</c> for commit-wide files.</summary>
    public string? SegmentId { get; init; }

    /// <summary>Gets the file role within the backup.</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>Gets a value indicating whether the file is required for the selected commit.</summary>
    public bool IsRequired { get; init; }

    /// <summary>Gets a value indicating whether this entry is the selected <c>segments_N</c> commit file.</summary>
    public bool IsCommitFile { get; init; }
}
