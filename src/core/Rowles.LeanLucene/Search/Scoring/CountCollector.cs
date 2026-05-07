namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// A simple count-only collector that tracks hit count without storing results.
/// Useful for count queries where actual documents are not needed.
/// </summary>
public sealed class CountCollector : ICollector
{
    /// <inheritdoc/>
    public int TotalHits { get; private set; }

    /// <inheritdoc/>
    public void Collect(int docId, float score) => TotalHits++;
}
