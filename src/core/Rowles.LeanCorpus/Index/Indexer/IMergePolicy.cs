namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Controls which segments are selected for merging during indexing.
/// </summary>
public interface IMergePolicy
{
    /// <summary>
    /// Returns the segments that should be merged from the given list.
    /// Returns an empty list if no merge is needed.
    /// The caller is responsible for executing the merge and replacing the
    /// returned segments with the merged result.
    /// </summary>
    /// <param name="segments">All currently committed segments.</param>
    /// <param name="protectedSegmentIds">
    /// Segment IDs protected by held snapshots. Must not be included in the result.
    /// </param>
    /// <returns>
    /// The segments to merge, or an empty collection if no merge should be performed.
    /// Segments are ordered for deterministic merge output.
    /// </returns>
    IReadOnlyList<SegmentInfo> FindMerges(
        IReadOnlyList<SegmentInfo> segments,
        IReadOnlySet<string> protectedSegmentIds);
}
