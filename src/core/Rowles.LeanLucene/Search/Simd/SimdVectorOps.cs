using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
namespace Rowles.LeanLucene.Search.Simd;

/// <summary>
/// SIMD vector primitives used by HNSW graph operations and exact rerank.
/// Built around <see cref="Vector{T}"/>, which the .NET JIT lowers to AVX2 or AVX-512
/// instructions automatically on capable hardware.
/// </summary>
/// <remarks>
/// All methods assume the inputs have equal length and treat empty inputs as a similarity of zero.
/// </remarks>
public static class SimdVectorOps
{
    /// <summary>
    /// Computes cosine similarity between two vectors. Returns zero for empty or
    /// length-mismatched inputs; never throws.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dot = 0f, normA = 0f, normB = 0f;
        int i = 0;

        if (a.Length >= Vector<float>.Count)
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

            dot = Vector.Sum(vDot);
            normA = Vector.Sum(vNormA);
            normB = Vector.Sum(vNormB);
            i = simdEnd;
        }

        for (; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 0f ? dot / denom : 0f;
    }

    /// <summary>
    /// Computes the dot product of two equal-length vectors. For pre-normalised vectors
    /// this is equivalent to cosine similarity at roughly two thirds of the cost.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0f;

        float dot = 0f;
        int i = 0;

        if (a.Length >= Vector<float>.Count)
        {
            var vDot = Vector<float>.Zero;
            int simdEnd = a.Length - (a.Length % Vector<float>.Count);
            var spanA = MemoryMarshal.Cast<float, Vector<float>>(a[..simdEnd]);
            var spanB = MemoryMarshal.Cast<float, Vector<float>>(b[..simdEnd]);

            for (int j = 0; j < spanA.Length; j++)
                vDot += spanA[j] * spanB[j];

            dot = Vector.Sum(vDot);
            i = simdEnd;
        }

        for (; i < a.Length; i++)
            dot += a[i] * b[i];

        return dot;
    }

    /// <summary>
    /// Computes the squared L2 norm of a vector (sum of squared elements).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float SquaredNorm(ReadOnlySpan<float> a)
    {
        if (a.Length == 0) return 0f;

        float norm = 0f;
        int i = 0;

        if (a.Length >= Vector<float>.Count)
        {
            var vNorm = Vector<float>.Zero;
            int simdEnd = a.Length - (a.Length % Vector<float>.Count);
            var spanA = MemoryMarshal.Cast<float, Vector<float>>(a[..simdEnd]);

            for (int j = 0; j < spanA.Length; j++)
                vNorm += spanA[j] * spanA[j];

            norm = Vector.Sum(vNorm);
            i = simdEnd;
        }

        for (; i < a.Length; i++)
            norm += a[i] * a[i];

        return norm;
    }

    /// <summary>
    /// L2-normalises a vector in place. Returns false when the input has zero norm
    /// (cosine is undefined in that case); the buffer is left untouched.
    /// </summary>
    public static bool NormaliseInPlace(Span<float> vector)
    {
        float sq = SquaredNorm(vector);
        if (sq <= 0f || float.IsNaN(sq) || float.IsInfinity(sq))
            return false;

        float invNorm = 1f / MathF.Sqrt(sq);
        int i = 0;
        if (vector.Length >= Vector<float>.Count)
        {
            int simdEnd = vector.Length - (vector.Length % Vector<float>.Count);
            var span = MemoryMarshal.Cast<float, Vector<float>>(vector[..simdEnd]);
            var scale = new Vector<float>(invNorm);
            for (int j = 0; j < span.Length; j++)
                span[j] *= scale;
            i = simdEnd;
        }
        for (; i < vector.Length; i++)
            vector[i] *= invNorm;
        return true;
    }

    /// <summary>
    /// Allocates a normalised copy of the input vector. Throws when the input has zero norm.
    /// </summary>
    public static float[] Normalise(ReadOnlySpan<float> vector)
    {
        var copy = vector.ToArray();
        if (!NormaliseInPlace(copy))
            throw new ArgumentException("Cannot normalise a zero-norm vector.", nameof(vector));
        return copy;
    }
}
