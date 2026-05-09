using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
namespace Rowles.LeanLucene.Search.Simd;

/// <summary>
/// Explicit-intrinsics SIMD path for vector arithmetic. Selects the widest available
/// instruction set at runtime: AVX-512 → AVX2 → SSE → scalar.
/// Provided alongside <see cref="SimdVectorOps"/> for benchmark comparison; the slower
/// of the two will be removed once measurements are concluded.
/// </summary>
public static class SimdIntrinsicsVectorOps
{
    /// <summary>True when the running hardware supports AVX-512F (single-precision FMA path).</summary>
    public static bool IsAvx512Supported => Avx512F.IsSupported;

    /// <summary>True when the running hardware supports AVX2 (256-bit float path with FMA).</summary>
    public static bool IsAvx2Supported => Avx2.IsSupported && Fma.IsSupported;

    /// <summary>True when the running hardware supports SSE (128-bit float path).</summary>
    public static bool IsSseSupported => Sse.IsSupported;

    /// <summary>Cosine similarity between two equal-length vectors, using explicit intrinsics.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dot, normA, normB;
        if (Avx512F.IsSupported && a.Length >= Vector512<float>.Count)
            (dot, normA, normB) = CosineAvx512(a, b);
        else if (Avx2.IsSupported && Fma.IsSupported && a.Length >= Vector256<float>.Count)
            (dot, normA, normB) = CosineAvx2(a, b);
        else if (Sse.IsSupported && a.Length >= Vector128<float>.Count)
            (dot, normA, normB) = CosineSse(a, b);
        else
            (dot, normA, normB) = CosineScalar(a, b);

        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 0f ? dot / denom : 0f;
    }

    /// <summary>Dot product using explicit intrinsics.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        if (Avx512F.IsSupported && a.Length >= Vector512<float>.Count)
            return DotAvx512(a, b);
        if (Avx2.IsSupported && Fma.IsSupported && a.Length >= Vector256<float>.Count)
            return DotAvx2(a, b);
        if (Sse.IsSupported && a.Length >= Vector128<float>.Count)
            return DotSse(a, b);
        return DotScalar(a, b);
    }

    private static (float dot, float na, float nb) CosineAvx512(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var vDot = Vector512<float>.Zero;
        var vA = Vector512<float>.Zero;
        var vB = Vector512<float>.Zero;
        int width = Vector512<float>.Count;
        int simdEnd = a.Length - (a.Length % width);
        ref float pa = ref MemoryMarshal.GetReference(a);
        ref float pb = ref MemoryMarshal.GetReference(b);

        for (int j = 0; j < simdEnd; j += width)
        {
            var x = Vector512.LoadUnsafe(ref Unsafe.Add(ref pa, j));
            var y = Vector512.LoadUnsafe(ref Unsafe.Add(ref pb, j));
            vDot = Avx512F.FusedMultiplyAdd(x, y, vDot);
            vA = Avx512F.FusedMultiplyAdd(x, x, vA);
            vB = Avx512F.FusedMultiplyAdd(y, y, vB);
        }

        float dot = Vector512.Sum(vDot);
        float na = Vector512.Sum(vA);
        float nb = Vector512.Sum(vB);

        for (int i = simdEnd; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return (dot, na, nb);
    }

    private static (float dot, float na, float nb) CosineAvx2(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var vDot = Vector256<float>.Zero;
        var vA = Vector256<float>.Zero;
        var vB = Vector256<float>.Zero;
        int width = Vector256<float>.Count;
        int simdEnd = a.Length - (a.Length % width);
        ref float pa = ref MemoryMarshal.GetReference(a);
        ref float pb = ref MemoryMarshal.GetReference(b);

        for (int j = 0; j < simdEnd; j += width)
        {
            var x = Vector256.LoadUnsafe(ref Unsafe.Add(ref pa, j));
            var y = Vector256.LoadUnsafe(ref Unsafe.Add(ref pb, j));
            vDot = Fma.MultiplyAdd(x, y, vDot);
            vA = Fma.MultiplyAdd(x, x, vA);
            vB = Fma.MultiplyAdd(y, y, vB);
        }

        float dot = Vector256.Sum(vDot);
        float na = Vector256.Sum(vA);
        float nb = Vector256.Sum(vB);

        for (int i = simdEnd; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return (dot, na, nb);
    }

    private static (float dot, float na, float nb) CosineSse(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var vDot = Vector128<float>.Zero;
        var vA = Vector128<float>.Zero;
        var vB = Vector128<float>.Zero;
        int width = Vector128<float>.Count;
        int simdEnd = a.Length - (a.Length % width);
        ref float pa = ref MemoryMarshal.GetReference(a);
        ref float pb = ref MemoryMarshal.GetReference(b);

        for (int j = 0; j < simdEnd; j += width)
        {
            var x = Vector128.LoadUnsafe(ref Unsafe.Add(ref pa, j));
            var y = Vector128.LoadUnsafe(ref Unsafe.Add(ref pb, j));
            vDot = Sse.Add(vDot, Sse.Multiply(x, y));
            vA = Sse.Add(vA, Sse.Multiply(x, x));
            vB = Sse.Add(vB, Sse.Multiply(y, y));
        }

        float dot = Vector128.Sum(vDot);
        float na = Vector128.Sum(vA);
        float nb = Vector128.Sum(vB);

        for (int i = simdEnd; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return (dot, na, nb);
    }

    private static (float dot, float na, float nb) CosineScalar(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0f, na = 0f, nb = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            na += a[i] * a[i];
            nb += b[i] * b[i];
        }
        return (dot, na, nb);
    }

    private static float DotAvx512(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var v = Vector512<float>.Zero;
        int width = Vector512<float>.Count;
        int simdEnd = a.Length - (a.Length % width);
        ref float pa = ref MemoryMarshal.GetReference(a);
        ref float pb = ref MemoryMarshal.GetReference(b);
        for (int j = 0; j < simdEnd; j += width)
        {
            var x = Vector512.LoadUnsafe(ref Unsafe.Add(ref pa, j));
            var y = Vector512.LoadUnsafe(ref Unsafe.Add(ref pb, j));
            v = Avx512F.FusedMultiplyAdd(x, y, v);
        }
        float s = Vector512.Sum(v);
        for (int i = simdEnd; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    private static float DotAvx2(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var v = Vector256<float>.Zero;
        int width = Vector256<float>.Count;
        int simdEnd = a.Length - (a.Length % width);
        ref float pa = ref MemoryMarshal.GetReference(a);
        ref float pb = ref MemoryMarshal.GetReference(b);
        for (int j = 0; j < simdEnd; j += width)
        {
            var x = Vector256.LoadUnsafe(ref Unsafe.Add(ref pa, j));
            var y = Vector256.LoadUnsafe(ref Unsafe.Add(ref pb, j));
            v = Fma.MultiplyAdd(x, y, v);
        }
        float s = Vector256.Sum(v);
        for (int i = simdEnd; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    private static float DotSse(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var v = Vector128<float>.Zero;
        int width = Vector128<float>.Count;
        int simdEnd = a.Length - (a.Length % width);
        ref float pa = ref MemoryMarshal.GetReference(a);
        ref float pb = ref MemoryMarshal.GetReference(b);
        for (int j = 0; j < simdEnd; j += width)
        {
            var x = Vector128.LoadUnsafe(ref Unsafe.Add(ref pa, j));
            var y = Vector128.LoadUnsafe(ref Unsafe.Add(ref pb, j));
            v = Sse.Add(v, Sse.Multiply(x, y));
        }
        float s = Vector128.Sum(v);
        for (int i = simdEnd; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }

    private static float DotScalar(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float s = 0f;
        for (int i = 0; i < a.Length; i++) s += a[i] * b[i];
        return s;
    }
}
