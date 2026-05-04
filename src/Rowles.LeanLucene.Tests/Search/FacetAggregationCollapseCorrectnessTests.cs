using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search;

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

    [Fact]
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

    [Fact]
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

    [Fact]
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
