using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for Facet Aggregation Collapse Correctness.
/// </summary>
public sealed class FacetAggregationCollapseCorrectnessTests : IDisposable
{
    private readonly string _dir;

    public FacetAggregationCollapseCorrectnessTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_exact_collectors_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch { }
    }

    /// <summary>
    /// Verifies the Facets: Count All Matching Documents Not Only Top N scenario.
    /// </summary>
    [Fact(DisplayName = "Facets: Count All Matching Documents Not Only Top N")]
    public void Facets_CountAllMatchingDocuments_NotOnlyTopN()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        for (int i = 0; i < 12; i++)
            writer.AddDocument(MakeDocument("common", i < 11 ? "dominant" : "rare", i));
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacets(new TermQuery("body", "common"), 1, "group");

        var groupFacet = Assert.Single(facets);
        Assert.Equal(11, groupFacet.Buckets.Single(b => b.Value == "dominant").Count);
        Assert.Equal(1, groupFacet.Buckets.Single(b => b.Value == "rare").Count);
    }

    /// <summary>
    /// Verifies the Aggregations: Count All Matching Documents Not Only Top N scenario.
    /// </summary>
    [Fact(DisplayName = "Aggregations: Count All Matching Documents Not Only Top N")]
    public void Aggregations_CountAllMatchingDocuments_NotOnlyTopN()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        for (int i = 0; i < 12; i++)
            writer.AddDocument(MakeDocument("common", "all", i));
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, aggregations) = searcher.SearchWithAggregations(
            new TermQuery("body", "common"),
            1,
            new AggregationRequest("price_stats", "price"));

        Assert.Equal(12, aggregations[0].Count);
        Assert.Equal(66, aggregations[0].Sum);
    }

    /// <summary>
    /// Verifies multi-valued facets use sorted-set DocValues and do not require stored fields.
    /// </summary>
    [Fact(DisplayName = "Facets: Multi Valued String Fields Use Doc Values")]
    public void Facets_MultiValuedStringFields_UseDocValues()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "common"));
        doc1.Add(new StringField("tag", "red", stored: false));
        doc1.Add(new StringField("tag", "blue", stored: false));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "common"));
        doc2.Add(new StringField("tag", "red", stored: false));
        writer.AddDocument(doc2);
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, facets) = searcher.SearchWithFacets(new TermQuery("body", "common"), 1, "tag");

        var tagFacet = Assert.Single(facets);
        Assert.Equal(2, tagFacet.Buckets.Single(b => b.Value == "red").Count);
        Assert.Equal(1, tagFacet.Buckets.Single(b => b.Value == "blue").Count);
    }

    /// <summary>
    /// Verifies repeated numeric values contribute every sorted-numeric value to stats.
    /// </summary>
    [Fact(DisplayName = "Aggregations: Multi Valued Numeric Fields Use All Values")]
    public void Aggregations_MultiValuedNumericFields_UseAllValues()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "common"));
        doc1.Add(new NumericField("price", 10, stored: false));
        doc1.Add(new NumericField("price", 2, stored: false));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "common"));
        doc2.Add(new NumericField("price", 3, stored: false));
        writer.AddDocument(doc2);
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var (_, aggregations) = searcher.SearchWithAggregations(
            new TermQuery("body", "common"),
            1,
            new AggregationRequest("price_stats", "price"));

        Assert.Equal(3, aggregations[0].Count);
        Assert.Equal(2, aggregations[0].Min);
        Assert.Equal(10, aggregations[0].Max);
        Assert.Equal(15, aggregations[0].Sum);
    }

    /// <summary>
    /// Verifies the Collapse: Sees Groups Outside Original Over Fetch Window scenario.
    /// </summary>
    [Fact(DisplayName = "Collapse: Sees Groups Outside Original Over Fetch Window")]
    public void Collapse_SeesGroupsOutsideOriginalOverFetchWindow()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        for (int i = 0; i < 11; i++)
            writer.AddDocument(MakeDocument("common", "dominant", i));
        writer.AddDocument(MakeDocument("common", "rare", 99));
        writer.Commit();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        var results = searcher.SearchWithCollapse(
            new TermQuery("body", "common"),
            1,
            new CollapseField("group"));

        Assert.Equal(2, results.TotalHits);
        Assert.Single(results.ScoreDocs);
    }

    private static LeanDocument MakeDocument(string body, string group, double price)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        doc.Add(new StringField("group", group));
        doc.Add(new NumericField("price", price));
        return doc;
    }
}
