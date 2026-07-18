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
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static float Distance(
        ReadOnlySpan<float> query,
        ReadOnlySpan<float> centroid,
        ReadOnlySpan<byte> storedBits,
        int dimension)
    {
        int packedBytes = (dimension + 7) / 8;
        int matching = 0;
        int j = 0;

        // Process 64 dimensions at a time (8 bytes, 8 bits each)
        ref byte storedRef = ref MemoryMarshal.GetReference(storedBits);
        int fullChunks = packedBytes / 8;
        for (int chunk = 0; chunk < fullChunks; chunk++)
        {
            // Build query bitmask: 64 bits from 64 float comparisons
            ulong queryBits = 0;
            for (int b = 0; b < 8; b++)
            {
                byte qb = 0;
                for (int bit = 0; bit < 8; bit++, j++)
                {
                    if ((query[j] - centroid[j]) > 0f)
                        qb |= (byte)(1 << bit);
                }
                queryBits |= (ulong)qb << (b * 8);
            }

            // Load 8 stored bytes as ulong
            ulong storedUlong = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref storedRef, chunk * 8));

            // XNOR: bits where query and stored agree
            ulong matchBits = ~(queryBits ^ storedUlong);
            matching += BitOperations.PopCount(matchBits);
        }

        // Tail: remaining < 64 dimensions, use original per-bit loop
        for (; j < dimension; j++)
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
        return -Util.VectorMath.DotProduct(a, b);
    }

    /// <summary>
    /// Computes Hamming-based distance between two bit-packed stored vectors.
    /// Uses the same metric as query-vs-stored search, ensuring graph construction
    /// optimises for the distance function actually used at search time.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static float Distance(ReadOnlySpan<byte> bitsA, ReadOnlySpan<byte> bitsB, int dimension)
    {
        int packedBytes = (dimension + 7) / 8;
        int matching = 0;

        ref byte aRef = ref MemoryMarshal.GetReference(bitsA);
        ref byte bRef = ref MemoryMarshal.GetReference(bitsB);
        int fullChunks = packedBytes / 8;

        for (int chunk = 0; chunk < fullChunks; chunk++)
        {
            ulong aUlong = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref aRef, chunk * 8));
            ulong bUlong = Unsafe.ReadUnaligned<ulong>(ref Unsafe.AddByteOffset(ref bRef, chunk * 8));
            ulong matchBits = ~(aUlong ^ bUlong); // XNOR
            matching += BitOperations.PopCount(matchBits);
        }

        // Tail: remaining < 64 bits
        int tailStart = fullChunks * 8 * 8;
        for (int j = tailStart; j < dimension; j++)
        {
            int byteIdx = j / 8;
            int bitIdx = j % 8;
            bool bitA = ((Unsafe.AddByteOffset(ref aRef, byteIdx) >> bitIdx) & 1) == 1;
            bool bitB = ((Unsafe.AddByteOffset(ref bRef, byteIdx) >> bitIdx) & 1) == 1;
            if (bitA == bitB) matching++;
        }

        return -(2f * matching - dimension);
    }
}
