namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// A simple count-only collector that tracks hit count without storing results.
/// Useful for count queries where actual documents are not needed.
/// </summary>
public struct CountCollector : ICollector
{
    /// <summary>The total number of matching documents.</summary>
    public int TotalHits { get; set; }

    /// <inheritdoc/>
    public void Collect(int docId, float score) => TotalHits++;
}
