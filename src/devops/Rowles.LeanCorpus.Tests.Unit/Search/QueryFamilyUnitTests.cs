using System.Reflection;
using System.Runtime.CompilerServices;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;

namespace Rowles.LeanCorpus.Tests.Unit.Search;

/// <summary>
/// Unit tests for the added query families and BM25F helper logic.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class QueryFamilyUnitTests
{
    [Fact(DisplayName = "MatchAllDocsQuery: Equal Instances Are Equal")]
    public void MatchAllDocsQuery_EqualInstances_AreEqual()
    {
        Assert.Equal(new MatchAllDocsQuery(), new MatchAllDocsQuery());
    }

    [Fact(DisplayName = "TermInSetQuery: Normalises Term Order For Equality")]
    public void TermInSetQuery_NormalisesOrder_ForEquality()
    {
        var a = new TermInSetQuery("body", "beta", "alpha", "alpha");
        var b = new TermInSetQuery("body", "alpha", "beta");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact(DisplayName = "TermInSetQuery: Qualified Term Cache Uses Volatile Publication")]
    public void TermInSetQuery_QualifiedTermCache_UsesVolatilePublication()
    {
        var field = typeof(TermInSetQuery).GetField("_cachedQualifiedTerms", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        Assert.Contains(typeof(IsVolatile), field!.GetRequiredCustomModifiers());
    }

    [Fact(DisplayName = "PointInSetQuery: Normalises Point Order For Equality")]
    public void PointInSetQuery_NormalisesOrder_ForEquality()
    {
        var a = new PointInSetQuery("price", 20.0, 10.0, 10.0);
        var b = new PointInSetQuery("price", 10.0, 20.0);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact(DisplayName = "CombinedFieldsQuery: Normalises Fields Terms And Weights")]
    public void CombinedFieldsQuery_NormalisesFieldsTermsAndWeights()
    {
        var a = new CombinedFieldsQuery(
            ["body", "title"],
            ["beta", "alpha", "alpha"],
            fieldWeights: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["title"] = 2.0f,
                ["body"] = 1.0f
            });

        var b = new CombinedFieldsQuery(
            ["title", "body"],
            ["alpha", "beta"],
            fieldWeights: new Dictionary<string, float>(StringComparer.Ordinal)
            {
                ["body"] = 1.0f,
                ["title"] = 2.0f
            });

        Assert.Equal(a, b);
        Assert.Equal(string.Empty, a.Field);
        Assert.Equal(2.0f, a.FieldWeights["title"]);
        Assert.Equal(1.0f, a.FieldWeights["body"]);
    }

    [Fact(DisplayName = "Query Families: Reject Empty Required Inputs")]
    public void QueryFamilies_RejectEmptyRequiredInputs()
    {
        Assert.Throws<ArgumentException>(() => new FieldExistsQuery(""));
        Assert.Throws<ArgumentNullException>(() => new MatchNoDocsQuery(null!));
        Assert.Throws<ArgumentException>(() => new TermInSetQuery("body", [""]));
        Assert.Throws<ArgumentException>(() => new TermInSetQuery("", ["alpha"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PointInSetQuery("price", [double.NaN]));
        Assert.Throws<ArgumentException>(() => new PointInSetQuery("", [1.0]));
        Assert.Throws<ArgumentException>(() => new MultiPhraseQuery("body", [["alpha"], []]));
        Assert.Throws<ArgumentException>(() => new IntervalsTermSource("body", ""));
        Assert.Throws<ArgumentException>(() => new IntervalsPhraseSource("body", []));
        Assert.Throws<ArgumentException>(() => new IntervalsOrSource([]));
        Assert.Throws<ArgumentException>(() => new IntervalsOrderedSource(1, []));
        Assert.Throws<ArgumentException>(() => new IntervalsUnorderedSource(1, []));
        Assert.Throws<ArgumentException>(() => new CombinedFieldsQuery([""], ["alpha"]));
        Assert.Throws<ArgumentException>(() => new CombinedFieldsQuery(["body"], [""]));
        Assert.Throws<ArgumentException>(() => new CombinedFieldsQuery(
            ["body"],
            ["alpha"],
            fieldWeights: new Dictionary<string, float>(StringComparer.Ordinal) { ["title"] = 2.0f }));
    }

    [Fact(DisplayName = "BM25F Helper: Normalised Field Term Frequency Applies Weight")]
    public void Bm25FHelper_NormalisedFieldTermFrequency_AppliesWeight()
    {
        float unweighted = Bm25Scorer.NormaliseFieldTermFrequency(2f, 10, 10f);
        float weighted = Bm25Scorer.NormaliseFieldTermFrequency(2f, 10, 10f, 2f);

        Assert.Equal(unweighted * 2f, weighted, precision: 4);
    }

    [Fact(DisplayName = "BM25F Helper: Combined Score Uses Supplied Idf")]
    public void Bm25FHelper_CombinedScore_UsesSuppliedIdf()
    {
        float idf = Bm25Scorer.Idf(100, 5);
        float score = Bm25Scorer.ScoreCombinedWithIdf(idf, 3.5f);

        Assert.True(score > idf);
    }

    [Fact(DisplayName = "QueryCache: TermInSetQuery Fingerprint Is Order Invariant")]
    public void QueryCache_TermInSetQuery_Fingerprint_IsOrderInvariant()
    {
        var cache = new QueryCache(10);
        cache.Put(new TermInSetQuery("body", "beta", "alpha"), 5, TopDocs.Empty);

        Assert.NotNull(cache.TryGet(new TermInSetQuery("body", "alpha", "beta"), 5));
    }

    [Fact(DisplayName = "QueryCache: PointInSetQuery Fingerprint Is Order Invariant")]
    public void QueryCache_PointInSetQuery_Fingerprint_IsOrderInvariant()
    {
        var cache = new QueryCache(10);
        cache.Put(new PointInSetQuery("price", 2.0, 1.0), 5, TopDocs.Empty);

        Assert.NotNull(cache.TryGet(new PointInSetQuery("price", 1.0, 2.0), 5));
    }

    [Fact(DisplayName = "QueryCache: IntervalsQuery Fingerprint Matches Equivalent Source Tree")]
    public void QueryCache_IntervalsQuery_Fingerprint_MatchesEquivalentSourceTree()
    {
        var cache = new QueryCache(10);
        var cached = new IntervalsQuery(
            new IntervalsContainingSource(
                new IntervalsOrderedSource(1,
                    new IntervalsTermSource("body", "alpha"),
                    new IntervalsTermSource("body", "beta")),
                new IntervalsTermSource("body", "beta")));

        cache.Put(cached, 5, TopDocs.Empty);

        var lookup = new IntervalsQuery(
            new IntervalsContainingSource(
                new IntervalsOrderedSource(1,
                    new IntervalsTermSource("body", "alpha"),
                    new IntervalsTermSource("body", "beta")),
                new IntervalsTermSource("body", "beta")));

        Assert.NotNull(cache.TryGet(lookup, 5));
    }
}
