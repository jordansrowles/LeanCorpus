namespace Rowles.LeanCorpus.Index.Format;

/// <summary>
/// Describes one index file and the codec version detected from its header.
/// </summary>
public sealed record CodecFileInventory
{
    /// <summary>Gets the file name relative to the index directory.</summary>
    public required string FileName { get; init; }

    /// <summary>Gets the file extension.</summary>
    public required string Extension { get; init; }

    /// <summary>Gets the logical codec name.</summary>
    public required string CodecName { get; init; }

    /// <summary>Gets the detected on-disk codec version, or <c>null</c> when the file has no standard codec header.</summary>
    public byte? Version { get; init; }

    /// <summary>Gets the current codec version supported by this build, or <c>null</c> when the file has no standard codec header.</summary>
    public byte? CurrentVersion { get; init; }

    /// <summary>Gets a value indicating whether the file has the expected LeanCorpus codec magic.</summary>
    public required bool HasValidMagic { get; init; }

    /// <summary>Gets a value indicating whether this build can read the detected version.</summary>
    public required bool IsSupported { get; init; }

    /// <summary>Gets a value indicating whether the file is already at the current codec version.</summary>
    public required bool IsCurrent { get; init; }

    /// <summary>Gets the file length in bytes when requested.</summary>
    public long? Length { get; init; }

    /// <summary>Gets the related segment ID, when known.</summary>
    public string? SegmentId { get; init; }

    /// <summary>Gets the related field name, when known.</summary>
    public string? FieldName { get; init; }
}
