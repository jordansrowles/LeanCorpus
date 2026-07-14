using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

/// <summary>
/// Contains unit tests for Search Options.
/// </summary>
[Trait("Category", "Search")]
public sealed class SearchOptionsTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public SearchOptionsTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    private MMapDirectory IndexSampleDocs(string suffix, int count)
    {
        var path = SubDir(suffix);
        var dir = new MMapDirectory(path);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < count; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"term{i % 10} payload"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }
        return dir;
    }

    /// <summary>
    /// Verifies the Default: Has No Limits scenario.
    /// </summary>
    [Fact(DisplayName = "Default: Has No Limits")]
    public void Default_HasNoLimits()
    {
        var options = SearchOptions.Default;

        Assert.Equal(long.MaxValue, options.MaxResultBytes);
        Assert.False(options.StreamResults);
        Assert.Null(options.Timeout);
        Assert.Equal(CancellationToken.None, options.CancellationToken);
    }

    /// <summary>
    /// Verifies the With Budget: Sets Max Result Bytes scenario.
    /// </summary>
    [Fact(DisplayName = "With Budget: Sets Max Result Bytes")]
    public void WithBudget_SetsMaxResultBytes()
    {
        var options = SearchOptions.WithBudget(1024);

        Assert.Equal(1024, options.MaxResultBytes);
    }

    /// <summary>
    /// Verifies the With Timeout: Sets Timeout scenario.
    /// </summary>
    [Fact(DisplayName = "With Timeout: Sets Timeout")]
    public void WithTimeout_SetsTimeout()
    {
        var timeout = TimeSpan.FromSeconds(5);

        var options = SearchOptions.WithTimeout(timeout);

        Assert.Equal(timeout, options.Timeout);
    }

    /// <summary>
    /// Verifies the With Budget And Timeout: Sets Both scenario.
    /// </summary>
    [Fact(DisplayName = "With Budget And Timeout: Sets Both")]
    public void WithBudgetAndTimeout_SetsBoth()
    {
        var options = SearchOptions.WithBudgetAndTimeout(2048, TimeSpan.FromSeconds(2));

        Assert.Equal(2048, options.MaxResultBytes);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Timeout);
    }

    /// <summary>
    /// Verifies the Init: Allows Custom Combination scenario.
    /// </summary>
    [Fact(DisplayName = "Init: Allows Custom Combination")]
    public void Init_AllowsCustomCombination()
    {
        var options = new SearchOptions
        {
            MaxResultBytes = 1024,
            StreamResults = true,
            Timeout = TimeSpan.FromSeconds(1),
        };

        Assert.Equal(1024, options.MaxResultBytes);
        Assert.True(options.StreamResults);
        Assert.Equal(TimeSpan.FromSeconds(1), options.Timeout);
    }

    /// <summary>
    /// Verifies the Default: Is Singleton scenario.
    /// </summary>
    [Fact(DisplayName = "Default: Is Singleton")]
    public void Default_IsSingleton()
    {
        var first = SearchOptions.Default;
        var second = SearchOptions.Default;

        Assert.True(ReferenceEquals(first, second));
    }

    /// <summary>
    /// Verifies the Search: With Unbounded Options Matches Plain Search scenario.
    /// </summary>
    [Fact(DisplayName = "Search: With Unbounded Options Matches Plain Search")]
    public void Search_WithUnboundedOptions_MatchesPlainSearch()
    {
        var dir = IndexSampleDocs("opts_match", 50);
        using var searcher = new IndexSearcher(dir);

        var query = new TermQuery("body", "term5");
        var plain = searcher.Search(query, topN: 10);
        var bounded = searcher.Search(query, topN: 10, SearchOptions.Default);

        Assert.False(bounded.IsPartial);
        Assert.Equal(plain.TotalHits, bounded.TotalHits);
        Assert.Equal(plain.ScoreDocs.Length, bounded.ScoreDocs.Length);
    }

    /// <summary>
    /// Verifies the Search: With Impossible Budget Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Search: With Impossible Budget Throws")]
    public void Search_WithImpossibleBudget_Throws()
    {
        var dir = IndexSampleDocs("opts_budget", 5);
        using var searcher = new IndexSearcher(dir);

        var query = new TermQuery("body", "term1");
        var opts = SearchOptions.WithBudget(1);

        Assert.Throws<ArgumentException>(() => searcher.Search(query, topN: 100, opts));
    }

    /// <summary>
    /// Verifies the Search: With Budget Large Enough For Top N Does Not Stop After Many Hits scenario.
    /// </summary>
    [Fact(DisplayName = "Search: With Budget Large Enough For Top N Does Not Stop After Many Hits")]
    public void Search_WithBudgetLargeEnoughForTopN_DoesNotStopAfterManyHits()
    {
        var dir = IndexSampleDocs("opts_budget_many_hits", 200);
        using var searcher = new IndexSearcher(dir);

        var query = new TermQuery("body", "payload");
        var opts = SearchOptions.WithBudget(ScoreDoc.EstimatedBytes);

        var result = searcher.Search(query, topN: 1, opts);

        Assert.False(result.IsPartial);
        Assert.Equal(200, result.TotalHits);
        Assert.Single(result.ScoreDocs);
    }

    /// <summary>
    /// Verifies the Search: With Cancelled Token Returns Partial scenario.
    /// </summary>
    [Fact(DisplayName = "Search: With Cancelled Token Returns Partial")]
    public void Search_WithCancelledToken_ReturnsPartial()
    {
        var dir = IndexSampleDocs("opts_cancel", 20);
        using var searcher = new IndexSearcher(dir);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var query = new TermQuery("body", "term1");
        var opts = new SearchOptions { CancellationToken = cts.Token };

        var result = searcher.Search(query, topN: 10, opts);

        Assert.True(result.IsPartial);
    }

    /// <summary>
    /// Verifies the Search Streaming: Yields Results scenario.
    /// </summary>
    [Fact(DisplayName = "Search Streaming: Yields Results")]
    public void SearchStreaming_YieldsResults()
    {
        var dir = IndexSampleDocs("opts_stream", 30);
        using var searcher = new IndexSearcher(dir);

        var query = new TermQuery("body", "term1");
        var emitted = searcher.SearchStreaming(query, perSegmentTopN: 64).ToList();

        Assert.NotEmpty(emitted);
    }

    /// <summary>
    /// Verifies the Search Streaming: With Cancelled Token Yields Nothing scenario.
    /// </summary>
    [Fact(DisplayName = "Search Streaming: With Cancelled Token Yields Nothing")]
    public void SearchStreaming_WithCancelledToken_YieldsNothing()
    {
        var dir = IndexSampleDocs("opts_stream_cancel", 30);
        using var searcher = new IndexSearcher(dir);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var query = new TermQuery("body", "term1");

        var cancelled = searcher.SearchStreaming(
            query,
            perSegmentTopN: 64,
            new SearchOptions { CancellationToken = cts.Token }).ToList();

        Assert.Empty(cancelled);
    }

    /// <summary>
    /// Verifies the Top Docs: Is Partial Defaults To False scenario.
    /// </summary>
    [Fact(DisplayName = "Top Docs: Is Partial Defaults To False")]
    public void TopDocs_IsPartial_DefaultsToFalse()
    {
        var docs = new TopDocs(0, []);

        Assert.False(docs.IsPartial);
    }

    /// <summary>
    /// Verifies the Top Docs: With Is Partial True Preserves Flag scenario.
    /// </summary>
    [Fact(DisplayName = "Top Docs: With Is Partial True Preserves Flag")]
    public void TopDocs_WithIsPartialTrue_PreservesFlag()
    {
        var docs = new TopDocs(0, [], isPartial: true);

        Assert.True(docs.IsPartial);
    }

    /// <summary>
    /// Verifies the Search(IEnumerable): StreamResults=true yields per-segment without global heap scenario.
    /// </summary>
    [Fact(DisplayName = "Search IEnumerable: StreamResults True Yields Per Segment")]
    public void Search_IEnumerable_StreamResults_True_YieldsResults()
    {
        var dir = IndexSampleDocs("ienum_stream", 30);
        using var searcher = new IndexSearcher(dir);

        var query = new TermQuery("body", "term1");
        var opts = new SearchOptions { StreamResults = true };
        var emitted = searcher.Search(query, opts).ToList();

        Assert.NotEmpty(emitted);
        // All scores should be non-negative and doc IDs non-negative.
        Assert.All(emitted, sd => Assert.True(sd.DocId >= 0));
        Assert.All(emitted, sd => Assert.True(sd.Score >= 0));
    }

    /// <summary>
    /// Verifies the Search(IEnumerable): StreamResults=false materialises matching plain Search scenario.
    /// </summary>
    [Fact(DisplayName = "Search IEnumerable: StreamResults False Matches Plain Search")]
    public void Search_IEnumerable_StreamResults_False_MatchesPlainSearch()
    {
        var dir = IndexSampleDocs("ienum_mat", 50);
        using var searcher = new IndexSearcher(dir);

        var query = new TermQuery("body", "term5");
        var plain = searcher.Search(query, topN: 10);
        var opts = new SearchOptions { StreamResults = false };
        var emitted = searcher.Search(query, opts).ToList();

        Assert.Equal(plain.ScoreDocs.Length, emitted.Count);
        for (int i = 0; i < emitted.Count; i++)
        {
            Assert.Equal(plain.ScoreDocs[i].DocId, emitted[i].DocId);
            Assert.Equal(plain.ScoreDocs[i].Score, emitted[i].Score);
        }
    }

    /// <summary>
    /// Verifies the Search(IEnumerable): empty index yields nothing scenario.
    /// </summary>
    [Fact(DisplayName = "Search IEnumerable: Empty Index Yields Nothing")]
    public void Search_IEnumerable_EmptyIndex_YieldsNothing()
    {
        var dir = new MMapDirectory(SubDir("ienum_empty"));
        using var searcher = new IndexSearcher(dir);

        var query = new TermQuery("body", "hello");
        var opts = new SearchOptions { StreamResults = true };
        var emitted = searcher.Search(query, opts).ToList();

        Assert.Empty(emitted);
    }

    /// <summary>
    /// Verifies the Search(IEnumerable): pre-cancelled token yields nothing scenario.
    /// </summary>
    [Fact(DisplayName = "Search IEnumerable: Pre-Cancelled Token Yields Nothing")]
    public void Search_IEnumerable_PreCancelled_YieldsNothing()
    {
        var dir = IndexSampleDocs("ienum_cancel", 30);
        using var searcher = new IndexSearcher(dir);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var query = new TermQuery("body", "term1");
        var opts = new SearchOptions { StreamResults = true, CancellationToken = cts.Token };
        var emitted = searcher.Search(query, opts).ToList();

        Assert.Empty(emitted);
    }

    /// <summary>
    /// Verifies the SearchAsync: yields results in segment order scenario.
    /// </summary>
    [Fact(DisplayName = "Search Async: Yields Results")]
    public async Task SearchAsync_YieldsResults()
    {
        var dir = IndexSampleDocs("async_stream", 30);
        using var searcher = new IndexSearcher(dir);

        var query = new TermQuery("body", "term1");
        var opts = new SearchOptions { StreamResults = true };

        var emitted = new List<ScoreDoc>();
        await foreach (var sd in searcher.SearchAsync(query, opts))
            emitted.Add(sd);

        Assert.NotEmpty(emitted);
        Assert.All(emitted, sd => Assert.True(sd.DocId >= 0));
        Assert.All(emitted, sd => Assert.True(sd.Score >= 0));
    }

    /// <summary>
    /// Verifies the SearchAsync: pre-cancelled token throws scenario.
    /// </summary>
    [Fact(DisplayName = "Search Async: Pre-Cancelled Token Throws")]
    public async Task SearchAsync_PreCancelled_Throws()
    {
        var dir = IndexSampleDocs("async_cancel", 30);
        using var searcher = new IndexSearcher(dir);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var query = new TermQuery("body", "term1");
        var opts = new SearchOptions { StreamResults = true };

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var sd in searcher.SearchAsync(query, opts, cts.Token))
            {
                // Should not reach here.
            }
        });
    }

    /// <summary>
    /// Verifies the SearchAsync: empty index yields nothing scenario.
    /// </summary>
    [Fact(DisplayName = "Search Async: Empty Index Yields Nothing")]
    public async Task SearchAsync_EmptyIndex_YieldsNothing()
    {
        var dir = new MMapDirectory(SubDir("async_empty"));
        using var searcher = new IndexSearcher(dir);

        var query = new TermQuery("body", "hello");
        var opts = new SearchOptions { StreamResults = true };

        var emitted = new List<ScoreDoc>();
        await foreach (var sd in searcher.SearchAsync(query, opts))
            emitted.Add(sd);

        Assert.Empty(emitted);
    }
}
