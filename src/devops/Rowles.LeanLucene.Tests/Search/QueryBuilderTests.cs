using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Search.Queries;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for Query Builder.
/// </summary>
public sealed class QueryBuilderTests
{
    /// <summary>
    /// Verifies the Term: Creates Term Query scenario.
    /// </summary>
    [Fact(DisplayName = "Term: Creates Term Query")]
    public void Term_CreatesTermQuery()
    {
        var q = QueryBuilder.Term("body", "hello");
        Assert.Equal("body", q.Field);
        Assert.Equal("hello", q.Term);
    }

    /// <summary>
    /// Verifies the Phrase: Creates Phrase Query scenario.
    /// </summary>
    [Fact(DisplayName = "Phrase: Creates Phrase Query")]
    public void Phrase_CreatesPhraseQuery()
    {
        var q = QueryBuilder.Phrase("body", "quick", "fox");
        Assert.Equal("body", q.Field);
        Assert.Equal(new[] { "quick", "fox" }, q.Terms);
    }

    /// <summary>
    /// Verifies the Bool: Builder Creates Valid Boolean Query scenario.
    /// </summary>
    [Fact(DisplayName = "Bool: Builder Creates Valid Boolean Query")]
    public void Bool_Builder_CreatesValidBooleanQuery()
    {
        var q = QueryBuilder.Bool(b => b
            .Must(QueryBuilder.Term("title", "hello"))
            .Should(QueryBuilder.Term("body", "world"))
            .MustNot(QueryBuilder.Term("status", "deleted")));

        Assert.Equal(3, q.Clauses.Count);
        Assert.Equal(Occur.Must, q.Clauses[0].Occur);
        Assert.Equal(Occur.Should, q.Clauses[1].Occur);
        Assert.Equal(Occur.MustNot, q.Clauses[2].Occur);
    }

    /// <summary>
    /// Verifies the With Boost: Sets Boost And Returns Same Query scenario.
    /// </summary>
    [Fact(DisplayName = "With Boost: Sets Boost And Returns Same Query")]
    public void WithBoost_SetsBoostAndReturnsSameQuery()
    {
        var q = QueryBuilder.Term("body", "hello").WithBoost(2.5f);
        Assert.Equal(2.5f, q.Boost);
        Assert.IsType<TermQuery>(q);
    }

    /// <summary>
    /// Verifies the And: Combines Queries With Must scenario.
    /// </summary>
    [Fact(DisplayName = "And: Combines Queries With Must")]
    public void And_CombinesQueriesWithMust()
    {
        var left = QueryBuilder.Term("body", "hello");
        var right = QueryBuilder.Term("body", "world");
        var combined = left.And(right);

        Assert.Equal(2, combined.Clauses.Count);
        Assert.All(combined.Clauses, c => Assert.Equal(Occur.Must, c.Occur));
    }

    /// <summary>
    /// Verifies the Or: Combines Queries With Should scenario.
    /// </summary>
    [Fact(DisplayName = "Or: Combines Queries With Should")]
    public void Or_CombinesQueriesWithShould()
    {
        var left = QueryBuilder.Term("body", "hello");
        var right = QueryBuilder.Term("body", "world");
        var combined = left.Or(right);

        Assert.Equal(2, combined.Clauses.Count);
        Assert.All(combined.Clauses, c => Assert.Equal(Occur.Should, c.Occur));
    }

    /// <summary>
    /// Verifies the Not: Creates Must And Must Not Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Not: Creates Must And Must Not Clauses")]
    public void Not_CreatesMustAndMustNotClauses()
    {
        var main = QueryBuilder.Term("body", "hello");
        var excluded = QueryBuilder.Term("status", "deleted");
        var combined = main.Not(excluded);

        Assert.Equal(2, combined.Clauses.Count);
        Assert.Equal(Occur.Must, combined.Clauses[0].Occur);
        Assert.Equal(Occur.MustNot, combined.Clauses[1].Occur);
    }

    /// <summary>
    /// Verifies the Dis Max: Combines Disjuncts scenario.
    /// </summary>
    [Fact(DisplayName = "Dis Max: Combines Disjuncts")]
    public void DisMax_CombinesDisjuncts()
    {
        var q = QueryBuilder.DisMax(0.1f,
            QueryBuilder.Term("title", "hello"),
            QueryBuilder.Term("body", "hello"));

        Assert.Equal(2, q.Disjuncts.Count);
        Assert.Equal(0.1f, q.TieBreakerMultiplier);
    }

    /// <summary>
    /// Verifies the Range: Creates Range Query scenario.
    /// </summary>
    [Fact(DisplayName = "Range: Creates Range Query")]
    public void Range_CreatesRangeQuery()
    {
        var q = QueryBuilder.Range("price", 10, 100);
        Assert.Equal("price", q.Field);
        Assert.Equal(10, q.Min);
        Assert.Equal(100, q.Max);
    }
}
