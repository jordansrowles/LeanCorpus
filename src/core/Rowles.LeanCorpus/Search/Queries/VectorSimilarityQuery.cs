namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>
/// Vector search that returns only documents meeting a minimum similarity, subject to a
/// hard result cap.
/// </summary>
public sealed class VectorSimilarityQuery : VectorQuery
{
    /// <summary>Minimum inclusive similarity required for a result.</summary>
    public float MinimumSimilarity { get; }

    /// <summary>
    /// Initialises a thresholded vector search.
    /// </summary>
    /// <param name="field">Vector field to search.</param>
    /// <param name="queryVector">Non-empty finite query vector.</param>
    /// <param name="minimumSimilarity">Minimum inclusive score accepted after reranking.</param>
    /// <param name="maxResults">Hard upper bound on returned results and base candidate demand.</param>
    /// <param name="efSearch">HNSW search candidate pool, or zero for the automatic value.</param>
    /// <param name="oversamplingFactor">Shortlist multiplier before reranking.</param>
    /// <param name="filter">Optional candidate filter.</param>
    public VectorSimilarityQuery(
        string field,
        float[] queryVector,
        float minimumSimilarity,
        int maxResults = 1_000,
        int efSearch = 0,
        int oversamplingFactor = 1,
        Query? filter = null)
        : base(field, queryVector, maxResults, efSearch, oversamplingFactor, filter)
    {
        if (!float.IsFinite(minimumSimilarity))
            throw new ArgumentOutOfRangeException(
                nameof(minimumSimilarity),
                "The minimum similarity must be finite.");
        MinimumSimilarity = minimumSimilarity;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is VectorSimilarityQuery other &&
        base.Equals((object)other) &&
        MinimumSimilarity.Equals(other.MinimumSimilarity);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), MinimumSimilarity);
}
