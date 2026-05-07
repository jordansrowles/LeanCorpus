using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
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
/// Tests for Stage 2 features: ISimilarity, FunctionScoreQuery, SpanQueries, FacetsCollector.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "Stage2")]
public sealed class Stage2SearchTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public Stage2SearchTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    // ── ISimilarity ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the BM25 Similarity: Score Returns Positive Value scenario.
    /// </summary>
    [Fact(DisplayName = "BM25 Similarity: Score Returns Positive Value")]
    public void Bm25Similarity_Score_ReturnsPositiveValue()
    {
        var sim = Bm25Similarity.Instance;
        float score = sim.Score(termFreq: 2, docLength: 100, avgDocLength: 80f, totalDocCount: 1000, docFreq: 10);
        Assert.True(score > 0f);
    }

    /// <summary>
    /// Verifies the Tf Idf Similarity: Score Returns Positive Value scenario.
    /// </summary>
    [Fact(DisplayName = "Tf Idf Similarity: Score Returns Positive Value")]
    public void TfIdfSimilarity_Score_ReturnsPositiveValue()
    {
        var sim = TfIdfSimilarity.Instance;
        float score = sim.Score(termFreq: 2, docLength: 100, avgDocLength: 80f, totalDocCount: 1000, docFreq: 10);
        Assert.True(score > 0f);
    }

    /// <summary>
    /// Verifies the Tf Idf Similarity: Precompute Factors Id F Is Positive scenario.
    /// </summary>
    [Fact(DisplayName = "Tf Idf Similarity: Precompute Factors Id F Is Positive")]
    public void TfIdfSimilarity_PrecomputeFactors_IdFIsPositive()
    {
        var sim = TfIdfSimilarity.Instance;
        var (idf, _) = sim.PrecomputeFactors(totalDocCount: 1000, docFreq: 10, avgDocLength: 80f);
        Assert.True(idf > 0f);
    }

    /// <summary>
    /// Verifies the Tf Idf Similarity: Score Precomputed Matches Full Score scenario.
    /// </summary>
    [Fact(DisplayName = "Tf Idf Similarity: Score Precomputed Matches Full Score")]
    public void TfIdfSimilarity_ScorePrecomputed_MatchesFullScore()
    {
        var sim = TfIdfSimilarity.Instance;
        float full = sim.Score(termFreq: 3, docLength: 50, avgDocLength: 80f, totalDocCount: 1000, docFreq: 5);
        var (factor1, factor2) = sim.PrecomputeFactors(1000, 5, 80f);
        float precomputed = sim.ScorePrecomputed(factor1, factor2, termFreq: 3, docLength: 50);
        Assert.Equal(full, precomputed, precision: 4);
    }

    /// <summary>
    /// Verifies the Index Searcher: With Tf Idf Similarity Returns Results scenario.
    /// </summary>
    [Fact(DisplayName = "Index Searcher: With Tf Idf Similarity Returns Results")]
    public void IndexSearcher_WithTfIdfSimilarity_ReturnsResults()
    {
        var dir = new MMapDirectory(SubDir("sim_tfidf"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "the quick brown fox"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir, TfIdfSimilarity.Instance);
        var results = searcher.Search(new TermQuery("body", "quick"), 10);
        Assert.Equal(1, results.TotalHits);
        Assert.True(results.ScoreDocs[0].Score > 0f);
    }

    // ── FunctionScoreQuery ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Function Score Query: Multiply Boosts Score By Field Value scenario.
    /// </summary>
    [Fact(DisplayName = "Function Score Query: Multiply Boosts Score By Field Value")]
    public void FunctionScoreQuery_Multiply_BoostsScoreByFieldValue()
    {
        var dir = new MMapDirectory(SubDir("fsq_multiply"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        for (int i = 1; i <= 3; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world"));
            doc.Add(new NumericField("boost", i * 10.0));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var inner = new TermQuery("body", "hello");
        var fsq = new FunctionScoreQuery(inner, "boost", ScoreMode.Multiply);
        var results = searcher.Search(fsq, 10);
        Assert.Equal(3, results.TotalHits);
        // Higher boost field value → higher score
        Assert.True(results.ScoreDocs[0].Score >= results.ScoreDocs[1].Score);
    }

    /// <summary>
    /// Verifies the Function Score Query: Replace Uses Field Value As Score scenario.
    /// </summary>
    [Fact(DisplayName = "Function Score Query: Replace Uses Field Value As Score")]
    public void FunctionScoreQuery_Replace_UsesFieldValueAsScore()
    {
        var dir = new MMapDirectory(SubDir("fsq_replace"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "test"));
        doc1.Add(new NumericField("rank", 100.0));
        writer.AddDocument(doc1);
        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "test"));
        doc2.Add(new NumericField("rank", 50.0));
        writer.AddDocument(doc2);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var fsq = new FunctionScoreQuery(new TermQuery("body", "test"), "rank", ScoreMode.Replace);
        var results = searcher.Search(fsq, 10);
        Assert.Equal(2, results.TotalHits);
        Assert.Equal(100.0f, results.ScoreDocs[0].Score, precision: 1);
        Assert.Equal(50.0f, results.ScoreDocs[1].Score, precision: 1);
    }

    /// <summary>
    /// Verifies the Function Score Query: Combine All Modes scenario.
    /// </summary>
    [Fact(DisplayName = "Function Score Query: Combine All Modes")]
    public void FunctionScoreQuery_Combine_AllModes()
    {
        Assert.Equal(6f, FunctionScoreQuery.Combine(2f, 3.0, ScoreMode.Multiply));
        Assert.Equal(3f, FunctionScoreQuery.Combine(2f, 3.0, ScoreMode.Replace));
        Assert.Equal(5f, FunctionScoreQuery.Combine(2f, 3.0, ScoreMode.Sum));
        Assert.Equal(3f, FunctionScoreQuery.Combine(2f, 3.0, ScoreMode.Max));
    }

    // ── SpanQueries ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Span Near Query: Adjacent Terms Finds Match scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: Adjacent Terms Finds Match")]
    public void SpanNearQuery_AdjacentTerms_FindsMatch()
    {
        var dir = new MMapDirectory(SubDir("span_near"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "the quick brown fox jumps over the lazy dog"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var q = new SpanNearQuery(
            [new SpanTermQuery("body", "quick"), new SpanTermQuery("body", "brown")],
            slop: 0, inOrder: true);
        var results = searcher.Search(q, 10);
        Assert.Equal(1, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Span Near Query: With Slop Finds Non Adjacent Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: With Slop Finds Non Adjacent Terms")]
    public void SpanNearQuery_WithSlop_FindsNonAdjacentTerms()
    {
        var dir = new MMapDirectory(SubDir("span_slop"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "the quick brown fox jumps over the lazy dog"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var q = new SpanNearQuery(
            [new SpanTermQuery("body", "quick"), new SpanTermQuery("body", "fox")],
            slop: 2, inOrder: true);
        var results = searcher.Search(q, 10);
        Assert.Equal(1, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Span Near Query: Exceeds Slop No Match scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: Exceeds Slop No Match")]
    public void SpanNearQuery_ExceedsSlop_NoMatch()
    {
        var dir = new MMapDirectory(SubDir("span_noslop"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "the quick brown fox jumps over the lazy dog"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var q = new SpanNearQuery(
            [new SpanTermQuery("body", "quick"), new SpanTermQuery("body", "lazy")],
            slop: 1, inOrder: true);
        var results = searcher.Search(q, 10);
        Assert.Equal(0, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Span Or Query: Returns Union Of Matches scenario.
    /// </summary>
    [Fact(DisplayName = "Span Or Query: Returns Union Of Matches")]
    public void SpanOrQuery_ReturnsUnionOfMatches()
    {
        var dir = new MMapDirectory(SubDir("span_or"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "the quick fox"));
        writer.AddDocument(doc1);
        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "the lazy dog"));
        writer.AddDocument(doc2);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var q = new SpanOrQuery(
            new SpanTermQuery("body", "quick"),
            new SpanTermQuery("body", "lazy"));
        var results = searcher.Search(q, 10);
        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Span Not Query: Excludes Matching Spans scenario.
    /// </summary>
    [Fact(DisplayName = "Span Not Query: Excludes Matching Spans")]
    public void SpanNotQuery_ExcludesMatchingSpans()
    {
        var dir = new MMapDirectory(SubDir("span_not"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "the quick brown fox"));
        writer.AddDocument(doc1);
        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "the slow brown dog"));
        writer.AddDocument(doc2);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        // Include docs with "brown", exclude docs with "fox"
        var q = new SpanNotQuery(
            new SpanTermQuery("body", "brown"),
            new SpanTermQuery("body", "fox"));
        var results = searcher.Search(q, 10);
        Assert.Equal(1, results.TotalHits);
    }

    // ── Facets ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Search With Facets: Returns Facet Counts scenario.
    /// </summary>
    [Fact(DisplayName = "Search With Facets: Returns Facet Counts")]
    public void SearchWithFacets_ReturnsFacetCounts()
    {
        var dir = new MMapDirectory(SubDir("facets"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var (color, size) in new[] { ("red", "small"), ("red", "large"), ("blue", "small"), ("blue", "small") })
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"{color} {size} widget"));
            doc.Add(new StringField("color", color));
            doc.Add(new StringField("size", size));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var (results, facets) = searcher.SearchWithFacets(new TermQuery("body", "widget"), 10, "color", "size");
        Assert.Equal(4, results.TotalHits);
        Assert.Equal(2, facets.Count);

        var colorFacet = facets.First(f => f.FieldName == "color");
        Assert.Contains(colorFacet.Buckets, b => b.Value == "red" && b.Count == 2);
        Assert.Contains(colorFacet.Buckets, b => b.Value == "blue" && b.Count == 2);

        var sizeFacet = facets.First(f => f.FieldName == "size");
        Assert.Contains(sizeFacet.Buckets, b => b.Value == "small" && b.Count == 3);
        Assert.Contains(sizeFacet.Buckets, b => b.Value == "large" && b.Count == 1);
    }

    /// <summary>
    /// Verifies the Facets Collector: Empty Results Returns Empty Facets scenario.
    /// </summary>
    [Fact(DisplayName = "Facets Collector: Empty Results Returns Empty Facets")]
    public void FacetsCollector_EmptyResults_ReturnsEmptyFacets()
    {
        var collector = new FacetsCollector();
        var results = collector.GetResults();
        Assert.Empty(results);
    }

    /// <summary>
    /// Verifies the Facets Collector: Single Field Correct Counts scenario.
    /// </summary>
    [Fact(DisplayName = "Facets Collector: Single Field Correct Counts")]
    public void FacetsCollector_SingleField_CorrectCounts()
    {
        var collector = new FacetsCollector();
        collector.Collect("category", "books");
        collector.Collect("category", "books");
        collector.Collect("category", "movies");
        var results = collector.GetResults();
        Assert.Single(results);
        Assert.Equal("category", results[0].FieldName);
        Assert.Equal(2, results[0].Buckets.First(b => b.Value == "books").Count);
        Assert.Equal(1, results[0].Buckets.First(b => b.Value == "movies").Count);
    }
}
