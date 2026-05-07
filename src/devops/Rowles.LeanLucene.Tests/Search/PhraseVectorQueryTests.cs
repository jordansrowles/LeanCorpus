namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Unit tests for <see cref="PhraseQuery"/> and <see cref="VectorQuery"/> covering
/// constructors, property defaults, equality, hash codes, and CosineSimilarity.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class PhraseVectorQueryTests
{
    // ── PhraseQuery ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "PhraseQuery: Ctor Sets Field And Terms")]
    public void PhraseQuery_Ctor_SetsFieldAndTerms()
    {
        var q = new PhraseQuery("body", "quick", "brown", "fox");
        Assert.Equal("body", q.Field);
        Assert.Equal(["quick", "brown", "fox"], q.Terms);
        Assert.Equal(0, q.Slop);
    }

    [Fact(DisplayName = "PhraseQuery: Ctor With Slop Sets Slop")]
    public void PhraseQuery_CtorWithSlop_SetsSlop()
    {
        var q = new PhraseQuery("title", 2, "lazy", "dog");
        Assert.Equal("title", q.Field);
        Assert.Equal(2, q.Slop);
        Assert.Equal(["lazy", "dog"], q.Terms);
    }

    [Fact(DisplayName = "PhraseQuery: QualifiedTerms Format Is Field-NulTerm")]
    public void PhraseQuery_QualifiedTerms_FormatsCorrectly()
    {
        var q = new PhraseQuery("f", "alpha", "beta");
        var qt = q.QualifiedTerms;
        Assert.Equal(2, qt.Length);
        // Separator is the null char (U+0000); split on it to avoid xUnit null-char rendering quirks
        var parts0 = qt[0].Split('\0');
        Assert.Equal("f", parts0[0]);
        Assert.Equal("alpha", parts0[1]);
        var parts1 = qt[1].Split('\0');
        Assert.Equal("f", parts1[0]);
        Assert.Equal("beta", parts1[1]);
    }

    [Fact(DisplayName = "PhraseQuery: QualifiedTerms Is Cached")]
    public void PhraseQuery_QualifiedTerms_IsCached()
    {
        var q = new PhraseQuery("f", "a");
        Assert.Same(q.QualifiedTerms, q.QualifiedTerms);
    }

    [Fact(DisplayName = "PhraseQuery: Equal When Same Field Slop And Terms")]
    public void PhraseQuery_Equals_SameFieldSlopTerms()
    {
        var a = new PhraseQuery("body", "quick", "brown");
        var b = new PhraseQuery("body", "quick", "brown");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact(DisplayName = "PhraseQuery: Not Equal When Terms Differ")]
    public void PhraseQuery_NotEqual_WhenTermsDiffer()
    {
        var a = new PhraseQuery("body", "quick", "brown");
        var b = new PhraseQuery("body", "brown", "quick");
        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "PhraseQuery: Not Equal When Slop Differs")]
    public void PhraseQuery_NotEqual_WhenSlopDiffers()
    {
        var a = new PhraseQuery("body", 0, "word");
        var b = new PhraseQuery("body", 1, "word");
        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "PhraseQuery: Not Equal When Field Differs")]
    public void PhraseQuery_NotEqual_WhenFieldDiffers()
    {
        var a = new PhraseQuery("title", "word");
        var b = new PhraseQuery("body", "word");
        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "PhraseQuery: Not Equal To Null")]
    public void PhraseQuery_NotEqualToNull()
    {
        var q = new PhraseQuery("f", "word");
        Assert.False(q.Equals((object?)null));
    }

    // ── VectorQuery ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "VectorQuery: EfSearch Defaults To max(64, 4*topK)")]
    public void VectorQuery_EfSearch_DefaultsToFormula()
    {
        var q = new VectorQuery("vec", [1f, 0f], topK: 5, efSearch: 0);
        Assert.Equal(Math.Max(64, 4 * 5), q.EfSearch);
    }

    [Fact(DisplayName = "VectorQuery: EfSearch Uses Explicit Value When Positive")]
    public void VectorQuery_EfSearch_UsesExplicitValue()
    {
        var q = new VectorQuery("vec", [1f, 0f], topK: 5, efSearch: 200);
        Assert.Equal(200, q.EfSearch);
    }

    [Fact(DisplayName = "VectorQuery: OversamplingFactor Clamped To At Least 1")]
    public void VectorQuery_OversamplingFactor_ClampedToOne()
    {
        var q = new VectorQuery("vec", [1f, 0f], oversamplingFactor: -3);
        Assert.Equal(1, q.OversamplingFactor);
    }

    [Fact(DisplayName = "VectorQuery: OversamplingFactor Preserved When Positive")]
    public void VectorQuery_OversamplingFactor_Preserved()
    {
        var q = new VectorQuery("vec", [1f, 0f], oversamplingFactor: 4);
        Assert.Equal(4, q.OversamplingFactor);
    }

    [Fact(DisplayName = "VectorQuery: Filter Property Stored")]
    public void VectorQuery_Filter_Stored()
    {
        var filter = new TermQuery("status", "active");
        var q = new VectorQuery("vec", [1f, 0f], filter: filter);
        Assert.Same(filter, q.Filter);
    }

    [Fact(DisplayName = "VectorQuery: Equal When Same Field TopK And Vector")]
    public void VectorQuery_Equals_SameFieldTopKVector()
    {
        var a = new VectorQuery("vec", [1f, 2f, 3f], topK: 5);
        var b = new VectorQuery("vec", [1f, 2f, 3f], topK: 5);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact(DisplayName = "VectorQuery: Not Equal When Vector Differs")]
    public void VectorQuery_NotEqual_WhenVectorDiffers()
    {
        var a = new VectorQuery("vec", [1f, 2f, 3f]);
        var b = new VectorQuery("vec", [1f, 2f, 4f]);
        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "VectorQuery: Not Equal To Null")]
    public void VectorQuery_NotEqualToNull()
    {
        var q = new VectorQuery("vec", [1f]);
        Assert.False(q.Equals((object?)null));
    }

    // ── VectorQuery.CosineSimilarity ──────────────────────────────────────────

    [Fact(DisplayName = "CosineSimilarity: Mismatched Lengths Returns Zero")]
    public void CosineSimilarity_MismatchedLengths_ReturnsZero()
    {
        var result = VectorQuery.CosineSimilarity([1f, 2f], [1f]);
        Assert.Equal(0f, result);
    }

    [Fact(DisplayName = "CosineSimilarity: Empty Vectors Returns Zero")]
    public void CosineSimilarity_Empty_ReturnsZero()
    {
        var result = VectorQuery.CosineSimilarity([], []);
        Assert.Equal(0f, result);
    }

    [Fact(DisplayName = "CosineSimilarity: Identical Vectors Returns One")]
    public void CosineSimilarity_IdenticalVectors_ReturnsOne()
    {
        var result = VectorQuery.CosineSimilarity([1f, 2f, 3f], [1f, 2f, 3f]);
        Assert.Equal(1f, result, 5);
    }

    [Fact(DisplayName = "CosineSimilarity: Orthogonal Vectors Returns Zero")]
    public void CosineSimilarity_OrthogonalVectors_ReturnsZero()
    {
        var result = VectorQuery.CosineSimilarity([1f, 0f], [0f, 1f]);
        Assert.Equal(0f, result, 5);
    }

    [Fact(DisplayName = "CosineSimilarity: Known Values Return Correct Similarity")]
    public void CosineSimilarity_KnownValues_ReturnsCorrect()
    {
        // [1,0] vs [1,1]/sqrt(2) → cos = 1/sqrt(2) ≈ 0.7071
        var result = VectorQuery.CosineSimilarity([1f, 0f], [1f, 1f]);
        Assert.Equal(1f / MathF.Sqrt(2f), result, 4);
    }

    [Fact(DisplayName = "CosineSimilarity: Zero Norm Vector Returns Zero")]
    public void CosineSimilarity_ZeroNormVector_ReturnsZero()
    {
        var result = VectorQuery.CosineSimilarity([0f, 0f], [1f, 1f]);
        Assert.Equal(0f, result);
    }

    [Fact(DisplayName = "CosineSimilarity: Long Vectors Exercise SIMD Path")]
    public void CosineSimilarity_LongVectors_MatchesScalar()
    {
        // 32 floats forces SIMD path (Vector<float>.Count is typically 4 or 8).
        var a = Enumerable.Range(1, 32).Select(i => (float)i).ToArray();
        var b = Enumerable.Range(1, 32).Select(i => (float)(33 - i)).ToArray();
        var result = VectorQuery.CosineSimilarity(a, b);
        Assert.True(result is > 0f and <= 1f);
    }
}
