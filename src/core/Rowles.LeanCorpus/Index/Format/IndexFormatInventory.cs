namespace Rowles.LeanCorpus.Index.Format;

/// <summary>
/// Describes the on-disk format state of a LeanCorpus index directory.
/// </summary>
public sealed record IndexFormatInventory
{
    /// <summary>Gets the inspected directory path.</summary>
    public required string DirectoryPath { get; init; }

    /// <summary>Gets the latest commit generation, or <c>null</c> when no readable commit was found.</summary>
    public int? CommitGeneration { get; init; }

    /// <summary>Gets the latest commit content token, or <c>null</c> when no readable commit was found.</summary>
    public long? ContentToken { get; init; }

    /// <summary>Gets segment IDs referenced by the latest readable commit.</summary>
    public required IReadOnlyList<string> SegmentIds { get; init; }

    /// <summary>Gets inspected segment inventories.</summary>
    public required IReadOnlyList<SegmentFormatInventory> Segments { get; init; }

    /// <summary>Gets recognised files that are not referenced by the latest readable commit.</summary>
    public required IReadOnlyList<CodecFileInventory> OrphanFiles { get; init; }

    /// <summary>Gets issues detected while inspecting commit and segment metadata.</summary>
    public required IReadOnlyList<IndexCheckIssue> Issues { get; init; }

    /// <summary>Gets a value indicating whether any file uses a future unsupported codec version.</summary>
    public required bool HasUnsupportedFutureFormat { get; init; }
}
