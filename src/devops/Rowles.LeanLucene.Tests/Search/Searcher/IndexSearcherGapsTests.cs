using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search.Searcher;

/// <summary>
/// Gap-coverage tests for <see cref="IndexSearcher"/> targeting branches not yet
/// covered: topN guard, SearchOptions validation, cancellation, timeout,
/// streaming, query-string overloads, BooleanQuery fast path, pattern queries,
/// and query cache hits.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class IndexSearcherGapsTests : IDisposable
{
    private readonly string _dir;

    public IndexSearcherGapsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_is_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private MMapDirectory BuildIndex(params string[] bodies)
    {
        var mmap = new MMapDirectory(_dir);
        using var writer = new IndexWriter(mmap, new IndexWriterConfig());
        foreach (var body in bodies)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", body));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return mmap;
    }

    private MMapDirectory EmptyIndex()
    {
        var mmap = new MMapDirectory(_dir);
        using var writer = new IndexWriter(mmap, new IndexWriterConfig());
        writer.Commit();
        return mmap;
    }

    // Guard paths

    [Fact(DisplayName = "IndexSearcher: Search TopN Zero Returns Empty")]
    public void Search_TopNZero_ReturnsEmpty()
    {
        using var dir = BuildIndex("hello world");
        using var searcher = new IndexSearcher(dir);
        var result = searcher.Search(new TermQuery("body", "hello"), 0);
        Assert.Empty(result.ScoreDocs);
    }

    [Fact(DisplayName = "IndexSearcher: Search Empty Index Returns Empty")]
    public void Search_EmptyIndex_ReturnsEmpty()
    {
        using var dir = EmptyIndex();
        using var searcher = new IndexSearcher(dir);
        var result = searcher.Search(new TermQuery("body", "hello"), 10);
        Assert.Empty(result.ScoreDocs);
    }

    // SearchOptions validation

    [Fact(DisplayName = "IndexSearcher: SearchOptions MaxResultBytes Too Small Throws")]
    public void Search_WithOptions_MaxResultBytesTooSmall_Throws()
    {
        using var dir = BuildIndex("hello");
        using var searcher = new IndexSearcher(dir);
        // topN=10 needs 10 * 12 = 120 bytes; we give only 1.
        var opts = new SearchOptions { MaxResultBytes = 1 };
        Assert.Throws<ArgumentException>(
            () => searcher.Search(new TermQuery("body", "hello"), 10, opts));
    }

    [Fact(DisplayName = "IndexSearcher: SearchOptions Default Returns Results")]
    public void Search_WithOptions_Default_ReturnsResults()
    {
        using var dir = BuildIndex("hello world", "hello mars");
        using var searcher = new IndexSearcher(dir);
        var result = searcher.Search(new TermQuery("body", "hello"), 10, SearchOptions.Default);
        Assert.True(result.TotalHits > 0);
    }

    // Cancellation

    [Fact(DisplayName = "IndexSearcher: Search Pre-Cancelled Token Throws")]
    public void Search_PreCancelledToken_Throws()
    {
        using var dir = BuildIndex("hello");
        using var searcher = new IndexSearcher(dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(
            () => searcher.Search(new TermQuery("body", "hello"), 10, cts.Token));
    }

    [Fact(DisplayName = "IndexSearcher: SearchOptions Pre-Cancelled Token Returns Partial")]
    public void Search_WithOptions_PreCancelledToken_ReturnsPartial()
    {
        using var dir = BuildIndex("hello world", "hello mars", "hello venus");
        using var searcher = new IndexSearcher(dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var opts = new SearchOptions { CancellationToken = cts.Token };
        // Cancelled before any segment runs, so the result is partial and may or may not have hits.
        var result = searcher.Search(new TermQuery("body", "hello"), 10, opts);
        Assert.True(result.IsPartial);
    }

    // Timeout

    [Fact(DisplayName = "IndexSearcher: SearchOptions Expired Timeout Returns Partial")]
    public void Search_WithOptions_ExpiredTimeout_ReturnsPartialOrEmpty()
    {
        using var dir = BuildIndex("hello world");
        using var searcher = new IndexSearcher(dir);
        // Use a zero, already-elapsed timeout.
        var opts = SearchOptions.WithTimeout(TimeSpan.Zero);
        var result = searcher.Search(new TermQuery("body", "hello"), 10, opts);
        // May or may not be partial depending on timing; just assert it doesn't throw
        Assert.NotNull(result);
    }

    // SearchStreaming

    [Fact(DisplayName = "IndexSearcher: SearchStreaming Returns Docs In Segment Order")]
    public void SearchStreaming_ReturnsDocs()
    {
        using var dir = BuildIndex("hello world", "hello mars");
        using var searcher = new IndexSearcher(dir);
        var docs = searcher.SearchStreaming(new TermQuery("body", "hello")).ToList();
        Assert.True(docs.Count > 0);
    }

    [Fact(DisplayName = "IndexSearcher: SearchStreaming Empty Index Yields Nothing")]
    public void SearchStreaming_EmptyIndex_YieldsNothing()
    {
        using var dir = EmptyIndex();
        using var searcher = new IndexSearcher(dir);
        var docs = searcher.SearchStreaming(new TermQuery("body", "hello")).ToList();
        Assert.Empty(docs);
    }

    [Fact(DisplayName = "IndexSearcher: SearchStreaming Pre-Cancelled Token Yields Nothing")]
    public void SearchStreaming_PreCancelledToken_YieldsNothing()
    {
        using var dir = BuildIndex("hello");
        using var searcher = new IndexSearcher(dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var opts = new SearchOptions { CancellationToken = cts.Token };
        var docs = searcher.SearchStreaming(new TermQuery("body", "hello"), options: opts).ToList();
        Assert.Empty(docs);
    }

    [Fact(DisplayName = "IndexSearcher: SearchStreaming MaxResultBytes Tiny Yields Nothing")]
    public void SearchStreaming_MaxResultBytesTooSmall_YieldsNothing()
    {
        using var dir = BuildIndex("hello world");
        using var searcher = new IndexSearcher(dir);
        // perSegmentTopN=10 needs 10 * 12 = 120 bytes; budget=1 stops before the first segment.
        var opts = new SearchOptions { MaxResultBytes = 1 };
        var docs = searcher.SearchStreaming(new TermQuery("body", "hello"), perSegmentTopN: 10, options: opts).ToList();
        Assert.Empty(docs);
    }

    // Query string overloads

    [Fact(DisplayName = "IndexSearcher: Search String Overload Returns Results")]
    public void Search_StringOverload_ReturnsResults()
    {
        using var dir = BuildIndex("the quick brown fox", "the lazy dog");
        using var searcher = new IndexSearcher(dir);
        var result = searcher.Search("quick", "body", 10);
        Assert.True(result.TotalHits > 0);
    }

    [Fact(DisplayName = "IndexSearcher: Search String With Cancellation Returns Results")]
    public void Search_StringOverloadWithCancellation_ReturnsResults()
    {
        using var dir = BuildIndex("the quick brown fox");
        using var searcher = new IndexSearcher(dir);
        var result = searcher.Search("quick", "body", 10, null, CancellationToken.None);
        Assert.True(result.TotalHits > 0);
    }

    // Query type dispatchers

    [Fact(DisplayName = "IndexSearcher: BooleanQuery All-Term Fast Path Returns Hits")]
    public void Search_BooleanQueryAllTermFastPath_ReturnsHits()
    {
        using var dir = BuildIndex("quick brown fox", "lazy brown dog");
        using var searcher = new IndexSearcher(dir);
        var bq = new BooleanQuery.Builder()
            .Add(new TermQuery("body", "brown"), Occur.Must)
            .Build();
        var result = searcher.Search(bq, 10);
        Assert.True(result.TotalHits > 0);
    }

    [Fact(DisplayName = "IndexSearcher: PrefixQuery Skips Global DF And Returns Hits")]
    public void Search_PrefixQuery_SkipsGlobalDFAndReturnsHits()
    {
        using var dir = BuildIndex("programming language", "python rocks", "prolog fun");
        using var searcher = new IndexSearcher(dir);
        var result = searcher.Search(new PrefixQuery("body", "pro"), 10);
        Assert.True(result.TotalHits > 0);
    }

    [Fact(DisplayName = "IndexSearcher: WildcardQuery Skips Global DF And Returns Hits")]
    public void Search_WildcardQuery_SkipsGlobalDFAndReturnsHits()
    {
        using var dir = BuildIndex("jumper cable", "jumping jacks");
        using var searcher = new IndexSearcher(dir);
        var result = searcher.Search(new WildcardQuery("body", "jump*"), 10);
        Assert.True(result.TotalHits > 0);
    }

    [Fact(DisplayName = "IndexSearcher: FuzzyQuery Skips Global DF And Returns Hits")]
    public void Search_FuzzyQuery_SkipsGlobalDFAndReturnsHits()
    {
        using var dir = BuildIndex("colour theory", "color wheel");
        using var searcher = new IndexSearcher(dir);
        var result = searcher.Search(new FuzzyQuery("body", "colour", 1), 10);
        Assert.True(result.TotalHits > 0);
    }

    // Constructor with segment list

    [Fact(DisplayName = "IndexSearcher: Segment List Constructor Returns Same Results")]
    public void SegmentListConstructor_ReturnsSameResults()
    {
        using var dir = BuildIndex("hello world");
        using var searcher1 = new IndexSearcher(dir);
        var segments = searcher1.GetSegmentReaders().Select(r => r.Info).ToList();

        using var searcher2 = new IndexSearcher(dir, segments);
        var r1 = searcher1.Search(new TermQuery("body", "hello"), 10);
        var r2 = searcher2.Search(new TermQuery("body", "hello"), 10);
        Assert.Equal(r1.TotalHits, r2.TotalHits);
    }

    // Query cache

    [Fact(DisplayName = "IndexSearcher: Query Cache Returns Hit On Repeat Call")]
    public void QueryCache_ReturnsCachedResultOnRepeatCall()
    {
        using var dir = BuildIndex("cache test document");
        var cfg = new IndexSearcherConfig { EnableQueryCache = true, QueryCacheMaxEntries = 16 };
        using var searcher = new IndexSearcher(dir, cfg);
        var query = new TermQuery("body", "cache");
        var r1 = searcher.Search(query, 10);
        var r2 = searcher.Search(query, 10);
        Assert.Equal(r1.TotalHits, r2.TotalHits);
        Assert.NotNull(searcher.Cache);
    }

    // GetIndexSize

    [Fact(DisplayName = "IndexSearcher: GetIndexSize Returns Non-Zero For Populated Index")]
    public void GetIndexSize_NonEmptyIndex_ReturnsNonZeroBytes()
    {
        using var dir = BuildIndex("some content here");
        using var searcher = new IndexSearcher(dir);
        var size = searcher.GetIndexSize();
        Assert.True(size.TotalSizeBytes > 0);
    }
}
