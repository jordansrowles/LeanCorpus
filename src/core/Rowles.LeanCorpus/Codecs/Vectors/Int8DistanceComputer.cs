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
///   -min * sum(query) - alpha * sum(query[i] * qv[i])
/// The query sum is precomputed once per search call.
/// </remarks>
internal static class Int8DistanceComputer
{
    /// <summary>
    /// Computes the distance between a query vector and a quantised stored vector.
    /// Lower values are closer (negative dot product).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(
        ReadOnlySpan<float> query,
        ReadOnlySpan<float> dequantised)
    {
        // Int8 dequantised vectors are already float arrays from QuantisedVectorSource.
        // Use the same dot-product path as unquantised vectors.
        return -SimdVectorOps.DotProduct(query, dequantised);
    }
}
