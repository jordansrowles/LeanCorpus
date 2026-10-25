namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Unit tests for the <see cref="AnalysingSuggester"/> and <see cref="FreeTextSuggester"/>
/// facades: their null guards and their argument-forwarding contracts to the underlying searcher.
/// Delegation is exercised against an empty searcher, so no index needs to be built.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class SuggesterFacadeTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public SuggesterFacadeTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private IndexSearcher CreateEmptySearcher()
    {
        var path = Path.Combine(_fixture.Path, $"empty_{Guid.NewGuid():N}");
        return new IndexSearcher(new MMapDirectory(path));
    }

    [Fact]
    public void AnalysingSuggester_Suggest_NullSearcher_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => AnalysingSuggester.Suggest(null!, "ap", "body", new StandardAnalyser()));
    }

    [Fact]
    public void FreeTextSuggester_Suggest_NullSearcher_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(
            () => FreeTextSuggester.Suggest(null!, "new ", "body", new StandardAnalyser()));
    }

    [Fact]
    public void AnalysingSuggester_Suggest_NullAnalyser_ThrowsArgumentNullException()
    {
        using var searcher = CreateEmptySearcher();

        Assert.Throws<ArgumentNullException>(
            () => AnalysingSuggester.Suggest(searcher, "ap", "body", null!));
    }

    [Fact]
    public void FreeTextSuggester_Suggest_NullAnalyser_ThrowsArgumentNullException()
    {
        using var searcher = CreateEmptySearcher();

        Assert.Throws<ArgumentNullException>(
            () => FreeTextSuggester.Suggest(searcher, "new ", "body", null!));
    }

    [Fact]
    public void AnalysingSuggester_Suggest_EmptyIndex_ReturnsEmpty()
    {
        using var searcher = CreateEmptySearcher();

        var suggestions = AnalysingSuggester.Suggest(searcher, "ap", "body", new StandardAnalyser());

        Assert.Empty(suggestions);
    }

    [Fact]
    public void AnalysingSuggester_Suggest_ContextFilter_EmptyIndex_ReturnsEmpty()
    {
        using var searcher = CreateEmptySearcher();

        var suggestions = AnalysingSuggester.Suggest(
            searcher,
            "ap",
            "body",
            new StandardAnalyser(),
            contextFilter: new TermQuery("category", "fruit"));

        Assert.Empty(suggestions);
    }

    [Fact]
    public void FreeTextSuggester_Suggest_EmptyIndex_ReturnsEmpty()
    {
        using var searcher = CreateEmptySearcher();

        var suggestions = FreeTextSuggester.Suggest(searcher, "new ", "body", new StandardAnalyser());

        Assert.Empty(suggestions);
    }

    [Fact]
    public void FreeTextSuggester_Suggest_NonPositiveTopN_ReturnsEmpty()
    {
        using var searcher = CreateEmptySearcher();

        var suggestions = FreeTextSuggester.Suggest(searcher, "new ", "body", new StandardAnalyser(), topN: 0);

        Assert.Empty(suggestions);
    }
}
