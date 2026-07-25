using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using Rowles.LeanCorpus.Util;

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
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static float Distance(
        ReadOnlySpan<float> query,
        ReadOnlySpan<byte> quantised,
        float min,
        float alpha)
    {
        // deq[i] = min + alpha * qv[i]
        // dot = Σ query[i] * deq[i] = min * Σ query[i] + alpha * Σ (query[i] * qv[i])
        int dim = query.Length;
        float querySum, weightedSum;

        if (Vector256.IsHardwareAccelerated && dim >= 8)
        {
            Vector256<float> qSumVec = Vector256<float>.Zero;
            Vector256<float> wSumVec = Vector256<float>.Zero;
            ref float queryRef = ref MemoryMarshal.GetReference(query);
            ref byte quantRef = ref MemoryMarshal.GetReference(quantised);
            int i = 0;

            Span<float> qvTemp = stackalloc float[8];

            for (; i + 8 <= dim; i += 8)
            {
                var qVec = Vector256.LoadUnsafe(ref Unsafe.Add(ref queryRef, i));

                for (int k = 0; k < 8; k++)
                    qvTemp[k] = Unsafe.Add(ref quantRef, i + k);

                var qvFloat = Vector256.LoadUnsafe(ref qvTemp[0]);

                qSumVec += qVec;
                wSumVec += qVec * qvFloat;
            }

            querySum = Vector256.Sum(qSumVec);
            weightedSum = Vector256.Sum(wSumVec);

            for (; i < dim; i++)
            {
                querySum += Unsafe.Add(ref queryRef, i);
                weightedSum += Unsafe.Add(ref queryRef, i) * Unsafe.Add(ref quantRef, i);
            }
        }
        else
        {
            querySum = 0f;
            weightedSum = 0f;
            for (int i = 0; i < dim; i++)
            {
                querySum += query[i];
                weightedSum += query[i] * quantised[i];
            }
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
        return -VectorMath.DotProduct(query, dequantised);
    }
}
