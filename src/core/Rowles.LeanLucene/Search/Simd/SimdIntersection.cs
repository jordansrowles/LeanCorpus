using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
namespace Rowles.LeanLucene.Search.Simd;

/// <summary>
/// Specialised SIMD-accelerated Boolean AND intersection for sorted doc-ID arrays.
/// </summary>
/// <remarks>
/// Both input spans must be sorted in ascending order with no duplicates.
/// On hardware that supports 128-bit integer vectors the implementation loads
/// four <see cref="int"/> values at a time and uses a vectorised merge-intersect
/// strategy. A scalar two-pointer fallback is used for tails and unsupported platforms.
/// </remarks>
internal static class SimdIntersection
{
    /// <summary>
    /// Computes the intersection of two sorted ascending doc-ID arrays,
    /// writing matched values into <paramref name="result"/>.
    /// </summary>
    /// <param name="sortedA">First sorted input.</param>
    /// <param name="sortedB">Second sorted input.</param>
    /// <param name="result">
    /// Destination buffer. Must be at least
    /// <c>Math.Min(sortedA.Length, sortedB.Length)</c> elements long.
    /// </param>
    /// <returns>The number of common elements written to <paramref name="result"/>.</returns>
    public static int Intersect(ReadOnlySpan<int> sortedA, ReadOnlySpan<int> sortedB, Span<int> result)
    {
#if NET11_0_OR_GREATER
        // .NET 11+: the JIT guarantees Vector128<int> is always hardware-backed
        // on every supported target, so no runtime check is required.
        return IntersectSimd(sortedA, sortedB, result);
#else
        if (Vector128.IsHardwareAccelerated)
            return IntersectSimd(sortedA, sortedB, result);

        return IntersectScalar(sortedA, sortedB, result);
#endif
    }

    /// <summary>
    /// Returns the number of elements common to both sorted ascending arrays
    /// without materialising the result set.
    /// </summary>
    public static int IntersectCount(ReadOnlySpan<int> sortedA, ReadOnlySpan<int> sortedB)
    {
#if NET11_0_OR_GREATER
        return IntersectCountSimd(sortedA, sortedB);
#else
        if (Vector128.IsHardwareAccelerated)
            return IntersectCountSimd(sortedA, sortedB);

        return IntersectCountScalar(sortedA, sortedB);
#endif
    }

    // ──────────────────────────────────────────────────────────────
    //  SIMD merge-intersect (Vector128<int> — 4 elements per lane)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// SIMD-accelerated merge-intersect that writes matches into <paramref name="result"/>.
    /// Falls back to scalar for the remaining tail elements that don't fill a full vector.
    /// </summary>
    private static int IntersectSimd(ReadOnlySpan<int> sortedA, ReadOnlySpan<int> sortedB, Span<int> result)
    {
        const int VectorLength = 4; // Vector128<int> holds 4 ints

        int ia = 0;
        int ib = 0;
        int count = 0;

        int simdLimitA = sortedA.Length - VectorLength + 1;
        int simdLimitB = sortedB.Length - VectorLength + 1;

        ref int refA = ref MemoryMarshal.GetReference(sortedA);
        ref int refB = ref MemoryMarshal.GetReference(sortedB);

        while (ia < simdLimitA && ib < simdLimitB)
        {
            // Load 4 sorted doc IDs from each array.
            Vector128<int> vecA = Vector128.LoadUnsafe(ref Unsafe.Add(ref refA, ia));
            Vector128<int> vecB = Vector128.LoadUnsafe(ref Unsafe.Add(ref refB, ib));

            int maxA = sortedA[ia + VectorLength - 1];
            int minB = sortedB[ib];
            int maxB = sortedB[ib + VectorLength - 1];
            int minA = sortedA[ia];

            if (maxA < minB)
            {
                // Entire A vector is less than B — advance A.
                ia += VectorLength;
                continue;
            }

            if (maxB < minA)
            {
                // Entire B vector is less than A — advance B.
                ib += VectorLength;
                continue;
            }

            // Ranges overlap — perform element-wise comparison.
            // For each element in vecA, check whether it equals any element in vecB
            // by shuffling vecB into all four rotations and OR-ing the equality masks.
            count = EmitVectorMatches(vecA, vecB, result, count);

            // Advance whichever pointer has the smaller maximum value so
            // that the merge-intersect invariant is maintained.
            if (maxA <= maxB)
                ia += VectorLength;
            if (maxB <= maxA)
                ib += VectorLength;
        }

        // Scalar tail for remaining elements.
        count = IntersectScalarCore(sortedA, sortedB, result, ia, ib, count);
        return count;
    }

    /// <summary>
    /// SIMD-accelerated merge-intersect that only counts matches.
    /// </summary>
    private static int IntersectCountSimd(ReadOnlySpan<int> sortedA, ReadOnlySpan<int> sortedB)
    {
        const int VectorLength = 4;

        int ia = 0;
        int ib = 0;
        int count = 0;

        int simdLimitA = sortedA.Length - VectorLength + 1;
        int simdLimitB = sortedB.Length - VectorLength + 1;

        ref int refA = ref MemoryMarshal.GetReference(sortedA);
        ref int refB = ref MemoryMarshal.GetReference(sortedB);

        while (ia < simdLimitA && ib < simdLimitB)
        {
            Vector128<int> vecA = Vector128.LoadUnsafe(ref Unsafe.Add(ref refA, ia));
            Vector128<int> vecB = Vector128.LoadUnsafe(ref Unsafe.Add(ref refB, ib));

            int maxA = sortedA[ia + VectorLength - 1];
            int minB = sortedB[ib];
            int maxB = sortedB[ib + VectorLength - 1];
            int minA = sortedA[ia];

            if (maxA < minB)
            {
                ia += VectorLength;
                continue;
            }

            if (maxB < minA)
            {
                ib += VectorLength;
                continue;
            }

            count += CountVectorMatches(vecA, vecB);

            if (maxA <= maxB)
                ia += VectorLength;
            if (maxB <= maxA)
                ib += VectorLength;
        }

        // Scalar tail.
        count = IntersectCountScalarCore(sortedA, sortedB, ia, ib, count);
        return count;
    }

