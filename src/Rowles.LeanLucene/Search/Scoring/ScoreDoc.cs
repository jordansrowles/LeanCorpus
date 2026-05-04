namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// Represents a scored document in search results.
/// </summary>
/// <param name="DocId">The zero-based internal document identifier.</param>
/// <param name="Score">The relevance score assigned to this document.</param>
public readonly record struct ScoreDoc(int DocId, float Score)
{
    /// <summary>Approximate size of a single <see cref="ScoreDoc"/> in bytes (4 + 4 plus alignment).</summary>
    internal const int EstimatedBytes = 12;
}
