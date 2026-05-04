using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Search.Scoring;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search;

public sealed class QueryCacheTests : IDisposable
{
    private readonly string _dir;

    public QueryCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ll_qcache_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Cache_ReturnsCachedResult_OnSecondSearch()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("hello world"));
            w.Commit();
        }

        var config = new IndexSearcherConfig { EnableQueryCache = true };
        using var searcher = new IndexSearcher(dir, config);

        var q = new TermQuery("body", "hello");
        var first = searcher.Search(q, 10);
        var second = searcher.Search(q, 10);

        Assert.Equal(first.TotalHits, second.TotalHits);
        Assert.NotNull(searcher.Cache);
        Assert.Equal(1, searcher.Cache.Hits);
        Assert.Equal(1, searcher.Cache.Misses);
    }

    [Fact]
    public void Cache_Disabled_ByDefault()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("test"));
            w.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        Assert.Null(searcher.Cache);
    }

    [Fact]
    public void Cache_DifferentQueries_DifferentEntries()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("alpha beta"));
            w.Commit();
        }

        var config = new IndexSearcherConfig { EnableQueryCache = true };
        using var searcher = new IndexSearcher(dir, config);

        searcher.Search(new TermQuery("body", "alpha"), 10);
        searcher.Search(new TermQuery("body", "beta"), 10);

        Assert.Equal(2, searcher.Cache!.Count);
        Assert.Equal(0, searcher.Cache.Hits);
        Assert.Equal(2, searcher.Cache.Misses);
    }

    [Fact]
    public void Cache_DifferentTopN_DifferentEntries()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("test"));
            w.Commit();
        }

        var config = new IndexSearcherConfig { EnableQueryCache = true };
        using var searcher = new IndexSearcher(dir, config);

        var q = new TermQuery("body", "test");
        searcher.Search(q, 5);
        searcher.Search(q, 10);

        Assert.Equal(2, searcher.Cache!.Count);
    }

    [Fact]
    public void Cache_Invalidation_ClearsStaleEntries()
    {
        var cache = new QueryCache(100);
        var q = new TermQuery("f", "t");

        cache.Put(q, 10, TopDocs.Empty);
        Assert.NotNull(cache.TryGet(q, 10));

        cache.Invalidate();
        Assert.Null(cache.TryGet(q, 10));
    }

    [Fact]
    public void Cache_LRU_EvictsOldestEntry()
    {
        var cache = new QueryCache(2);
        var q1 = new TermQuery("f", "a");
        var q2 = new TermQuery("f", "b");
        var q3 = new TermQuery("f", "c");

        cache.Put(q1, 10, TopDocs.Empty);
        cache.Put(q2, 10, TopDocs.Empty);
        cache.Put(q3, 10, TopDocs.Empty); // should evict q1

        Assert.Null(cache.TryGet(q1, 10));
        Assert.NotNull(cache.TryGet(q2, 10));
        Assert.NotNull(cache.TryGet(q3, 10));
    }

    [Fact]
    public void QueryEquality_TermQuery()
    {
        var a = new TermQuery("body", "hello");
        var b = new TermQuery("body", "hello");
        var c = new TermQuery("body", "world");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void QueryEquality_BooleanQuery()
    {
        var a = new BooleanQuery();
        a.Add(new TermQuery("f", "x"), Occur.Must);
        a.Add(new TermQuery("f", "y"), Occur.Should);

        var b = new BooleanQuery();
        b.Add(new TermQuery("f", "x"), Occur.Must);
        b.Add(new TermQuery("f", "y"), Occur.Should);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void QueryEquality_PhraseQuery()
    {
        var a = new PhraseQuery("body", "hello", "world");
        var b = new PhraseQuery("body", "hello", "world");
        var c = new PhraseQuery("body", 1, "hello", "world");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c); // different slop
    }

    [Fact]
    public void QueryEquality_Boost_AffectsEquality()
    {
        var a = new TermQuery("f", "t") { Boost = 1.0f };
        var b = new TermQuery("f", "t") { Boost = 2.0f };

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Cache_QueryMutationAfterPut_DoesNotReturnStaleEntry()
    {
        var cache = new QueryCache(10);
        var query = new TermQuery("body", "hello");
        var cached = new TopDocs(1, [new ScoreDoc(123, 1.0f)]);

        cache.Put(query, 10, cached);
        query.Boost = 2.0f;

        Assert.Null(cache.TryGet(query, 10));
    }

    private static LeanDocument Doc(string body)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        return doc;
    }
}
