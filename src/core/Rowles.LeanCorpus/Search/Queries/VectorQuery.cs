using Rowles.LeanCorpus.Search.Simd;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>
/// ANN search over vector data using HNSW when available, with a flat SIMD fallback.
/// </summary>
public sealed class VectorQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the query vector used to find approximate nearest neighbours.</summary>
    public float[] QueryVector { get; }

    /// <summary>Gets the maximum number of nearest-neighbour results to return.</summary>
    public int TopK { get; }

    /// <summary>
    /// HNSW search-time candidate pool size (the <c>ef</c> parameter). Larger values increase recall at
    /// the cost of latency. Uses <see cref="LeanCorpusDefaults"/>'s HNSW search default when one is
    /// configured; otherwise defaults to <c>max(64, 4 * topK)</c>. Search raises the effective pool to
    /// the requested oversampled shortlist, capped at the available graph nodes.
    /// </summary>
    public int EfSearch { get; }

    /// <summary>
    /// Candidate oversampling factor. The HNSW shortlist returns
    /// <c>topK * OversamplingFactor</c> candidates which are rescored using the vector field's
    /// active representation. Quantised fields use decoded values, so their scores can differ from
    /// cosine similarity over the original Float32 vectors. Default: 1 (no oversampling).
    /// </summary>
    public int OversamplingFactor { get; }

    /// <summary>Optional pre-filter restricting candidates to documents whose IDs satisfy the predicate.</summary>
    public Query? Filter { get; }

    /// <summary>Initialises a new <see cref="VectorQuery"/> for the given field and query vector.</summary>
    /// <param name="field">The vector field to search.</param>
    /// <param name="queryVector">The query vector for similarity comparison.</param>
    /// <param name="topK">Maximum number of nearest neighbours to return. Default: 10.</param>
    /// <param name="efSearch">HNSW <c>ef</c> parameter. <c>0</c> selects an automatic default.</param>
    /// <param name="oversamplingFactor">Multiplier for the HNSW candidate shortlist before scoring.</param>
    /// <param name="filter">Optional pre-filter query that constrains the candidate set.</param>
    public VectorQuery(
        string field,
        float[] queryVector,
        int topK = 10,
        int efSearch = 0,
        int oversamplingFactor = 1,
        Query? filter = null)
    {
        Field = field;
        QueryVector = queryVector;
        TopK = topK;
        EfSearch = efSearch > 0
            ? efSearch
            : ResolveDefaultEf(topK);
        OversamplingFactor = Math.Max(1, oversamplingFactor);
        Filter = filter;
    }

    private static int ResolveDefaultEf(int topK)
    {
        var configured = LeanCorpusDefaults.GetSnapshot().Search.Hnsw.Ef;
        return configured.IsSet && configured.Value > 0
            ? configured.Value
            : (int)Math.Min(int.MaxValue, Math.Max(64L, 4L * topK));
    }

    internal int GetShortlistSize(int availableCandidates)
    {
        if (availableCandidates <= 0 || TopK <= 0)
            return 0;

        long requested = (long)TopK * OversamplingFactor;
        return (int)Math.Min(requested, availableCandidates);
    }

    internal int GetEffectiveEfSearch(int availableCandidates)
    {
        if (availableCandidates <= 0)
            return 0;

        return Math.Min(Math.Max(EfSearch, GetShortlistSize(availableCandidates)), availableCandidates);
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.VisitLeaf(this);
        Filter?.Visit(visitor.GetSubVisitor(Occur.Must, this));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is VectorQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        TopK == other.TopK && Boost == other.Boost &&
        QueryVector.AsSpan().SequenceEqual(other.QueryVector);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(nameof(VectorQuery));
        h.Add(Field);
        h.Add(TopK);
        // Hash first few elements for performance
        int len = Math.Min(QueryVector.Length, 8);
        for (int i = 0; i < len; i++) h.Add(QueryVector[i]);
        h.Add(QueryVector.Length);
        return CombineBoost(h.ToHashCode());
    }

    /// <summary>
    /// Computes cosine similarity between two vectors using SIMD where available.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
        => SimdVectorOps.CosineSimilarity(a, b);
}