    /// <summary>
    /// Checks each element of <paramref name="vecA"/> against all elements of
    /// <paramref name="vecB"/> using four rotations. Matching values from A are
    /// written to <paramref name="result"/>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int EmitVectorMatches(
        Vector128<int> vecA,
        Vector128<int> vecB,
        Span<int> result,
        int count)
    {
        // Rotate vecB through all four positions and compare with vecA.
        // Any lane that equals produces a -1 (all bits set) mask element.
        Vector128<int> rot0 = vecB;
        Vector128<int> rot1 = Vector128.Shuffle(vecB, Vector128.Create(1, 2, 3, 0));
        Vector128<int> rot2 = Vector128.Shuffle(vecB, Vector128.Create(2, 3, 0, 1));
        Vector128<int> rot3 = Vector128.Shuffle(vecB, Vector128.Create(3, 0, 1, 2));

        Vector128<int> cmp0 = Vector128.Equals(vecA, rot0);
        Vector128<int> cmp1 = Vector128.Equals(vecA, rot1);
        Vector128<int> cmp2 = Vector128.Equals(vecA, rot2);
        Vector128<int> cmp3 = Vector128.Equals(vecA, rot3);

        // OR all comparison results — a lane is set if the corresponding A
        // element matched *any* element in B.
        Vector128<int> mask = (cmp0 | cmp1) | (cmp2 | cmp3);

        // Extract matched elements. Each set lane in `mask` means -1 (0xFFFFFFFF).
        for (int lane = 0; lane < 4; lane++)
        {
            if (mask.GetElement(lane) != 0)
            {
                result[count++] = vecA.GetElement(lane);
            }
        }

        return count;
    }

    /// <summary>
    /// Counts matching elements between two vectors without writing output.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountVectorMatches(Vector128<int> vecA, Vector128<int> vecB)
    {
        Vector128<int> rot0 = vecB;
        Vector128<int> rot1 = Vector128.Shuffle(vecB, Vector128.Create(1, 2, 3, 0));
        Vector128<int> rot2 = Vector128.Shuffle(vecB, Vector128.Create(2, 3, 0, 1));
        Vector128<int> rot3 = Vector128.Shuffle(vecB, Vector128.Create(3, 0, 1, 2));

        Vector128<int> cmp0 = Vector128.Equals(vecA, rot0);
        Vector128<int> cmp1 = Vector128.Equals(vecA, rot1);
        Vector128<int> cmp2 = Vector128.Equals(vecA, rot2);
        Vector128<int> cmp3 = Vector128.Equals(vecA, rot3);

        Vector128<int> mask = (cmp0 | cmp1) | (cmp2 | cmp3);

        // Each matching lane has value -1 (0xFFFFFFFF). Negate to get +1 per match,
        // then sum across all lanes.
        // -(-1) == 1, -(0) == 0
        Vector128<int> ones = Vector128.Negate(mask);
        return Vector128.Sum(ones);
    }

    // ──────────────────────────────────────────────────────────────
    //  Scalar two-pointer merge-intersect (fallback)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Classic two-pointer merge-intersect over the entire spans.
    /// </summary>
    private static int IntersectScalar(ReadOnlySpan<int> sortedA, ReadOnlySpan<int> sortedB, Span<int> result)
    {
        return IntersectScalarCore(sortedA, sortedB, result, ia: 0, ib: 0, count: 0);
    }

    /// <summary>
    /// Scalar merge-intersect starting from arbitrary indices, used both as a
    /// standalone fallback and to process the tail after the SIMD loop.
    /// </summary>
    private static int IntersectScalarCore(
        ReadOnlySpan<int> sortedA,
        ReadOnlySpan<int> sortedB,
        Span<int> result,
        int ia,
        int ib,
        int count)
    {
        while (ia < sortedA.Length && ib < sortedB.Length)
        {
            int a = sortedA[ia];
            int b = sortedB[ib];

            if (a == b)
            {
                result[count++] = a;
                ia++;
                ib++;
            }
            else if (a < b)
            {
                ia++;
            }
            else
            {
                ib++;
            }
        }

        return count;
    }

    /// <summary>
    /// Classic two-pointer count-only merge-intersect.
    /// </summary>
    private static int IntersectCountScalar(ReadOnlySpan<int> sortedA, ReadOnlySpan<int> sortedB)
    {
        return IntersectCountScalarCore(sortedA, sortedB, ia: 0, ib: 0, count: 0);
    }

    /// <summary>
    /// Scalar count-only merge-intersect from arbitrary indices.
    /// </summary>
    private static int IntersectCountScalarCore(
        ReadOnlySpan<int> sortedA,
        ReadOnlySpan<int> sortedB,
        int ia,
        int ib,
        int count)
    {
        while (ia < sortedA.Length && ib < sortedB.Length)
        {
            int a = sortedA[ia];
            int b = sortedB[ib];

            if (a == b)
            {
                count++;
                ia++;
                ib++;
            }
            else if (a < b)
            {
                ia++;
            }
            else
            {
                ib++;
            }
        }

        return count;
    }
}
