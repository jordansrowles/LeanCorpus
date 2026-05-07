namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// Abstraction for collecting search results. Implementations can count,
/// aggregate, or collect in custom ways beyond the default TopN scoring.
/// </summary>
public interface ICollector
{
    /// <summary>Accepts a matching document with its score.</summary>
    void Collect(int docId, float score);

    /// <summary>Total number of matching documents seen so far.</summary>
    int TotalHits { get; }
}
