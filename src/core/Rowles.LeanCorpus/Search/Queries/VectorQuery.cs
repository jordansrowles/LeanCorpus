using System.Numerics;
using System.Runtime.InteropServices;

using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>
/// ANN search over vector data using HNSW when available, with a flat SIMD fallback.
/// </summary>
public class VectorQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the query vector used to find approximate nearest neighbours.</summary>
    public float[] QueryVector { get; }

    /// <summary>Gets the maximum number of nearest-neighbour results to return.</summary>
    public int TopK { get; }

    /// <summary>
    /// HNSW search-time candidate pool size (the <c>ef</c> parameter). Larger values increase recall at
    /// the cost of latency. Defaults to <c>max(64, 4 * topK)</c>.
    /// </summary>
    public int EfSearch { get; }

    /// <summary>
    /// Exact-rerank oversampling factor. The HNSW shortlist returns <c>topK * OversamplingFactor</c>
    /// candidates which are then exactly rescored. Default: 1 (no oversampling).
    /// </summary>
    public int OversamplingFactor { get; }

    /// <summary>Optional pre-filter restricting candidates to documents whose IDs satisfy the predicate.</summary>
    public Query? Filter { get; }

    /// <summary>
    /// Maximum distinct HNSW layer-zero nodes visited in each segment. Zero means
    /// unbounded. Exhaustion is surfaced by <c>SearchWithDiagnostics</c>.
    /// </summary>
    public int MaxVisitedNodes { get; }

    /// <summary>Gets the bounded HNSW shortlist size requested before reranking.</summary>
    internal int CandidateCount { get; }

    /// <summary>Initialises a new <see cref="VectorQuery"/> for the given field and query vector.</summary>
    /// <param name="field">The vector field to search.</param>
    /// <param name="queryVector">The query vector for similarity comparison.</param>
    /// <param name="topK">Maximum number of nearest neighbours to return. Default: 10.</param>
    /// <param name="efSearch">HNSW <c>ef</c> parameter. <c>0</c> selects an automatic default.</param>
    /// <param name="oversamplingFactor">Multiplier for the HNSW shortlist before exact rerank.</param>
    /// <param name="filter">Optional pre-filter query that constrains the candidate set.</param>
    /// <param name="maxVisitedNodes">Optional per-segment HNSW traversal-node budget.</param>
    public VectorQuery(
        string field,
        float[] queryVector,
        int topK = 10,
        int efSearch = 0,
        int oversamplingFactor = 1,
        Query? filter = null,
        int maxVisitedNodes = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        ArgumentNullException.ThrowIfNull(queryVector);
        if (queryVector.Length == 0)
            throw new ArgumentException("Query vectors must contain at least one dimension.", nameof(queryVector));
        foreach (float component in queryVector)
        {
            if (!float.IsFinite(component))
                throw new ArgumentException("Query vectors must contain only finite values.", nameof(queryVector));
        }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(topK);
        ArgumentOutOfRangeException.ThrowIfNegative(efSearch);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(oversamplingFactor);
        ArgumentOutOfRangeException.ThrowIfNegative(maxVisitedNodes);

        long candidateCount = (long)topK * oversamplingFactor;
        if (candidateCount > int.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(oversamplingFactor),
                "The product of topK and oversamplingFactor must not exceed Int32.MaxValue.");

        long automaticEfSearch = Math.Max(64L, 4L * topK);
        if (efSearch == 0 && automaticEfSearch > int.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(topK),
                "topK is too large to calculate the automatic efSearch value.");

        Field = field;
        QueryVector = (float[])queryVector.Clone();
        TopK = topK;
        EfSearch = efSearch > 0 ? efSearch : (int)automaticEfSearch;
        OversamplingFactor = oversamplingFactor;
        Filter = filter;
        MaxVisitedNodes = maxVisitedNodes;
        CandidateCount = (int)candidateCount;
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
        GetType() == other.GetType() &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        TopK == other.TopK &&
        EfSearch == other.EfSearch &&
        OversamplingFactor == other.OversamplingFactor &&
        MaxVisitedNodes == other.MaxVisitedNodes &&
        Equals(Filter, other.Filter) &&
        Boost == other.Boost &&
        QueryVector.AsSpan().SequenceEqual(other.QueryVector);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(nameof(VectorQuery));
        h.Add(Field);
        h.Add(TopK);
        h.Add(EfSearch);
        h.Add(OversamplingFactor);
        h.Add(MaxVisitedNodes);
        h.Add(Filter);
        for (int i = 0; i < QueryVector.Length; i++) h.Add(QueryVector[i]);
        h.Add(QueryVector.Length);
        return CombineBoost(h.ToHashCode());
    }

    /// <summary>
    /// Computes cosine similarity between two vectors using SIMD where available.
    /// </summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dot = 0f, normA = 0f, normB = 0f;

        int i = 0;

#if NET11_0_OR_GREATER
        // .NET 11: Vector hardware acceleration is guaranteed on all supported targets
        if (a.Length >= Vector<float>.Count)
#else
        if (Vector.IsHardwareAccelerated && a.Length >= Vector<float>.Count)
#endif
        {
            var vDot = Vector<float>.Zero;
            var vNormA = Vector<float>.Zero;
            var vNormB = Vector<float>.Zero;

            int simdEnd = a.Length - (a.Length % Vector<float>.Count);
            var spanA = MemoryMarshal.Cast<float, Vector<float>>(a[..simdEnd]);
            var spanB = MemoryMarshal.Cast<float, Vector<float>>(b[..simdEnd]);

            for (int j = 0; j < spanA.Length; j++)
            {
                vDot += spanA[j] * spanB[j];
                vNormA += spanA[j] * spanA[j];
                vNormB += spanB[j] * spanB[j];
            }

            dot = Vector.Dot(vDot, Vector<float>.One);
            normA = Vector.Dot(vNormA, Vector<float>.One);
            normB = Vector.Dot(vNormB, Vector<float>.One);
            i = simdEnd;
        }

        // Scalar remainder
        for (; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 0f ? dot / denom : 0f;
    }

    /// <summary>Computes the configured similarity between two vectors.</summary>
    public static float ComputeSimilarity(
        ReadOnlySpan<float> a,
        ReadOnlySpan<float> b,
        VectorSimilarityFunction similarity)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        if (similarity == VectorSimilarityFunction.Cosine)
            return CosineSimilarity(a, b);

        float dot = 0f;
        float squaredDistance = 0f;
        int hammingDistance = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            float delta = a[i] - b[i];
            squaredDistance += delta * delta;
            if (a[i] != b[i])
                hammingDistance++;
        }

        return similarity switch
        {
            VectorSimilarityFunction.DotProduct => dot,
            VectorSimilarityFunction.Euclidean => 1f / (1f + squaredDistance),
            VectorSimilarityFunction.MaximumInnerProduct => dot < 0f ? 1f / (1f - dot) : dot + 1f,
            VectorSimilarityFunction.Hamming => 1f - (float)hammingDistance / a.Length,
            _ => throw new ArgumentOutOfRangeException(nameof(similarity)),
        };
    }
}
