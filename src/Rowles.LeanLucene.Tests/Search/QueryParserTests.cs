using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;

namespace Rowles.LeanLucene.Tests.Search;

[Trait("Category", "Search")]
[Trait("Category", "QueryParser")]
public sealed class QueryParserTests
{
    private readonly QueryParser _parser = new("body", new StandardAnalyser());

    [Fact]
    public void Parse_SingleTerm_ReturnsTermQuery()
    {
        var query = _parser.Parse("lucene");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("body", tq.Field);
        Assert.Equal("lucene", tq.Term);
    }

    [Fact]
    public void Parse_FieldColonTerm_ReturnsTermQueryWithField()
    {
        var query = _parser.Parse("title:search");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("title", tq.Field);
        Assert.Equal("search", tq.Term);
    }

    [Fact]
    public void Parse_QuotedPhrase_ReturnsPhraseQuery()
    {
        var query = _parser.Parse("\"quick brown fox\"");
        var pq = Assert.IsType<PhraseQuery>(query);
        Assert.Equal("body", pq.Field);
        Assert.Equal(new[] { "quick", "brown", "fox" }, pq.Terms);
    }

    [Fact]
    public void Parse_RequiredTerm_ReturnsMustClause()
    {
        var query = _parser.Parse("+required");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.Must, bq.Clauses[0].Occur);
    }

    [Fact]
    public void Parse_ExcludedTerm_ReturnsMustNotClause()
    {
        var query = _parser.Parse("-excluded");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.MustNot, bq.Clauses[0].Occur);
    }

    [Fact]
    public void Parse_MultipleTerms_ReturnsBooleanWithShouldClauses()
    {
        var query = _parser.Parse("quick brown fox");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(3, bq.Clauses.Count);
        Assert.All(bq.Clauses, c => Assert.Equal(Occur.Should, c.Occur));
    }

    [Fact]
    public void Parse_PrefixWildcard_ReturnsPrefixQuery()
    {
        var query = _parser.Parse("search*");
        var pq = Assert.IsType<PrefixQuery>(query);
        Assert.Equal("body", pq.Field);
        Assert.Equal("search", pq.Prefix);
    }

    [Fact]
    public void Parse_WildcardPattern_ReturnsWildcardQuery()
    {
        var query = _parser.Parse("te?t");
        var wq = Assert.IsType<WildcardQuery>(query);
        Assert.Equal("body", wq.Field);
        Assert.Equal("te?t", wq.Pattern);
    }

    [Fact]
    public void Parse_FuzzyTerm_ReturnsFuzzyQuery()
    {
        var query = _parser.Parse("lucene~2");
        var fq = Assert.IsType<FuzzyQuery>(query);
        Assert.Equal("body", fq.Field);
        Assert.Equal("lucene", fq.Term);
        Assert.Equal(2, fq.MaxEdits);
    }

    [Fact]
    public void Parse_PhraseWithSlop_ReturnsPhraseQueryWithSlop()
    {
        var query = _parser.Parse("\"quick fox\"~2");
        var pq = Assert.IsType<PhraseQuery>(query);
        Assert.Equal(2, pq.Slop);
    }

    [Fact]
    public void Parse_BoostSuffix_SetsBoostOnQuery()
    {
        var query = _parser.Parse("important^3.5");
        Assert.Equal(3.5f, query.Boost, 0.01f);
    }

    [Fact]
    public void Parse_EmptyString_ReturnsBooleanQueryWithNoClauses()
    {
        var query = _parser.Parse("");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Empty(bq.Clauses);
    }

    [Fact]
    public void Parse_GroupedParens_ReturnsNestedBooleanQuery()
    {
        var query = _parser.Parse("+(quick brown)");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.Must, bq.Clauses[0].Occur);
        Assert.IsType<BooleanQuery>(bq.Clauses[0].Query);
    }

    [Fact]
    public void Parse_FieldColonPhrase_ReturnsPhraseQueryOnField()
    {
        var query = _parser.Parse("title:\"exact match\"");
        var pq = Assert.IsType<PhraseQuery>(query);
        Assert.Equal("title", pq.Field);
    }

    [Fact]
    public void Parse_MixedClauses_CorrectOccurTypes()
    {
        var query = _parser.Parse("+required optional -excluded");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(3, bq.Clauses.Count);
        Assert.Equal(Occur.Must, bq.Clauses[0].Occur);
        Assert.Equal(Occur.Should, bq.Clauses[1].Occur);
        Assert.Equal(Occur.MustNot, bq.Clauses[2].Occur);
    }

    [Theory]
    [InlineData("\"unterminated")]
    [InlineData("(quick brown")]
    [InlineData("title:")]
    [InlineData("+")]
    [InlineData("quick)")]
    public void Parse_InvalidSyntax_ThrowsQueryParseException(string query)
    {
        Assert.Throws<QueryParseException>(() => _parser.Parse(query));
    }
}
