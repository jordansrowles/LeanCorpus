using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Search.Searcher;

/// <summary>
/// Gap-coverage tests for <see cref="IndexSearcher"/> targeting branches not yet
/// covered: Metrics property, span queries (Near, Or, Not), CollectChildDocsIntoBitArray
/// via BlockJoin with a non-TermQuery child, and ExecuteVectorQuery.
/// </summary>
[Trait("Category", "Search")]
public sealed class IndexSearcherAdvancedTests : IDisposable
{
    private readonly string _dir;

    public IndexSearcherAdvancedTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_isa_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_dir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    // Metrics property

    [Fact(DisplayName = "IndexSearcher: Metrics Returns Default NullMetricsCollector")]
    public void Metrics_DefaultConfig_ReturnsNullMetricsCollector()
    {
        var dir = SubDir("metrics_default");
        using var mmap = BuildIndex(dir, "hello world");
        using var searcher = new IndexSearcher(mmap);

        var metrics = searcher.Metrics;

        Assert.IsType<NullMetricsCollector>(metrics);
    }

    [Fact(DisplayName = "IndexSearcher: Metrics Returns Custom Collector From Config")]
    public void Metrics_CustomConfig_ReturnsSameInstance()
    {
        var dir = SubDir("metrics_custom");
        using var mmap = BuildIndex(dir, "hello world");
        var custom = new DefaultMetricsCollector();
        var config = new IndexSearcherConfig { Metrics = custom };
        using var searcher = new IndexSearcher(mmap, config);

        Assert.Same(custom, searcher.Metrics);
    }

    // Span queries

    [Fact(DisplayName = "IndexSearcher: SpanNearQuery In-Order Match Returns Hit")]
    public void Search_SpanNearQuery_InOrderMatch_ReturnsHit()
    {
        var dir = SubDir("span_near");
        using var mmap = BuildIndex(dir, "quick brown fox", "brown quick fox");
        using var searcher = new IndexSearcher(mmap);

        var query = new SpanNearQuery(
            [new SpanTermQuery("body", "quick"), new SpanTermQuery("body", "brown")],
            slop: 0,
            inOrder: true);

        var result = searcher.Search(query, 10);

        Assert.True(result.TotalHits > 0);
    }

    [Fact(DisplayName = "IndexSearcher: SpanNearQuery Out-Of-Order Slop Match Returns Hit")]
    public void Search_SpanNearQuery_OutOfOrderSlop_ReturnsHit()
    {
        var dir = SubDir("span_near_slop");
        using var mmap = BuildIndex(dir, "quick brown fox");
        using var searcher = new IndexSearcher(mmap);

        var query = new SpanNearQuery(
            [new SpanTermQuery("body", "brown"), new SpanTermQuery("body", "quick")],
            slop: 2,
            inOrder: false);

        var result = searcher.Search(query, 10);

        Assert.True(result.TotalHits > 0);
    }

    [Fact(DisplayName = "IndexSearcher: SpanNearQuery No Match Returns Empty")]
    public void Search_SpanNearQuery_NoMatch_ReturnsEmpty()
    {
        var dir = SubDir("span_near_empty");
        using var mmap = BuildIndex(dir, "quick brown fox");
        using var searcher = new IndexSearcher(mmap);

        var query = new SpanNearQuery(
            [new SpanTermQuery("body", "quick"), new SpanTermQuery("body", "absent")],
            slop: 0,
            inOrder: true);

        var result = searcher.Search(query, 10);

        Assert.Equal(0, result.TotalHits);
    }

    [Fact(DisplayName = "IndexSearcher: SpanOrQuery Returns Docs Matching Any Clause")]
    public void Search_SpanOrQuery_ReturnsDocsMatchingAnyClause()
    {
        var dir = SubDir("span_or");
        using var mmap = BuildIndex(dir, "apple pie", "cherry cake", "banana split");
        using var searcher = new IndexSearcher(mmap);

        var query = new SpanOrQuery(
            new SpanTermQuery("body", "apple"),
            new SpanTermQuery("body", "cherry"));

        var result = searcher.Search(query, 10);

        Assert.Equal(2, result.TotalHits);
    }

