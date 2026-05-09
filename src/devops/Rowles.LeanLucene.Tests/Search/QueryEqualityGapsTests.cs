using Rowles.LeanLucene.Search.Queries;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Unit tests covering Equals, GetHashCode, and property accessors for query types
/// that were missing coverage: FuzzyQuery, PrefixQuery, RegexpQuery, RangeQuery,
/// TermRangeQuery, GeoBoundingBoxQuery, GeoDistanceQuery, and FunctionScoreQuery.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class QueryEqualityGapsTests
{
    // ── FuzzyQuery ────────────────────────────────────────────────────────────

    /// <summary>Verifies FuzzyQuery exposes Field, Term, MaxEdits, and MaxExpansions.</summary>
    [Fact(DisplayName = "FuzzyQuery: Properties Reflect Constructor Arguments")]
    public void FuzzyQuery_Properties_ReflectConstructorArguments()
    {
        var q = new FuzzyQuery("title", "search", maxEdits: 1, maxExpansions: 32);
        Assert.Equal("title", q.Field);
        Assert.Equal("search", q.Term);
        Assert.Equal(1, q.MaxEdits);
        Assert.Equal(32, q.MaxExpansions);
    }

    /// <summary>Verifies two FuzzyQuery instances with identical arguments are equal.</summary>
    [Fact(DisplayName = "FuzzyQuery: Equal Instances Are Equal")]
    public void FuzzyQuery_EqualInstances_AreEqual()
    {
        var a = new FuzzyQuery("f", "word", 2, 64);
        var b = new FuzzyQuery("f", "word", 2, 64);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Verifies FuzzyQuery instances differ when MaxEdits differs.</summary>
    [Fact(DisplayName = "FuzzyQuery: Differ By MaxEdits")]
    public void FuzzyQuery_DifferByMaxEdits_NotEqual()
    {
        var a = new FuzzyQuery("f", "word", 1, 64);
        var b = new FuzzyQuery("f", "word", 2, 64);
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies FuzzyQuery instances differ when MaxExpansions differs.</summary>
    [Fact(DisplayName = "FuzzyQuery: Differ By MaxExpansions")]
    public void FuzzyQuery_DifferByMaxExpansions_NotEqual()
    {
        var a = new FuzzyQuery("f", "word", 2, 32);
        var b = new FuzzyQuery("f", "word", 2, 64);
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies FuzzyQuery.Equals returns false for null.</summary>
    [Fact(DisplayName = "FuzzyQuery: Not Equal To Null")]
    public void FuzzyQuery_NotEqualToNull()
    {
        var q = new FuzzyQuery("f", "word");
        Assert.False(q.Equals((object?)null));
    }

    // ── PrefixQuery ───────────────────────────────────────────────────────────

    /// <summary>Verifies PrefixQuery exposes Field and Prefix.</summary>
    [Fact(DisplayName = "PrefixQuery: Properties Reflect Constructor Arguments")]
    public void PrefixQuery_Properties_ReflectConstructorArguments()
    {
        var q = new PrefixQuery("content", "pre");
        Assert.Equal("content", q.Field);
        Assert.Equal("pre", q.Prefix);
    }

    /// <summary>Verifies two identical PrefixQuery instances are equal.</summary>
    [Fact(DisplayName = "PrefixQuery: Equal Instances Are Equal")]
    public void PrefixQuery_EqualInstances_AreEqual()
    {
        var a = new PrefixQuery("f", "abc");
        var b = new PrefixQuery("f", "abc");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Verifies PrefixQuery instances differ when Prefix differs.</summary>
    [Fact(DisplayName = "PrefixQuery: Differ By Prefix")]
    public void PrefixQuery_DifferByPrefix_NotEqual()
    {
        var a = new PrefixQuery("f", "abc");
        var b = new PrefixQuery("f", "xyz");
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies PrefixQuery instances differ when Field differs.</summary>
    [Fact(DisplayName = "PrefixQuery: Differ By Field")]
    public void PrefixQuery_DifferByField_NotEqual()
    {
        var a = new PrefixQuery("f1", "abc");
        var b = new PrefixQuery("f2", "abc");
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies PrefixQuery is not equal to a different type.</summary>
    [Fact(DisplayName = "PrefixQuery: Not Equal To Different Type")]
    public void PrefixQuery_NotEqualToDifferentType()
    {
        var q = new PrefixQuery("f", "abc");
        Assert.False(q.Equals("not a query"));
    }

    // ── RegexpQuery ───────────────────────────────────────────────────────────

    /// <summary>Verifies RegexpQuery exposes Field and Pattern.</summary>
    [Fact(DisplayName = "RegexpQuery: Properties Reflect Constructor Arguments")]
    public void RegexpQuery_Properties_ReflectConstructorArguments()
    {
        var q = new RegexpQuery("body", "^hello");
        Assert.Equal("body", q.Field);
        Assert.Equal("^hello", q.Pattern);
    }

    /// <summary>Verifies two identical RegexpQuery instances are equal.</summary>
    [Fact(DisplayName = "RegexpQuery: Equal Instances Are Equal")]
    public void RegexpQuery_EqualInstances_AreEqual()
    {
        var a = new RegexpQuery("f", "^abc");
        var b = new RegexpQuery("f", "^abc");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Verifies RegexpQuery instances differ when Pattern differs.</summary>
    [Fact(DisplayName = "RegexpQuery: Differ By Pattern")]
    public void RegexpQuery_DifferByPattern_NotEqual()
    {
        var a = new RegexpQuery("f", "^abc");
        var b = new RegexpQuery("f", "^xyz");
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies RegexpQuery accepts RegexOptions parameter.</summary>
    [Fact(DisplayName = "RegexpQuery: Accepts RegexOptions")]
    public void RegexpQuery_AcceptsRegexOptions()
    {
        var q = new RegexpQuery("f", "^abc", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.Equal("^abc", q.Pattern);
    }

    // ── RangeQuery ────────────────────────────────────────────────────────────

    /// <summary>Verifies RangeQuery exposes Field, Min, and Max.</summary>
    [Fact(DisplayName = "RangeQuery: Properties Reflect Constructor Arguments")]
    public void RangeQuery_Properties_ReflectConstructorArguments()
    {
        var q = new RangeQuery("price", 10.0, 99.99);
        Assert.Equal("price", q.Field);
        Assert.Equal(10.0, q.Min);
        Assert.Equal(99.99, q.Max);
    }

    /// <summary>Verifies two identical RangeQuery instances are equal.</summary>
    [Fact(DisplayName = "RangeQuery: Equal Instances Are Equal")]
    public void RangeQuery_EqualInstances_AreEqual()
    {
        var a = new RangeQuery("f", 1.0, 5.0);
        var b = new RangeQuery("f", 1.0, 5.0);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Verifies RangeQuery instances differ when Min differs.</summary>
    [Fact(DisplayName = "RangeQuery: Differ By Min")]
    public void RangeQuery_DifferByMin_NotEqual()
    {
        var a = new RangeQuery("f", 1.0, 5.0);
        var b = new RangeQuery("f", 2.0, 5.0);
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies RangeQuery instances differ when Max differs.</summary>
    [Fact(DisplayName = "RangeQuery: Differ By Max")]
    public void RangeQuery_DifferByMax_NotEqual()
    {
        var a = new RangeQuery("f", 1.0, 5.0);
        var b = new RangeQuery("f", 1.0, 9.0);
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies RangeQuery.Equals returns false for null.</summary>
    [Fact(DisplayName = "RangeQuery: Not Equal To Null")]
    public void RangeQuery_NotEqualToNull()
    {
        var q = new RangeQuery("f", 0.0, 1.0);
        Assert.False(q.Equals((object?)null));
    }

    // ── TermRangeQuery ────────────────────────────────────────────────────────

    /// <summary>Verifies TermRangeQuery exposes all properties.</summary>
    [Fact(DisplayName = "TermRangeQuery: Properties Reflect Constructor Arguments")]
    public void TermRangeQuery_Properties_ReflectConstructorArguments()
    {
        var q = new TermRangeQuery("fruit", "apple", "mango", includeLower: false, includeUpper: false);
        Assert.Equal("fruit", q.Field);
        Assert.Equal("apple", q.LowerTerm);
        Assert.Equal("mango", q.UpperTerm);
        Assert.False(q.IncludeLower);
        Assert.False(q.IncludeUpper);
    }

    /// <summary>Verifies two identical TermRangeQuery instances are equal.</summary>
    [Fact(DisplayName = "TermRangeQuery: Equal Instances Are Equal")]
    public void TermRangeQuery_EqualInstances_AreEqual()
    {
        var a = new TermRangeQuery("f", "a", "z");
        var b = new TermRangeQuery("f", "a", "z");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Verifies TermRangeQuery handles null bounds in equality check.</summary>
    [Fact(DisplayName = "TermRangeQuery: Null Bounds Equal")]
    public void TermRangeQuery_NullBounds_Equal()
    {
        var a = new TermRangeQuery("f", null, null);
        var b = new TermRangeQuery("f", null, null);
        Assert.Equal(a, b);
    }

    /// <summary>Verifies TermRangeQuery instances differ when IncludeLower differs.</summary>
    [Fact(DisplayName = "TermRangeQuery: Differ By IncludeLower")]
    public void TermRangeQuery_DifferByIncludeLower_NotEqual()
    {
        var a = new TermRangeQuery("f", "a", "z", includeLower: true);
        var b = new TermRangeQuery("f", "a", "z", includeLower: false);
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies TermRangeQuery instances differ when IncludeUpper differs.</summary>
    [Fact(DisplayName = "TermRangeQuery: Differ By IncludeUpper")]
    public void TermRangeQuery_DifferByIncludeUpper_NotEqual()
    {
        var a = new TermRangeQuery("f", "a", "z", includeUpper: true);
        var b = new TermRangeQuery("f", "a", "z", includeUpper: false);
        Assert.NotEqual(a, b);
    }

    // ── GeoBoundingBoxQuery ───────────────────────────────────────────────────

    /// <summary>Verifies GeoBoundingBoxQuery exposes all coordinate properties.</summary>
    [Fact(DisplayName = "GeoBoundingBoxQuery: Properties Reflect Constructor Arguments")]
    public void GeoBoundingBoxQuery_Properties_ReflectConstructorArguments()
    {
        var q = new GeoBoundingBoxQuery("loc", 10.0, 20.0, -30.0, 40.0);
        Assert.Equal("loc", q.Field);
        Assert.Equal(10.0, q.MinLat);
        Assert.Equal(20.0, q.MaxLat);
        Assert.Equal(-30.0, q.MinLon);
        Assert.Equal(40.0, q.MaxLon);
    }

    /// <summary>Verifies two identical GeoBoundingBoxQuery instances are equal.</summary>
    [Fact(DisplayName = "GeoBoundingBoxQuery: Equal Instances Are Equal")]
    public void GeoBoundingBoxQuery_EqualInstances_AreEqual()
    {
        var a = new GeoBoundingBoxQuery("f", 1, 2, 3, 4);
        var b = new GeoBoundingBoxQuery("f", 1, 2, 3, 4);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Verifies GeoBoundingBoxQuery instances differ when any coordinate differs.</summary>
    [Fact(DisplayName = "GeoBoundingBoxQuery: Differ By Coordinate")]
    public void GeoBoundingBoxQuery_DifferByCoordinate_NotEqual()
    {
        var a = new GeoBoundingBoxQuery("f", 1, 2, 3, 4);
        var b = new GeoBoundingBoxQuery("f", 1, 2, 3, 5);
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies GeoBoundingBoxQuery throws when field is null.</summary>
    [Fact(DisplayName = "GeoBoundingBoxQuery: Null Field Throws")]
    public void GeoBoundingBoxQuery_NullField_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GeoBoundingBoxQuery(null!, 1, 2, 3, 4));
    }

    // ── GeoDistanceQuery ──────────────────────────────────────────────────────

    /// <summary>Verifies GeoDistanceQuery exposes all coordinate and radius properties.</summary>
    [Fact(DisplayName = "GeoDistanceQuery: Properties Reflect Constructor Arguments")]
    public void GeoDistanceQuery_Properties_ReflectConstructorArguments()
    {
        var q = new GeoDistanceQuery("location", 51.5, -0.13, 5000.0);
        Assert.Equal("location", q.Field);
        Assert.Equal(51.5, q.CentreLat);
        Assert.Equal(-0.13, q.CentreLon);
        Assert.Equal(5000.0, q.RadiusMetres);
    }

    /// <summary>Verifies two identical GeoDistanceQuery instances are equal.</summary>
    [Fact(DisplayName = "GeoDistanceQuery: Equal Instances Are Equal")]
    public void GeoDistanceQuery_EqualInstances_AreEqual()
    {
        var a = new GeoDistanceQuery("f", 51.5, -0.1, 1000.0);
        var b = new GeoDistanceQuery("f", 51.5, -0.1, 1000.0);
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Verifies GeoDistanceQuery instances differ when radius differs.</summary>
    [Fact(DisplayName = "GeoDistanceQuery: Differ By Radius")]
    public void GeoDistanceQuery_DifferByRadius_NotEqual()
    {
        var a = new GeoDistanceQuery("f", 51.5, -0.1, 1000.0);
        var b = new GeoDistanceQuery("f", 51.5, -0.1, 2000.0);
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies GeoDistanceQuery throws when field is null.</summary>
    [Fact(DisplayName = "GeoDistanceQuery: Null Field Throws")]
    public void GeoDistanceQuery_NullField_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new GeoDistanceQuery(null!, 51.5, -0.1, 1000.0));
    }

    // ── FunctionScoreQuery ────────────────────────────────────────────────────

    /// <summary>Verifies FunctionScoreQuery exposes Inner, NumericField, Mode, and Field.</summary>
    [Fact(DisplayName = "FunctionScoreQuery: Properties Reflect Constructor Arguments")]
    public void FunctionScoreQuery_Properties_ReflectConstructorArguments()
    {
        var inner = new TermQuery("body", "word");
        var q = new FunctionScoreQuery(inner, "score", ScoreMode.Multiply);
        Assert.Same(inner, q.Inner);
        Assert.Equal("score", q.NumericField);
        Assert.Equal(ScoreMode.Multiply, q.Mode);
        Assert.Equal("body", q.Field);
    }

    /// <summary>Verifies two identical FunctionScoreQuery instances are equal.</summary>
    [Fact(DisplayName = "FunctionScoreQuery: Equal Instances Are Equal")]
    public void FunctionScoreQuery_EqualInstances_AreEqual()
    {
        var a = new FunctionScoreQuery(new TermQuery("f", "t"), "score");
        var b = new FunctionScoreQuery(new TermQuery("f", "t"), "score");
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>Verifies FunctionScoreQuery instances differ when Mode differs.</summary>
    [Fact(DisplayName = "FunctionScoreQuery: Differ By Mode")]
    public void FunctionScoreQuery_DifferByMode_NotEqual()
    {
        var a = new FunctionScoreQuery(new TermQuery("f", "t"), "score", ScoreMode.Multiply);
        var b = new FunctionScoreQuery(new TermQuery("f", "t"), "score", ScoreMode.Replace);
        Assert.NotEqual(a, b);
    }

    /// <summary>Verifies FunctionScoreQuery throws when inner query is null.</summary>
    [Fact(DisplayName = "FunctionScoreQuery: Null Inner Throws")]
    public void FunctionScoreQuery_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new FunctionScoreQuery(null!, "score"));
    }

    /// <summary>Verifies FunctionScoreQuery.Combine returns correct value for Multiply mode.</summary>
    [Fact(DisplayName = "FunctionScoreQuery: Combine Multiply")]
    public void FunctionScoreQuery_Combine_Multiply()
    {
        float result = FunctionScoreQuery.Combine(2.0f, 3.0, ScoreMode.Multiply);
        Assert.Equal(6.0f, result, precision: 5);
    }

    /// <summary>Verifies FunctionScoreQuery.Combine returns field value for Replace mode.</summary>
    [Fact(DisplayName = "FunctionScoreQuery: Combine Replace")]
    public void FunctionScoreQuery_Combine_Replace()
    {
        float result = FunctionScoreQuery.Combine(2.0f, 3.0, ScoreMode.Replace);
        Assert.Equal(3.0f, result, precision: 5);
    }

    /// <summary>Verifies FunctionScoreQuery.Combine sums scores for Sum mode.</summary>
    [Fact(DisplayName = "FunctionScoreQuery: Combine Sum")]
    public void FunctionScoreQuery_Combine_Sum()
    {
        float result = FunctionScoreQuery.Combine(2.0f, 3.0, ScoreMode.Sum);
        Assert.Equal(5.0f, result, precision: 5);
    }

    /// <summary>Verifies FunctionScoreQuery.Combine returns max score for Max mode.</summary>
    [Fact(DisplayName = "FunctionScoreQuery: Combine Max")]
    public void FunctionScoreQuery_Combine_Max()
    {
        float result = FunctionScoreQuery.Combine(2.0f, 5.0, ScoreMode.Max);
        Assert.Equal(5.0f, result, precision: 5);
    }

    /// <summary>Verifies FunctionScoreQuery.Combine returns query score for unknown mode.</summary>
    [Fact(DisplayName = "FunctionScoreQuery: Combine Default Falls Through")]
    public void FunctionScoreQuery_Combine_DefaultFallsThrough()
    {
        float result = FunctionScoreQuery.Combine(2.0f, 99.0, (ScoreMode)999);
        Assert.Equal(2.0f, result, precision: 5);
    }
}
