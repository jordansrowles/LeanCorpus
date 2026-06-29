namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Merge policy that never triggers automatic merges.
/// Use for bulk-load-then-merge-once workflows where all merging is
/// performed manually via <see cref="IndexWriter.Compact"/> or
/// <see cref="IndexWriter.ForceMerge"/>.
/// </summary>
public sealed class NoMergePolicy : IMergePolicy
{
    /// <summary>Singleton instance.</summary>
    public static readonly NoMergePolicy Instance = new();

    private NoMergePolicy() { }

    /// <inheritdoc/>
    public IReadOnlyList<SegmentInfo> FindMerges(
        IReadOnlyList<SegmentInfo> segments,
        IReadOnlySet<string> protectedSegmentIds)
        => Array.Empty<SegmentInfo>();
}
