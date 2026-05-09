using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for SIMD Vector Ops.
/// </summary>
[Trait("Category", "Simd")]
public sealed class SimdVectorOpsTests
{
    /// <summary>
    /// Verifies the Cosine: On Identical Vectors Is One scenario.
    /// </summary>
    [Fact(DisplayName = "Cosine: On Identical Vectors Is One")]
    public void Cosine_OnIdenticalVectors_IsOne()
    {
        var v = new float[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        float similarity = SimdVectorOps.CosineSimilarity(v, v);

        Assert.InRange(similarity, 0.9999f, 1.0001f);
    }

    /// <summary>
    /// Verifies the Cosine: On Orthogonal Vectors Is Zero scenario.
    /// </summary>
    [Fact(DisplayName = "Cosine: On Orthogonal Vectors Is Zero")]
    public void Cosine_OnOrthogonalVectors_IsZero()
    {
        var a = new float[] { 1, 0, 0, 0 };
        var b = new float[] { 0, 1, 0, 0 };

        float similarity = SimdVectorOps.CosineSimilarity(a, b);

        Assert.Equal(0f, similarity, 5);
    }

    /// <summary>
    /// Verifies the Cosine: On Empty Or Mismatched Inputs Returns Zero scenario.
    /// </summary>
    [Fact(DisplayName = "Cosine: On Empty Or Mismatched Inputs Returns Zero")]
    public void Cosine_OnEmptyOrMismatchedInputs_ReturnsZero()
    {
        Assert.Equal(0f, SimdVectorOps.CosineSimilarity(Array.Empty<float>(), Array.Empty<float>()));
        Assert.Equal(0f, SimdVectorOps.CosineSimilarity(new float[] { 1, 2 }, new float[] { 1, 2, 3 }));
    }

    /// <summary>
    /// Verifies the Dot Product: Matches Scalar Reference scenario.
    /// </summary>
    [Fact(DisplayName = "Dot Product: Matches Scalar Reference")]
    public void DotProduct_MatchesScalarReference()
    {
        var rng = new Random(17);
        var a = new float[37];
        var b = new float[37];
        for (int i = 0; i < a.Length; i++) { a[i] = (float)rng.NextDouble(); b[i] = (float)rng.NextDouble(); }
        float reference = 0f;
        for (int i = 0; i < a.Length; i++) reference += a[i] * b[i];

        float result = SimdVectorOps.DotProduct(a, b);

        Assert.Equal(reference, result, 4);
    }

    /// <summary>
    /// Verifies the Normalise In Place: Produces Unit Norm scenario.
    /// </summary>
    [Fact(DisplayName = "Normalise In Place: Produces Unit Norm")]
    public void NormaliseInPlace_ProducesUnitNorm()
    {
        var v = new float[] { 3, 4, 0, 0 };

        bool ok = SimdVectorOps.NormaliseInPlace(v);

        Assert.True(ok);
        Assert.Equal(1f, MathF.Sqrt(SimdVectorOps.SquaredNorm(v)), 5);
    }

    /// <summary>
    /// Verifies the Normalise In Place: On Zero Vector Returns False And Leaves Input Untouched scenario.
    /// </summary>
    [Fact(DisplayName = "Normalise In Place: On Zero Vector Returns False And Leaves Input Untouched")]
    public void NormaliseInPlace_OnZeroVector_ReturnsFalseAndLeavesInputUntouched()
    {
        var v = new float[] { 0, 0, 0, 0 };

        bool ok = SimdVectorOps.NormaliseInPlace(v);

        Assert.False(ok);
        Assert.All(v, x => Assert.Equal(0f, x));
    }

    /// <summary>
    /// Verifies the Normalise: On Zero Vector Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Normalise: On Zero Vector Throws")]
    public void Normalise_OnZeroVector_Throws()
    {
        Assert.Throws<ArgumentException>(() => SimdVectorOps.Normalise(new float[] { 0, 0, 0 }));
    }

    /// <summary>
    /// Verifies the Dot Product: Of Normalised Equals Cosine Similarity scenario.
    /// </summary>
    [Fact(DisplayName = "Dot Product: Of Normalised Equals Cosine Similarity")]
    public void DotProduct_OfNormalised_EqualsCosineSimilarity()
    {
        var rng = new Random(99);
        var a = new float[64];
        var b = new float[64];
        for (int i = 0; i < a.Length; i++) { a[i] = (float)rng.NextDouble(); b[i] = (float)rng.NextDouble(); }

        var an = SimdVectorOps.Normalise(a);
        var bn = SimdVectorOps.Normalise(b);

        float dot = SimdVectorOps.DotProduct(an, bn);
        float cos = SimdVectorOps.CosineSimilarity(a, b);

        Assert.Equal(cos, dot, 4);
    }
}
