namespace Rowles.LeanCorpus.Diagnostics;

/// <summary>
/// The candidate route one Float32 vector field segment took for a vector query (ADR052).
/// Every route except <see cref="Graph"/> is an exact scan and never counts as approximate search.
/// </summary>
public enum VectorSearchRoute
{
    /// <summary>The vector graph selected the candidates, which were then rescored exactly.</summary>
    Graph = 0,

    /// <summary>The field has no vector graph in this segment.</summary>
    ExactNoGraph = 1,

    /// <summary>The filter left too few eligible documents for a traversal to pay off.</summary>
    ExactSelectiveFilter = 2,

    /// <summary>The traversal reached its distance-evaluation budget before converging.</summary>
    ExactBudgetExceeded = 3,

    /// <summary>The traversal returned fewer candidates than the query requested.</summary>
    ExactUnderfilled = 4,

    /// <summary>The declared vector graph could not be opened or failed validation.</summary>
    ExactGraphUnavailable = 5,

    /// <summary>
    /// The final score is not monotonic in cosine similarity: a non-positive query boost, per-document
    /// field boosts or a zero query vector on a non-normalised field.
    /// </summary>
    ExactScoreTransform = 6,
}
