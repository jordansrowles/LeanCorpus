using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Search.Simd;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Distance computer for int8 scalar-quantised vectors. Fuses dequantisation and dot product
/// into a single pass, avoiding the allocation of a temporary float array per comparison.
/// </summary>
/// <remarks>
/// For int8-quantised vectors: deq[i] = min + alpha * qv[i].
/// The distance (negative dot product) is:
///   dot = Σ query[i] * deq[i] = min * Σ query[i] + alpha * Σ (query[i] * qv[i])
///   distance = -dot
/// The two sums are accumulated in one pass over the raw byte vector.
/// </remarks>
internal static class Int8DistanceComputer
{
    /// <summary>
    /// Computes distance from raw int8 bytes without intermediate float array allocation.
    /// This is the primary fast path used during HNSW search.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(
        ReadOnlySpan<float> query,
        ReadOnlySpan<byte> quantised,
        float min,
        float alpha)
    {
        // deq[i] = min + alpha * qv[i]
        // dot = Σ query[i] * deq[i] = min * Σ query[i] + alpha * Σ (query[i] * qv[i])
        float querySum = 0f;
        float weightedSum = 0f;
        int dim = query.Length;

        for (int i = 0; i < dim; i++)
        {
            querySum += query[i];
            weightedSum += query[i] * quantised[i];
        }

        return -(min * querySum + alpha * weightedSum);
    }

    /// <summary>
    /// Computes the distance between a query vector and an already-dequantised float vector.
    /// Used for stored-vs-stored comparisons (e.g. neighbour selection during HNSW build).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(
        ReadOnlySpan<float> query,
        ReadOnlySpan<float> dequantised)
    {
        return -SimdVectorOps.DotProduct(query, dequantised);
    }
}
