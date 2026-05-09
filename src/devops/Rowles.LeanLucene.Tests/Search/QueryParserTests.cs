using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for Query Parser.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "QueryParser")]
public sealed class QueryParserTests
{
    private readonly QueryParser _parser = new("body", new StandardAnalyser());

    /// <summary>
    /// Verifies the Parse: Single Term Returns Term Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Single Term Returns Term Query")]
    public void Parse_SingleTerm_ReturnsTermQuery()
    {
        var query = _parser.Parse("lucene");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("body", tq.Field);
        Assert.Equal("lucene", tq.Term);
    }

    /// <summary>
    /// Verifies the Parse: Field Colon Term Returns Term Query With Field scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Field Colon Term Returns Term Query With Field")]
    public void Parse_FieldColonTerm_ReturnsTermQueryWithField()
    {
        var query = _parser.Parse("title:search");
        var tq = Assert.IsType<TermQuery>(query);
        Assert.Equal("title", tq.Field);
        Assert.Equal("search", tq.Term);
    }

    /// <summary>
    /// Verifies the Parse: Quoted Phrase Returns Phrase Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Quoted Phrase Returns Phrase Query")]
    public void Parse_QuotedPhrase_ReturnsPhraseQuery()
    {
        var query = _parser.Parse("\"quick brown fox\"");
        var pq = Assert.IsType<PhraseQuery>(query);
        Assert.Equal("body", pq.Field);
        Assert.Equal(new[] { "quick", "brown", "fox" }, pq.Terms);
    }

    /// <summary>
    /// Verifies the Parse: Required Term Returns Must Clause scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Required Term Returns Must Clause")]
    public void Parse_RequiredTerm_ReturnsMustClause()
    {
        var query = _parser.Parse("+required");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.Must, bq.Clauses[0].Occur);
    }

    /// <summary>
    /// Verifies the Parse: Excluded Term Returns Must Not Clause scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Excluded Term Returns Must Not Clause")]
    public void Parse_ExcludedTerm_ReturnsMustNotClause()
    {
        var query = _parser.Parse("-excluded");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.MustNot, bq.Clauses[0].Occur);
    }

    /// <summary>
    /// Verifies the Parse: Multiple Terms Returns Boolean With Should Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Multiple Terms Returns Boolean With Should Clauses")]
    public void Parse_MultipleTerms_ReturnsBooleanWithShouldClauses()
    {
        var query = _parser.Parse("quick brown fox");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(3, bq.Clauses.Count);
        Assert.All(bq.Clauses, c => Assert.Equal(Occur.Should, c.Occur));
    }

    /// <summary>
    /// Verifies the Parse: Prefix Wildcard Returns Prefix Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Prefix Wildcard Returns Prefix Query")]
    public void Parse_PrefixWildcard_ReturnsPrefixQuery()
    {
        var query = _parser.Parse("search*");
        var pq = Assert.IsType<PrefixQuery>(query);
        Assert.Equal("body", pq.Field);
        Assert.Equal("search", pq.Prefix);
    }

    /// <summary>
    /// Verifies the Parse: Wildcard Pattern Returns Wildcard Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Wildcard Pattern Returns Wildcard Query")]
    public void Parse_WildcardPattern_ReturnsWildcardQuery()
    {
        var query = _parser.Parse("te?t");
        var wq = Assert.IsType<WildcardQuery>(query);
        Assert.Equal("body", wq.Field);
        Assert.Equal("te?t", wq.Pattern);
    }

    /// <summary>
    /// Verifies the Parse: Fuzzy Term Returns Fuzzy Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Fuzzy Term Returns Fuzzy Query")]
    public void Parse_FuzzyTerm_ReturnsFuzzyQuery()
    {
        var query = _parser.Parse("lucene~2");
        var fq = Assert.IsType<FuzzyQuery>(query);
        Assert.Equal("body", fq.Field);
        Assert.Equal("lucene", fq.Term);
        Assert.Equal(2, fq.MaxEdits);
    }

    /// <summary>
    /// Verifies the Parse: Phrase With Slop Returns Phrase Query With Slop scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Phrase With Slop Returns Phrase Query With Slop")]
    public void Parse_PhraseWithSlop_ReturnsPhraseQueryWithSlop()
    {
        var query = _parser.Parse("\"quick fox\"~2");
        var pq = Assert.IsType<PhraseQuery>(query);
        Assert.Equal(2, pq.Slop);
    }

    /// <summary>
    /// Verifies the Parse: Boost Suffix Sets Boost On Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Boost Suffix Sets Boost On Query")]
    public void Parse_BoostSuffix_SetsBoostOnQuery()
    {
        var query = _parser.Parse("important^3.5");
        Assert.Equal(3.5f, query.Boost, 0.01f);
    }

    /// <summary>
    /// Verifies the Parse: Empty String Returns Boolean Query With No Clauses scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Empty String Returns Boolean Query With No Clauses")]
    public void Parse_EmptyString_ReturnsBooleanQueryWithNoClauses()
    {
        var query = _parser.Parse("");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Empty(bq.Clauses);
    }

    /// <summary>
    /// Verifies the Parse: Grouped Parens Returns Nested Boolean Query scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Grouped Parens Returns Nested Boolean Query")]
    public void Parse_GroupedParens_ReturnsNestedBooleanQuery()
    {
        var query = _parser.Parse("+(quick brown)");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Single(bq.Clauses);
        Assert.Equal(Occur.Must, bq.Clauses[0].Occur);
        Assert.IsType<BooleanQuery>(bq.Clauses[0].Query);
    }

    /// <summary>
    /// Verifies the Parse: Field Colon Phrase Returns Phrase Query On Field scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Field Colon Phrase Returns Phrase Query On Field")]
    public void Parse_FieldColonPhrase_ReturnsPhraseQueryOnField()
    {
        var query = _parser.Parse("title:\"exact match\"");
        var pq = Assert.IsType<PhraseQuery>(query);
        Assert.Equal("title", pq.Field);
    }

    /// <summary>
    /// Verifies the Parse: Mixed Clauses Correct Occur Types scenario.
    /// </summary>
    [Fact(DisplayName = "Parse: Mixed Clauses Correct Occur Types")]
    public void Parse_MixedClauses_CorrectOccurTypes()
    {
        var query = _parser.Parse("+required optional -excluded");
        var bq = Assert.IsType<BooleanQuery>(query);
        Assert.Equal(3, bq.Clauses.Count);
        Assert.Equal(Occur.Must, bq.Clauses[0].Occur);
        Assert.Equal(Occur.Should, bq.Clauses[1].Occur);
        Assert.Equal(Occur.MustNot, bq.Clauses[2].Occur);
    }

    /// <summary>
    /// Verifies the Parse: Invalid Syntax Throws Query Parse Exception scenario.
    /// </summary>
    /// <param name="query">The query value for the test case.</param>
    [Theory(DisplayName = "Parse: Invalid Syntax Throws Query Parse Exception")]
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