    [Fact(DisplayName = "IndexSearcher: SpanOrQuery With No Matching Clauses Returns Empty")]
    public void Search_SpanOrQuery_NoMatch_ReturnsEmpty()
    {
        var dir = SubDir("span_or_empty");
        using var mmap = BuildIndex(dir, "apple pie");
        using var searcher = new IndexSearcher(mmap);

        var query = new SpanOrQuery(
            new SpanTermQuery("body", "cherry"),
            new SpanTermQuery("body", "mango"));

        var result = searcher.Search(query, 10);

        Assert.Equal(0, result.TotalHits);
    }

    [Fact(DisplayName = "IndexSearcher: SpanNotQuery Excludes Docs With Excluded Term")]
    public void Search_SpanNotQuery_ExcludesDocsWithExcludedTerm()
    {
        var dir = SubDir("span_not");
        using var mmap = BuildIndex(dir,
            "apple pie is great",
            "apple and cherry cake");
        using var searcher = new IndexSearcher(mmap);

        var include = new SpanTermQuery("body", "apple");
        var exclude = new SpanTermQuery("body", "cherry");
        var query = new SpanNotQuery(include, exclude);

        var result = searcher.Search(query, 10);

        // Only "apple pie is great" should match (apple present, cherry absent)
        Assert.Equal(1, result.TotalHits);
    }

    [Fact(DisplayName = "IndexSearcher: SpanNotQuery With No Include Matches Returns Empty")]
    public void Search_SpanNotQuery_NoIncludeMatches_ReturnsEmpty()
    {
        var dir = SubDir("span_not_empty");
        using var mmap = BuildIndex(dir, "cherry cake");
        using var searcher = new IndexSearcher(mmap);

        var query = new SpanNotQuery(
            new SpanTermQuery("body", "apple"),
            new SpanTermQuery("body", "cherry"));

        var result = searcher.Search(query, 10);

        Assert.Equal(0, result.TotalHits);
    }

    // CollectChildDocsIntoBitArray via BlockJoinQuery with non-TermQuery child

    [Fact(DisplayName = "IndexSearcher: BlockJoinQuery With BooleanQuery Child Uses BitArray Path")]
    public void Search_BlockJoinQuery_BooleanQueryChild_CollectsViaChildBitArray()
    {
        var dir = SubDir("bjq_bitarray");
        using var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
        {
            for (int i = 0; i < 3; i++)
            {
                var block = new List<LeanDocument>();

                var child = new LeanDocument();
                child.Add(new TextField("body", $"child note about topic {i}"));
                child.Add(new StringField("type", "child"));
                block.Add(child);

                var parent = new LeanDocument();
                parent.Add(new TextField("title", $"parent post {i}"));
                parent.Add(new StringField("type", "parent"));
                block.Add(parent);

                writer.AddDocumentBlock(block);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);

        // BooleanQuery child forces the BitArray path (not the fast TermQuery path)
        var childQuery = new BooleanQuery.Builder()
            .Add(new TermQuery("body", "child"), Occur.Must)
            .Add(new TermQuery("body", "note"), Occur.Must)
            .Build();
        var bjq = new BlockJoinQuery(childQuery);

        var result = searcher.Search(bjq, 10);

        Assert.True(result.TotalHits > 0);
    }

    // ExecuteVectorQuery

    [Fact(DisplayName = "IndexSearcher: VectorQuery Returns Nearest Neighbours")]
    public void Search_VectorQuery_ReturnsNearestNeighbours()
    {
        var dir = SubDir("vec_query");
        using var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig { BuildHnswOnFlush = true }))
        {
            for (int i = 0; i < 5; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new VectorField("embedding", new ReadOnlyMemory<float>([i + 1f, 0f, 0f])));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var query = new VectorQuery("embedding", [1f, 0f, 0f], topK: 3);

        var result = searcher.Search(query, 10);

        Assert.True(result.TotalHits > 0);
    }

    [Fact(DisplayName = "IndexSearcher: VectorQuery On Empty Index Returns Empty")]
    public void Search_VectorQuery_EmptyIndex_ReturnsEmpty()
    {
        var dir = SubDir("vec_query_empty");
        using var mmap = new MMapDirectory(dir);
        using (var writer = new IndexWriter(mmap, new IndexWriterConfig()))
            writer.Commit();
        using var searcher = new IndexSearcher(mmap);

        var query = new VectorQuery("embedding", [1f, 0f, 0f], topK: 3);
        var result = searcher.Search(query, 10);

        Assert.Equal(0, result.TotalHits);
    }

    // Helpers

    private static MMapDirectory BuildIndex(string dir, params string[] bodies)
    {
        var mmap = new MMapDirectory(dir);
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
}
