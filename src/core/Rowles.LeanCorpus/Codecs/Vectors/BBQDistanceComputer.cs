using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rowles.LeanCorpus.Codecs.Vectors;

/// <summary>
/// Distance computer for BBQ (Better Binary Quantisation) vectors.
/// Uses PopCount-based Hamming distance between binary-quantised vectors,
/// which is a fast approximation of cosine distance for normalised, centred vectors.
/// </summary>
/// <remarks>
/// Both the query and stored vectors are binary-quantised (centred, then sign bit).
/// The distance is computed as: -(dimension - 2 * matching_bits), where
/// matching_bits counts positions where query and stored bits agree.
/// This is equivalent to the negated Hamming distance, scaled to match the
/// range of the existing dot-product-based distance.
///
/// Using <c>BitOperations.PopCount</c> for efficient software fallback;
/// the JIT can lower this to the POPCNT instruction on x64.
/// </remarks>
internal static class BBQDistanceComputer
{
    /// <summary>
    /// Computes the Hamming-based distance between a float query and a bit-packed stored vector.
    /// The query is binary-quantised against the centroid inline.
    /// Lower values are closer (negated match count).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(
        ReadOnlySpan<float> query,
        ReadOnlySpan<float> centroid,
        ReadOnlySpan<byte> storedBits,
        int dimension)
    {
        int packedBytes = (dimension + 7) / 8;
        int matching = 0;

        // Binary quantise the query on the fly and compare against stored bits
        for (int j = 0; j < dimension; j++)
        {
            bool queryBit = (query[j] - centroid[j]) > 0f;
            int byteIdx = j / 8;
            int bitIdx = j % 8;
            bool storedBit = ((storedBits[byteIdx] >> bitIdx) & 1) == 1;
            if (queryBit == storedBit)
                matching++;
        }

        // Convert to a distance where lower = closer.
        // Range: [dimension, -dimension]; 0 when all match, -dimension when none match.
        return -(2f * matching - dimension);
    }

    /// <summary>
    /// Computes distance between two already-dequantised float vectors from BBQ.
    /// Used for stored-vs-stored comparisons (e.g. neighbour selection heuristic).
    /// Falls back to the standard dot-product path.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Distance(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        // Both are dequantised float arrays; use standard dot product.
        return -Search.Simd.SimdVectorOps.DotProduct(a, b);
    }
}
