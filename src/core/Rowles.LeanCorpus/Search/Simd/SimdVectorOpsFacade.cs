using Rowles.LeanCorpus.Util;

namespace Rowles.LeanCorpus.Search.Simd;

/// <summary>Public SIMD vector primitives for similarity calculations and normalisation.</summary>
public static class SimdVectorOps
{
    /// <summary>Computes cosine similarity, returning zero for empty or mismatched vectors.</summary>
    public static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b) =>
        VectorMath.CosineSimilarity(a, b);

    /// <summary>Computes the dot product of two equal-length vectors.</summary>
    public static float DotProduct(ReadOnlySpan<float> a, ReadOnlySpan<float> b) =>
        VectorMath.DotProduct(a, b);

    /// <summary>Computes the squared L2 norm of a vector.</summary>
    public static float SquaredNorm(ReadOnlySpan<float> vector) => VectorMath.SquaredNorm(vector);

    /// <summary>L2-normalises a vector in place.</summary>
    public static bool NormaliseInPlace(Span<float> vector) => VectorMath.NormaliseInPlace(vector);

    /// <summary>Allocates and returns an L2-normalised copy of a vector.</summary>
    public static float[] Normalise(ReadOnlySpan<float> vector) => VectorMath.Normalise(vector);
}
