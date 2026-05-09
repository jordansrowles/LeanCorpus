using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using System.Runtime.Intrinsics.X86;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for SIMD Intrinsics Vector Ops.
/// </summary>
[Trait("Category", "Phase5")]
public sealed class SimdIntrinsicsVectorOpsTests
{
    /// <summary>
    /// Verifies the Cosine Similarity: Matches Numerics Implementation scenario.
    /// </summary>
    [Fact(DisplayName = "Cosine Similarity: Matches Numerics Implementation")]
    public void CosineSimilarity_MatchesNumericsImplementation()
    {
        var rnd = new Random(7);
        foreach (var dim in new[] { 8, 31, 64, 100, 257, 1024 })
        {
            var a = new float[dim];
            var b = new float[dim];
            for (int i = 0; i < dim; i++)
            {
                a[i] = (float)(rnd.NextDouble() * 2 - 1);
                b[i] = (float)(rnd.NextDouble() * 2 - 1);
            }

            float expected = SimdVectorOps.CosineSimilarity(a, b);
            float actual = SimdIntrinsicsVectorOps.CosineSimilarity(a, b);
            Assert.InRange(actual - expected, -1e-4f, 1e-4f);
        }
    }

    /// <summary>
    /// Verifies the Dot Product: Matches Numerics Implementation scenario.
    /// </summary>
    [Fact(DisplayName = "Dot Product: Matches Numerics Implementation")]
    public void DotProduct_MatchesNumericsImplementation()
    {
        var rnd = new Random(11);
        foreach (var dim in new[] { 8, 31, 64, 100, 257, 1024 })
        {
            var a = new float[dim];
            var b = new float[dim];
            for (int i = 0; i < dim; i++)
            {
                a[i] = (float)(rnd.NextDouble() * 2 - 1);
                b[i] = (float)(rnd.NextDouble() * 2 - 1);
            }
            float expected = SimdVectorOps.DotProduct(a, b);
            float actual = SimdIntrinsicsVectorOps.DotProduct(a, b);
            Assert.InRange(actual - expected, -1e-3f, 1e-3f);
        }
    }

    [Fact(DisplayName = "Support Flags: Match Runtime Intrinsics")]
    public void SupportFlags_MatchRuntimeIntrinsics()
    {
        Assert.Equal(Avx512F.IsSupported, SimdIntrinsicsVectorOps.IsAvx512Supported);
        Assert.Equal(Avx2.IsSupported && Fma.IsSupported, SimdIntrinsicsVectorOps.IsAvx2Supported);
        Assert.Equal(Sse.IsSupported, SimdIntrinsicsVectorOps.IsSseSupported);
    }

    [Theory(DisplayName = "Dot Product: Width Boundaries Match Scalar")]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(17)]
    public void DotProduct_WidthBoundaries_MatchScalar(int dimension)
    {
        var a = BuildVector(dimension, 0.25f);
        var b = BuildVector(dimension, -0.5f);

        float expected = DotProductScalar(a, b);
        float actual = SimdIntrinsicsVectorOps.DotProduct(a, b);

        Assert.InRange(actual - expected, -1e-4f, 1e-4f);
    }

    [Theory(DisplayName = "Cosine Similarity: Width Boundaries Match Scalar")]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(17)]
    public void CosineSimilarity_WidthBoundaries_MatchScalar(int dimension)
    {
        var a = BuildVector(dimension, 1.25f);
        var b = BuildVector(dimension, -0.75f);

        float expected = CosineSimilarityScalar(a, b);
        float actual = SimdIntrinsicsVectorOps.CosineSimilarity(a, b);

        Assert.InRange(actual - expected, -1e-4f, 1e-4f);
    }

    /// <summary>
    /// Verifies the Cosine Similarity: Empty Inputs Return Zero scenario.
    /// </summary>
    [Fact(DisplayName = "Cosine Similarity: Empty Inputs Return Zero")]
    public void CosineSimilarity_EmptyInputs_ReturnZero()
    {
        Assert.Equal(0f, SimdIntrinsicsVectorOps.CosineSimilarity([], []));
    }

    /// <summary>
    /// Verifies the Cosine Similarity: Identical Vectors Return One scenario.
    /// </summary>
    [Fact(DisplayName = "Cosine Similarity: Identical Vectors Return One")]
    public void CosineSimilarity_IdenticalVectors_ReturnOne()
    {
        float[] v = [1f, 2f, 3f, 4f, 5f, 6f, 7f, 8f];
        float sim = SimdIntrinsicsVectorOps.CosineSimilarity(v, v);
        Assert.InRange(sim, 0.999f, 1.001f);
    }

    /// <summary>
    /// Verifies mismatched vector lengths return zero rather than throwing.
    /// </summary>
    [Fact(DisplayName = "Cosine Similarity: Length Mismatch Returns Zero")]
    public void CosineSimilarity_LengthMismatch_ReturnsZero()
    {
        Assert.Equal(0f, SimdIntrinsicsVectorOps.CosineSimilarity([1f, 2f], [1f]));
    }

    /// <summary>
    /// Verifies zero-magnitude vectors return zero rather than NaN.
    /// </summary>
    [Fact(DisplayName = "Cosine Similarity: Zero Magnitude Returns Zero")]
    public void CosineSimilarity_ZeroMagnitude_ReturnsZero()
    {
        Assert.Equal(0f, SimdIntrinsicsVectorOps.CosineSimilarity([0f, 0f, 0f], [1f, 2f, 3f]));
    }

    /// <summary>
    /// Verifies mismatched vector lengths return zero rather than throwing.
    /// </summary>
    [Fact(DisplayName = "Dot Product: Length Mismatch Returns Zero")]
    public void DotProduct_LengthMismatch_ReturnsZero()
    {
        Assert.Equal(0f, SimdIntrinsicsVectorOps.DotProduct([1f, 2f], [1f]));
    }

    /// <summary>
    /// Verifies short vectors use the scalar tail path correctly on every CPU.
    /// </summary>
    [Fact(DisplayName = "Dot Product: Short Vector Uses Scalar Path")]
    public void DotProduct_ShortVectorUsesScalarPath()
    {
        float actual = SimdIntrinsicsVectorOps.DotProduct([2f, -3f, 4f], [5f, 6f, -7f]);

        Assert.Equal(-36f, actual);
    }

    /// <summary>
    /// Verifies short vectors use the scalar cosine path correctly on every CPU.
    /// </summary>
    [Fact(DisplayName = "Cosine Similarity: Short Vector Uses Scalar Path")]
    public void CosineSimilarity_ShortVectorUsesScalarPath()
    {
        float actual = SimdIntrinsicsVectorOps.CosineSimilarity([1f, 0f, 0f], [0f, 1f, 0f]);

        Assert.Equal(0f, actual);
    }

    private static float[] BuildVector(int dimension, float seed)
    {
        var vector = new float[dimension];
        for (int i = 0; i < vector.Length; i++)
            vector[i] = seed + (i % 7) - (i * 0.125f);
        return vector;
    }

    private static float DotProductScalar(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float result = 0f;
        for (int i = 0; i < a.Length; i++)
            result += a[i] * b[i];
        return result;
    }

    private static float CosineSimilarityScalar(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        float dot = 0f;
        float normA = 0f;
        float normB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        float denom = MathF.Sqrt(normA) * MathF.Sqrt(normB);
        return denom > 0f ? dot / denom : 0f;
    }
}
