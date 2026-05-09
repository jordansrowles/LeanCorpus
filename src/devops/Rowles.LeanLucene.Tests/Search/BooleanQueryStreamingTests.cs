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
/// Contains unit tests for Boolean Query Streaming.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "BooleanQuery")]
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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "alpha"), Occur.Must);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "red"), Occur.Must);
        query.Add(new TermQuery("body", "green"), Occur.Must);
        query.Add(new TermQuery("body", "blue"), Occur.Must);
        var results = searcher.Search(query, 10);

        Assert.Equal(2, results.TotalHits);
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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "alpha"), Occur.Must);
        query.Add(new TermQuery("body", "beta"), Occur.Must);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "some"), Occur.Must);
        query.Add(new TermQuery("body", "nonexistent"), Occur.Must);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "alpha"), Occur.Should);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "alpha"), Occur.Should);
        query.Add(new TermQuery("body", "beta"), Occur.Should);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "search"), Occur.Must);
        query.Add(new TermQuery("body", "database"), Occur.MustNot);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "search"), Occur.Must);
        query.Add(new TermQuery("body", "database"), Occur.MustNot);
        query.Add(new TermQuery("body", "cache"), Occur.MustNot);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "fast"), Occur.Must);
        query.Add(new TermQuery("body", "search"), Occur.Should);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();

        var alternatives = new BooleanQuery();
        alternatives.Add(new TermQuery("title", "look"), Occur.Should);
        alternatives.Add(new TermQuery("title", "looks"), Occur.Should);
        alternatives.Add(new TermQuery("title", "looked"), Occur.Should);
        query.Add(alternatives, Occur.Must);

        query.Add(new TermQuery("title", "after"), Occur.Must);

        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "fast"), Occur.Must);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "fast"), Occur.Must);
        query.Add(new TermQuery("body", "search"), Occur.MustNot);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "anything"), Occur.Must);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "alpha"), Occur.Should);
        query.Add(new TermQuery("body", "beta"), Occur.Should);
        query.Add(new TermQuery("body", "gamma"), Occur.MustNot);
        var results = searcher.Search(query, 10);

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
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "red"), Occur.Should);
        query.Add(new TermQuery("body", "green"), Occur.Should);
        query.Add(new TermQuery("body", "blue"), Occur.Should);
        var results = searcher.Search(query, 10);

        Assert.Equal(3, results.TotalHits);
        // Doc matching most terms should rank highest
        Assert.Equal(0, results.ScoreDocs[0].DocId);
        Assert.True(results.ScoreDocs[0].Score > results.ScoreDocs[1].Score);
        Assert.True(results.ScoreDocs[1].Score > results.ScoreDocs[2].Score);
    }
}
