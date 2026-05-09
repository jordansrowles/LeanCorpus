namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>
/// Encapsulates the results of a search query.
/// </summary>
public sealed class TopDocs
{
    /// <summary>Gets the total number of documents that matched the query (may be larger than <see cref="ScoreDocs"/>).</summary>
    public int TotalHits { get; }

    /// <summary>Gets the array of scored documents in descending score order.</summary>
    public ScoreDoc[] ScoreDocs { get; }

    /// <summary>
    /// Gets a value indicating whether the search terminated before all segments were
    /// scored, due to a <c>SearchOptions.Timeout</c> deadline or cancellation.
    /// When true, <see cref="ScoreDocs"/> and <see cref="TotalHits"/> reflect
    /// only the work completed before termination.
    /// </summary>
    public bool IsPartial { get; }

    /// <summary>Initialises a new <see cref="TopDocs"/> with the given total hit count and scored results.</summary>
    /// <param name="totalHits">Total number of matching documents across the entire index.</param>
    /// <param name="scoreDocs">Scored documents, typically the top-N by relevance.</param>
    public TopDocs(int totalHits, ScoreDoc[] scoreDocs)
        : this(totalHits, scoreDocs, isPartial: false)
    {
    }

    /// <summary>
    /// Initialises a new <see cref="TopDocs"/> with the given total hit count, scored results,
    /// and partial-result indicator.
    /// </summary>
    /// <param name="totalHits">Total number of matching documents seen before termination.</param>
    /// <param name="scoreDocs">Scored documents, typically the top-N by relevance.</param>
    /// <param name="isPartial">True if the search terminated early due to timeout or budget.</param>
    public TopDocs(int totalHits, ScoreDoc[] scoreDocs, bool isPartial)
    {
        TotalHits = totalHits;
        ScoreDocs = scoreDocs;
        IsPartial = isPartial;
    }

    /// <summary>Gets an empty <see cref="TopDocs"/> with zero hits.</summary>
    public static TopDocs Empty => new(0, []);

    /// <summary>Returns a copy of this <see cref="TopDocs"/> with <see cref="IsPartial"/> set to true.</summary>
    internal TopDocs AsPartial() => IsPartial ? this : new TopDocs(TotalHits, ScoreDocs, isPartial: true);
}
