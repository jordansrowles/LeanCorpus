namespace Rowles.LeanCorpus.Index.Format;

/// <summary>
/// Describes format metadata for one segment.
/// </summary>
public sealed record SegmentFormatInventory
{
    /// <summary>Gets the segment ID.</summary>
    public required string SegmentId { get; init; }

    /// <summary>Gets the segment document count, or <c>null</c> when metadata could not be read.</summary>
    public int? DocCount { get; init; }

    /// <summary>Gets the live-document count, or <c>null</c> when metadata could not be read.</summary>
    public int? LiveDocCount { get; init; }

    /// <summary>Gets the segment creation commit generation, or <c>null</c> when metadata could not be read.</summary>
    public int? CommitGeneration { get; init; }

    /// <summary>Gets the live-doc deletion generation, or <c>null</c> for legacy or absent deletion files.</summary>
    public int? DelGeneration { get; init; }

    /// <summary>Gets file inventories for this segment.</summary>
    public required IReadOnlyList<CodecFileInventory> Files { get; init; }

    /// <summary>Gets required files missing from this segment.</summary>
    public required IReadOnlyList<string> MissingFiles { get; init; }

    /// <summary>Gets non-fatal warnings detected while inspecting this segment.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}
