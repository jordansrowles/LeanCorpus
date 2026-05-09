using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for Highlighter.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "Highlighter")]
public sealed class HighlighterTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public HighlighterTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Verifies the Get Best Fragment: Matching Terms Highlights Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Get Best Fragment: Matching Terms Highlights Correctly")]
    public void GetBestFragment_MatchingTerms_HighlightsCorrectly()
    {
        // Arrange
        var highlighter = new Highlighter("<b>", "</b>");
        var text = "The search engine provides fast performance for all queries.";
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "search", "performance" };

        // Act
        var fragment = highlighter.GetBestFragment(text, terms);

        // Assert
        Assert.Contains("<b>search</b>", fragment);
        Assert.Contains("<b>performance</b>", fragment);
    }

    /// <summary>
    /// Verifies the Get Best Fragment: No Matches Returns Truncated Text scenario.
    /// </summary>
    [Fact(DisplayName = "Get Best Fragment: No Matches Returns Truncated Text")]
    public void GetBestFragment_NoMatches_ReturnsTruncatedText()
    {
        // Arrange
        var highlighter = new Highlighter();
        var text = "No matching terms here.";
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "zebra" };

        // Act
        var fragment = highlighter.GetBestFragment(text, terms);

        // Assert — should return original text (no truncation needed for short text)
        Assert.Equal("No matching terms here.", fragment);
    }

    /// <summary>
    /// Verifies the Get Best Fragment: Empty Text Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Get Best Fragment: Empty Text Returns Empty")]
    public void GetBestFragment_EmptyText_ReturnsEmpty()
    {
        var highlighter = new Highlighter();
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "search" };

        var fragment = highlighter.GetBestFragment("", terms);
        Assert.Equal(string.Empty, fragment);
    }

    /// <summary>
    /// Verifies the Get Best Fragment: Empty Terms Returns Truncated Text scenario.
    /// </summary>
    [Fact(DisplayName = "Get Best Fragment: Empty Terms Returns Truncated Text")]
    public void GetBestFragment_EmptyTerms_ReturnsTruncatedText()
    {
        var highlighter = new Highlighter();
        var text = "Some text content.";

        var fragment = highlighter.GetBestFragment(text, new HashSet<string>());
        Assert.Equal("Some text content.", fragment);
    }

    /// <summary>
    /// Verifies the Get Best Fragment: Custom Tags Uses Provided Tags scenario.
    /// </summary>
    [Fact(DisplayName = "Get Best Fragment: Custom Tags Uses Provided Tags")]
    public void GetBestFragment_CustomTags_UsesProvidedTags()
    {
        // Arrange
        var highlighter = new Highlighter("<em>", "</em>");
        var text = "search engine benchmark";
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "search" };

        // Act
        var fragment = highlighter.GetBestFragment(text, terms);

        // Assert
        Assert.Contains("<em>search</em>", fragment);
        Assert.DoesNotContain("<b>", fragment);
    }

    /// <summary>
    /// Verifies the Extract Terms: Term Query Extracts Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Extract Terms: Term Query Extracts Correctly")]
    public void ExtractTerms_TermQuery_ExtractsCorrectly()
    {
        var query = new TermQuery("body", "search");
        var terms = Highlighter.ExtractTerms(query);

        Assert.Single(terms);
        Assert.Contains("search", terms);
    }

    /// <summary>
    /// Verifies the Extract Terms: Boolean Query Extracts All Non Must Not Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Extract Terms: Boolean Query Extracts All Non Must Not Terms")]
    public void ExtractTerms_BooleanQuery_ExtractsAllNonMustNotTerms()
    {
        var bq = new BooleanQuery();
        bq.Add(new TermQuery("body", "search"), Occur.Must);
        bq.Add(new TermQuery("body", "engine"), Occur.Should);
        bq.Add(new TermQuery("body", "spam"), Occur.MustNot);

        var terms = Highlighter.ExtractTerms(bq);

        Assert.Equal(2, terms.Count);
        Assert.Contains("search", terms);
        Assert.Contains("engine", terms);
        Assert.DoesNotContain("spam", terms);
    }

    /// <summary>
    /// Verifies the Extract Terms: Phrase Query Extracts All Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Extract Terms: Phrase Query Extracts All Terms")]
    public void ExtractTerms_PhraseQuery_ExtractsAllTerms()
    {
        var query = new PhraseQuery("body", "search", "engine");
        var terms = Highlighter.ExtractTerms(query);

        Assert.Equal(2, terms.Count);
        Assert.Contains("search", terms);
        Assert.Contains("engine", terms);
    }
}
