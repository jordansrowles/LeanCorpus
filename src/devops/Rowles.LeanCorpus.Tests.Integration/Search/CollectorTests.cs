using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

/// <summary>
/// Tests for <see cref="IndexSearcher.Count"/> and <see cref="IndexSearcher.Search(Query, ICollector)"/>.
/// </summary>
public sealed class CollectorTests : IDisposable
{
    private readonly string _dir;

    public CollectorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"lc_collector_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    [Fact(DisplayName = "Count: Returns correct count for TermQuery")]
    public void Count_TermQuery_ReturnsCorrectCount()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 100; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", i % 2 == 0 ? "even" : "odd"));
                w.AddDocument(doc);
            }
            w.Commit();
        }

        using var searcher = new IndexSearcher(dir, new IndexSearcherConfig());
        Assert.Equal(50, searcher.Count(new TermQuery("body", "even")));
        Assert.Equal(50, searcher.Count(new TermQuery("body", "odd")));
    }

    [Fact(DisplayName = "Count: MatchAllDocsQuery returns total doc count")]
    public void Count_MatchAllDocsQuery_ReturnsTotalDocCount()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 42; i++)
                w.AddDocument(Doc($"doc {i}"));
            w.Commit();
        }

        using var searcher = new IndexSearcher(dir, new IndexSearcherConfig());
        Assert.Equal(42, searcher.Count(new MatchAllDocsQuery()));
    }

    [Fact(DisplayName = "Count: BooleanQuery returns correct count")]
    public void Count_BooleanQuery_ReturnsCorrectCount()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var d1 = new LeanDocument();
            d1.Add(new TextField("body", "alpha beta"));
            w.AddDocument(d1);

            var d2 = new LeanDocument();
            d2.Add(new TextField("body", "alpha gamma"));
            w.AddDocument(d2);

            var d3 = new LeanDocument();
            d3.Add(new TextField("body", "beta gamma"));
            w.AddDocument(d3);

            w.Commit();
        }

        using var searcher = new IndexSearcher(dir, new IndexSearcherConfig());
        var bq = new BooleanQuery.Builder()
            .Add(new TermQuery("body", "alpha"), Occur.Must)
            .Add(new TermQuery("body", "beta"), Occur.Must)
            .Build();
        Assert.Equal(1, searcher.Count(bq));
    }

    [Fact(DisplayName = "Count: Empty index returns zero")]
    public void Count_EmptyIndex_ReturnsZero()
    {
        var dir = new MMapDirectory(_dir);
        using var w = new IndexWriter(dir, new IndexWriterConfig());
        w.Commit();

        using var searcher = new IndexSearcher(dir, new IndexSearcherConfig());
        Assert.Equal(0, searcher.Count(new TermQuery("body", "nothing")));
        Assert.Equal(0, searcher.Count(new MatchAllDocsQuery()));
    }

    [Fact(DisplayName = "Count: Matches Search TotalHits")]
    public void Count_MatchesSearchTotalHits()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 200; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"document number {i}"));
                doc.Add(new NumericField("val", i));
                w.AddDocument(doc);
            }
            w.Commit();
        }

        using var searcher = new IndexSearcher(dir, new IndexSearcherConfig());

        // Count via Count() should match TotalHits from Search().
        int count = searcher.Count(new TermQuery("body", "document"));
        int totalHits = searcher.Search(new TermQuery("body", "document"), 10).TotalHits;
        Assert.Equal(totalHits, count);

        // Same for range query.
        int rangeCount = searcher.Count(new RangeQuery("val", 0.0, 99.0));
        int rangeHits = searcher.Search(new RangeQuery("val", 0.0, 99.0), 10).TotalHits;
        Assert.Equal(rangeHits, rangeCount);
    }

    [Fact(DisplayName = "Search(ICollector): Custom collector receives all hits")]
    public void Search_Collector_ReceivesAllHits()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 50; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", "match"));
                w.AddDocument(doc);
            }
            w.Commit();
        }

        using var searcher = new IndexSearcher(dir, new IndexSearcherConfig());
        var collector = new TrackingCollector();
        searcher.Search(new TermQuery("body", "match"), collector);
        Assert.Equal(50, collector.TotalHits);
        Assert.Equal(50, collector.DocIds.Count);
        Assert.All(collector.Scores, score => Assert.True(score > 0, $"Expected positive score, got {score}"));
    }

    [Fact(DisplayName = "Search(ICollector): CountCollector matches Count()")]
    public void Search_Collector_CountCollectorMatchesCount()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 75; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", i % 3 == 0 ? "target" : "other"));
                w.AddDocument(doc);
            }
            w.Commit();
        }

        using var searcher = new IndexSearcher(dir, new IndexSearcherConfig());

        // CountCollector is a struct; hold as ICollector so the box is updated in-place.
        ICollector cc = new CountCollector();
        searcher.Search(new TermQuery("body", "target"), cc);
        Assert.Equal(searcher.Count(new TermQuery("body", "target")), ((CountCollector)cc).TotalHits);
    }

    [Fact(DisplayName = "Search(ICollector): BooleanQuery through collector")]
    public void Search_Collector_BooleanQuery()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 20; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"item {i}"));
                doc.Add(new NumericField("price", i));
                w.AddDocument(doc);
            }
            w.Commit();
        }

        using var searcher = new IndexSearcher(dir, new IndexSearcherConfig());

        var bq = new BooleanQuery.Builder()
            .Add(new TermQuery("body", "item"), Occur.Must)
            .Add(new RangeQuery("price", 5.0, 15.0), Occur.Must)
            .Build();

        ICollector cc = new CountCollector();
        searcher.Search(bq, cc);
        Assert.Equal(11, ((CountCollector)cc).TotalHits); // docs 5-15 inclusive
    }

    private static LeanDocument Doc(string body)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        return doc;
    }

    /// <summary>Collector that records every doc ID and score for assertions.</summary>
    private sealed class TrackingCollector : ICollector
    {
        public int TotalHits { get; private set; }
        public List<int> DocIds { get; } = [];
        public List<float> Scores { get; } = [];

        public void Collect(int docId, float score)
        {
            TotalHits++;
            DocIds.Add(docId);
            Scores.Add(score);
        }
    }
}
