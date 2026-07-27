using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>
/// Defines a custom scoring pipeline over candidates from an executable approximation query.
/// </summary>
public abstract class Weight
{
    /// <summary>Gets the built-in query used to produce candidate documents.</summary>
    public Query Approximation { get; }

    /// <summary>Initialises a weight with its candidate approximation.</summary>
    protected Weight(Query approximation)
    {
        Approximation = approximation ?? throw new ArgumentNullException(nameof(approximation));
    }

    /// <summary>Creates the scorer used for one search operation.</summary>
    public abstract Scorer CreateScorer(IndexSearcher searcher);
}
