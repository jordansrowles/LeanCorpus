using System.Numerics;
using System.Runtime.InteropServices;

namespace Rowles.LeanLucene.Search.Queries;

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

    /// <summary>Initialises a new <see cref="VectorQuery"/> for the given field and query vector.</summary>
    /// <param name="field">The vector field to search.</param>
    /// <param name="queryVector">The query vector for similarity comparison.</param>
    /// <param name="topK">Maximum number of nearest neighbours to return. Default: 10.</param>
    /// <param name="efSearch">HNSW <c>ef</c> parameter. <c>0</c> selects an automatic default.</param>
    /// <param name="oversamplingFactor">Multiplier for the HNSW shortlist before exact rerank.</param>
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
        EfSearch = efSearch > 0 ? efSearch : Math.Max(64, 4 * topK);
        OversamplingFactor = Math.Max(1, oversamplingFactor);
        Filter = filter;
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
}
