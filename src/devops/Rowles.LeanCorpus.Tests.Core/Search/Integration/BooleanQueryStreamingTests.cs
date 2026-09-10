using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Contains unit tests for Boolean Query Streaming.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class BooleanQueryStreamingTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BooleanQueryStreamingTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    private static BooleanQuery BuildBooleanQuery(params (Query query, Occur occur)[] clauses)
    {
        var builder = new BooleanQuery.Builder();
        foreach (var (query, occur) in clauses)
            builder.Add(query, occur);

        return builder.Build();
    }

    /// <summary>
    /// Verifies the Must: Single Clause Returns Matching Docs scenario.
    /// </summary>
    [Fact(DisplayName = "Must: Single Clause Returns Matching Docs")]
    public void Must_SingleClause_ReturnsMatchingDocs()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_single_must"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[] { "alpha beta", "gamma delta", "alpha gamma" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "alpha"), Occur.Must));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Must: Three Terms Intersects All scenario.
    /// </summary>
    [Fact(DisplayName = "Must: Three Terms Intersects All")]
    public void Must_ThreeTerms_IntersectsAll()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_three_must"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[]
        {
            "red green blue",
            "red green yellow",
            "red blue purple",
            "green blue orange",
            "red green blue bright"
        };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "red"), Occur.Must),
            (new TermQuery("body", "green"), Occur.Must),
            (new TermQuery("body", "blue"), Occur.Must));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the fast all-term Boolean path honours the parallel-search
    /// configuration and preserves exact totals while merging bounded candidates.
    /// </summary>
    [Fact(DisplayName = "Must: Multi-Segment Four Terms Preserve Exact Total Hits Beyond Top N")]
    public void Must_MultiSegmentFourTerms_PreservesExactTotalHitsBeyondTopN()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_multisegment_total_hits"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = 1_000,
            MergePolicy = NoMergePolicy.Instance,
        });

        AddDocuments(writer, count: 130, matching: true);
        writer.Commit();
        AddDocuments(writer, count: 27, matching: true);
        AddDocuments(writer, count: 10, matching: false);
        writer.Commit();

        var query = BuildBooleanQuery(
            (new TermQuery("body", "president"), Occur.Must),
            (new TermQuery("body", "company"), Occur.Must),
            (new TermQuery("body", "reported"), Occur.Must),
            (new TermQuery("body", "financial"), Occur.Must));

        using var serialSearcher = new IndexSearcher(dir, new IndexSearcherConfig { ParallelSearch = false });
        using var parallelSearcher = new IndexSearcher(dir, new IndexSearcherConfig { ParallelSearch = true });
        var serial = serialSearcher.Search(query, topN: 10, TestContext.Current.CancellationToken);
        var parallel = parallelSearcher.Search(query, topN: 10, TestContext.Current.CancellationToken);

        Assert.Equal(157, serialSearcher.Count(query));
        Assert.Equal(157, serial.TotalHits);
        Assert.Equal(serial.TotalHits, parallel.TotalHits);
        Assert.True(serial.ScoreDocs.Length <= 10);
        Assert.True(parallel.ScoreDocs.Length <= 10);
        Assert.Equal(serial.ScoreDocs.Select(static hit => hit.DocId), parallel.ScoreDocs.Select(static hit => hit.DocId));
        Assert.Equal(serial.ScoreDocs.Select(static hit => hit.Score), parallel.ScoreDocs.Select(static hit => hit.Score));
        Assert.Equal(Enumerable.Range(0, 10), serial.ScoreDocs.Select(static hit => hit.DocId));
    }

    /// <summary>
    /// Verifies the generic parallel merge preserves complete segment totals and
    /// global top-N candidates for a phrase query that bypasses the Boolean fast path.
    /// </summary>
    [Fact(DisplayName = "Phrase: Multi-Segment Parallel Merge Preserves Exact Total Hits")]
    public void Phrase_MultiSegmentParallelMerge_PreservesExactTotalHits()
    {
        var dir = new MMapDirectory(SubDir("phrase_multisegment_parallel_total_hits"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = 1_000,
            MergePolicy = NoMergePolicy.Instance,
        });

        AddPhraseDocuments(writer, count: 130, matching: true);
        writer.Commit();
        AddPhraseDocuments(writer, count: 27, matching: true);
        AddPhraseDocuments(writer, count: 10, matching: false);
        writer.Commit();

        var query = new PhraseQuery("body", "parallel", "merge");
        using var serialSearcher = new IndexSearcher(dir, new IndexSearcherConfig { ParallelSearch = false });
        using var parallelSearcher = new IndexSearcher(dir, new IndexSearcherConfig { ParallelSearch = true });
        var serial = serialSearcher.Search(query, topN: 10, TestContext.Current.CancellationToken);
        var parallel = parallelSearcher.Search(query, topN: 10, TestContext.Current.CancellationToken);

        Assert.Equal(157, serialSearcher.Count(query));
        Assert.Equal(157, serial.TotalHits);
        Assert.Equal(serial.TotalHits, parallel.TotalHits);
        Assert.True(serial.ScoreDocs.Length <= 10);
        Assert.True(parallel.ScoreDocs.Length <= 10);
        Assert.Equal(serial.ScoreDocs.Select(static hit => hit.DocId), parallel.ScoreDocs.Select(static hit => hit.DocId));
        Assert.Equal(serial.ScoreDocs.Select(static hit => hit.Score), parallel.ScoreDocs.Select(static hit => hit.Score));
    }

    private static void AddDocuments(IndexWriter writer, int count, bool matching)
    {
        for (var i = 0; i < count; i++)
        {
            var document = new LeanDocument();
            document.Add(new TextField(
                "body",
                matching
                    ? "president company reported financial"
                    : "president company reported"));
            writer.AddDocument(document);
        }
    }

    private static void AddPhraseDocuments(IndexWriter writer, int count, bool matching)
    {
        for (var i = 0; i < count; i++)
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", matching ? "parallel merge candidate" : "parallel candidate merge"));
            writer.AddDocument(document);
        }
    }

    /// <summary>
    /// Verifies WAND retains global document IDs and exact totals across segments,
    /// while producing the same bounded results as ordinary Boolean execution.
    /// </summary>
    [Fact(DisplayName = "WAND: Multi-Segment Results Preserve Global IDs And Exact Total Hits")]
    public void Wand_MultiSegment_PreservesGlobalIdsAndExactTotalHits()
    {
        var dir = new MMapDirectory(SubDir("wand_multisegment_global_ids"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = 1_000,
            MergePolicy = NoMergePolicy.Instance,
        });

        AddWandDocuments(writer, count: 130);
        writer.Commit();
        AddWandDocuments(writer, count: 27);
        writer.Commit();

        var query = BuildBooleanQuery(
            (new TermQuery("body", "wand"), Occur.Should),
            (new TermQuery("body", "global"), Occur.Should));
        using var ordinary = new IndexSearcher(dir, new IndexSearcherConfig { EnableBlockMaxWand = false });
        using var wand = new IndexSearcher(dir, new IndexSearcherConfig { EnableBlockMaxWand = true });
        var expected = ordinary.Search(query, topN: 10, TestContext.Current.CancellationToken);
        var actual = wand.Search(query, topN: 10, TestContext.Current.CancellationToken);

        Assert.Equal(157, expected.TotalHits);
        Assert.Equal(expected.TotalHits, actual.TotalHits);
        Assert.Equal(expected.ScoreDocs.Select(static hit => hit.DocId), actual.ScoreDocs.Select(static hit => hit.DocId));
        Assert.Equal(expected.ScoreDocs.Select(static hit => hit.Score), actual.ScoreDocs.Select(static hit => hit.Score));
        Assert.All(actual.ScoreDocs, static hit => Assert.InRange(hit.DocId, 0, 156));
    }

    private static void AddWandDocuments(IndexWriter writer, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "wand global"));
            writer.AddDocument(document);
        }
    }

    /// <summary>
    /// Verifies the Must: No Common Docs Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Must: No Common Docs Returns Empty")]
    public void Must_NoCommonDocs_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_must_disjoint"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "only alpha here"));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "only beta here"));
        writer.AddDocument(doc2);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "alpha"), Occur.Must),
            (new TermQuery("body", "beta"), Occur.Must));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Must: Nonexistent Term Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Must: Nonexistent Term Returns Empty")]
    public void Must_NonexistentTerm_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_must_missing"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "some content"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "some"), Occur.Must),
            (new TermQuery("body", "nonexistent"), Occur.Must));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Should: Single Clause Returns Matching Docs scenario.
    /// </summary>
    [Fact(DisplayName = "Should: Single Clause Returns Matching Docs")]
    public void Should_SingleClause_ReturnsMatchingDocs()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_single_should"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[] { "alpha beta", "gamma delta", "alpha gamma" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "alpha"), Occur.Should));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Should: Multiple Terms Score Sums Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Should: Multiple Terms Score Sums Correctly")]
    public void Should_MultipleTerms_ScoreSumsCorrectly()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_should_score"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Doc 0 matches both Should terms → higher score
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "alpha beta"));
        writer.AddDocument(doc1);

        // Doc 1 matches only one term
        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "alpha only"));
        writer.AddDocument(doc2);

        // Doc 2 matches only the other term
        var doc3 = new LeanDocument();
        doc3.Add(new TextField("body", "beta only"));
        writer.AddDocument(doc3);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "alpha"), Occur.Should),
            (new TermQuery("body", "beta"), Occur.Should));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(3, results.TotalHits);
        // Doc matching both terms should rank highest
        Assert.Equal(0, results.ScoreDocs[0].DocId);
    }

    /// <summary>
    /// Verifies the Must Not: Excludes From Must Results scenario.
    /// </summary>
    [Fact(DisplayName = "Must Not: Excludes From Must Results")]
    public void MustNot_ExcludesFromMustResults()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_mustnot"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[] { "search engine", "search database", "search cache" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "search"), Occur.Must),
            (new TermQuery("body", "database"), Occur.MustNot));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Must Not: Multiple Clauses Excludes All scenario.
    /// </summary>
    [Fact(DisplayName = "Must Not: Multiple Clauses Excludes All")]
    public void MustNot_MultipleClauses_ExcludesAll()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_multi_mustnot"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[] { "search engine", "search database", "search cache", "search proxy" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "search"), Occur.Must),
            (new TermQuery("body", "database"), Occur.MustNot),
            (new TermQuery("body", "cache"), Occur.MustNot));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Must: With Should Should Boosts Score scenario.
    /// </summary>
    [Fact(DisplayName = "Must: With Should Should Boosts Score")]
    public void Must_WithShould_ShouldBoostsScore()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_must_should"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Both match "fast", but doc 0 also matches the Should clause "search"
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "fast search"));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "fast indexing"));
        writer.AddDocument(doc2);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "fast"), Occur.Must),
            (new TermQuery("body", "search"), Occur.Should));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.TotalHits);
        // Doc matching both Must + Should should rank higher
        Assert.Equal(0, results.ScoreDocs[0].DocId);
        Assert.True(results.ScoreDocs[0].Score > results.ScoreDocs[1].Score);
    }

    /// <summary>
    /// Verifies the Must: Nested Should Group And Must Term Returns Matching Doc scenario.
    /// </summary>
    [Fact(DisplayName = "Must: Nested Should Group And Must Term Returns Matching Doc")]
    public void Must_NestedShouldGroupAndMustTerm_ReturnsMatchingDoc()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_nested_should_must"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("title", "look after", stored: false));
        doc.Add(new StoredField("id", "1"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var alternatives = BuildBooleanQuery(
            (new TermQuery("title", "look"), Occur.Should),
            (new TermQuery("title", "looks"), Occur.Should),
            (new TermQuery("title", "looked"), Occur.Should));
        var query = BuildBooleanQuery(
            (alternatives, Occur.Must),
            (new TermQuery("title", "after"), Occur.Must));

        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(1, results.TotalHits);
        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        Assert.Equal("1", stored["id"][0]);
    }

    /// <summary>
    /// Verifies the Deleted Docs: Excluded From Streaming Results scenario.
    /// </summary>
    [Fact(DisplayName = "Deleted Docs: Excluded From Streaming Results")]
    public void DeletedDocs_ExcludedFromStreamingResults()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_deleted"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[] { "fast search", "fast indexing", "fast querying" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Delete doc 1
        writer.DeleteDocuments(new TermQuery("body", "indexing"));
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "fast"), Occur.Must));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.TotalHits);
        var docIds = results.ScoreDocs.Select(sd => sd.DocId).ToHashSet();
        Assert.DoesNotContain(1, docIds);
    }

    /// <summary>
    /// Verifies the WAND path excludes deleted documents when block-max skipping is enabled.
    /// </summary>
    [Fact(DisplayName = "WAND Deleted Docs: Excluded From Results")]
    public void Wand_DeletedDocs_ExcludedFromResults()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_wand_deleted"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[] { "fast search", "fast indexing", "fast querying" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        // Delete doc 1
        writer.DeleteDocuments(new TermQuery("body", "indexing"));
        writer.Commit();

        var config = new IndexSearcherConfig { EnableBlockMaxWand = true };
        using var searcher = new IndexSearcher(dir, config);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "fast"), Occur.Should),
            (new TermQuery("body", "search"), Occur.Should));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.TotalHits);
        var docIds = results.ScoreDocs.Select(sd => sd.DocId).ToHashSet();
        Assert.DoesNotContain(1, docIds);
    }

    /// <summary>
    /// Verifies the Must Not: All Excluded Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Must Not: All Excluded Returns Empty")]
    public void MustNot_AllExcluded_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_all_excluded"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // All docs match both Must and MustNot terms
        var texts = new[] { "fast search", "fast query search" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "fast"), Occur.Must),
            (new TermQuery("body", "search"), Occur.MustNot));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Empty Index: Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Index: Returns Empty")]
    public void EmptyIndex_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_empty"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "anything"), Occur.Must));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Should With Must Not: Streaming Merge Excludes Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Should With Must Not: Streaming Merge Excludes Correctly")]
    public void ShouldWithMustNot_StreamingMerge_ExcludesCorrectly()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_should_mustnot"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var texts = new[] { "alpha beta", "gamma delta", "alpha gamma", "beta delta" };
        foreach (var text in texts)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "alpha"), Occur.Should),
            (new TermQuery("body", "beta"), Occur.Should),
            (new TermQuery("body", "gamma"), Occur.MustNot));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        // Docs: 0 (alpha beta), 1 (gamma delta), 2 (alpha gamma), 3 (beta delta)
        // Should matches: alpha→[0,2], beta→[0,3] → union [0,2,3]
        // MustNot gamma→[1,2] → exclude doc 2
        // Expected: docs 0 and 3
        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Should: Three Terms Scores Multi Match Higher scenario.
    /// </summary>
    [Fact(DisplayName = "Should: Three Terms Scores Multi Match Higher")]
    public void Should_ThreeTerms_ScoresMultiMatchHigher()
    {
        var dir = new MMapDirectory(SubDir("bool_stream_should_three"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Doc 0 matches all three Should terms
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "red green blue"));
        writer.AddDocument(doc1);

        // Doc 1 matches two
        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "red green yellow"));
        writer.AddDocument(doc2);

        // Doc 2 matches one
        var doc3 = new LeanDocument();
        doc3.Add(new TextField("body", "red yellow purple"));
        writer.AddDocument(doc3);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var query = BuildBooleanQuery(
            (new TermQuery("body", "red"), Occur.Should),
            (new TermQuery("body", "green"), Occur.Should),
            (new TermQuery("body", "blue"), Occur.Should));
        var results = searcher.Search(query, 10, TestContext.Current.CancellationToken);

        Assert.Equal(3, results.TotalHits);
        // Doc matching most terms should rank highest
        Assert.Equal(0, results.ScoreDocs[0].DocId);
        Assert.True(results.ScoreDocs[0].Score > results.ScoreDocs[1].Score);
        Assert.True(results.ScoreDocs[1].Score > results.ScoreDocs[2].Score);
    }
}
