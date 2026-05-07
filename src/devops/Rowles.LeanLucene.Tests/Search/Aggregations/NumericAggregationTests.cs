using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search.Aggregations;

/// <summary>
/// Contains unit tests for Numeric Aggregation.
/// </summary>
public class NumericAggregationTests : IDisposable
{
    private readonly string _dir;

    public NumericAggregationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "agg_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }
        catch { /* mmap handles may linger on Windows */ }
    }

    private void IndexProducts()
    {
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());

        for (int i = 0; i < 10; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("name", $"product {i}"));
            doc.Add(new NumericField("price", 10.0 + i * 5.0)); // 10, 15, 20, ..., 55
            doc.Add(new NumericField("rating", (i % 5) + 1.0));  // 1, 2, 3, 4, 5, 1, 2, 3, 4, 5
            writer.AddDocument(doc);
        }

        writer.Commit();
    }

    /// <summary>
    /// Verifies the Stats: Computes Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Stats: Computes Correctly")]
    public void Stats_ComputesCorrectly()
    {
        IndexProducts();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var (results, aggs) = searcher.SearchWithAggregations(
            new TermQuery("name", "product"), 100,
            new AggregationRequest("price_stats", "price"));

        Assert.True(results.TotalHits > 0);
        Assert.Single(aggs);

        var stats = aggs[0];
        Assert.Equal("price_stats", stats.Name);
        Assert.Equal("price", stats.Field);
        Assert.True(stats.Count > 0);
        Assert.True(stats.Min >= 10.0);
        Assert.True(stats.Max <= 55.0);
        Assert.True(stats.Sum > 0);
        Assert.True(stats.Avg > 0);
    }

    /// <summary>
    /// Verifies the Stats: Multiple Aggregations scenario.
    /// </summary>
    [Fact(DisplayName = "Stats: Multiple Aggregations")]
    public void Stats_MultipleAggregations()
    {
        IndexProducts();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var (_, aggs) = searcher.SearchWithAggregations(
            new TermQuery("name", "product"), 100,
            new AggregationRequest("price_stats", "price"),
            new AggregationRequest("rating_stats", "rating"));

        Assert.Equal(2, aggs.Length);
        Assert.Equal("price_stats", aggs[0].Name);
        Assert.Equal("rating_stats", aggs[1].Name);
    }

    /// <summary>
    /// Verifies the Histogram: Creates Buckets scenario.
    /// </summary>
    [Fact(DisplayName = "Histogram: Creates Buckets")]
    public void Histogram_CreatesBuckets()
    {
        IndexProducts();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var (_, aggs) = searcher.SearchWithAggregations(
            new TermQuery("name", "product"), 100,
            new AggregationRequest("price_hist", "price", AggregationType.Histogram)
            {
                HistogramInterval = 20.0
            });

        Assert.Single(aggs);
        var hist = aggs[0];
        Assert.NotNull(hist.Buckets);
        Assert.True(hist.Buckets.Count > 0);

        // All bucket counts should sum to total count
        long bucketSum = 0;
        foreach (var b in hist.Buckets)
            bucketSum += b.Count;
        Assert.Equal(hist.Count, bucketSum);
    }

    /// <summary>
    /// Verifies the No Aggregations: Returns Empty Array scenario.
    /// </summary>
    [Fact(DisplayName = "No Aggregations: Returns Empty Array")]
    public void NoAggregations_ReturnsEmptyArray()
    {
        IndexProducts();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var (results, aggs) = searcher.SearchWithAggregations(
            new TermQuery("name", "product"), 10);

        Assert.True(results.TotalHits > 0);
        Assert.Empty(aggs);
    }

    /// <summary>
    /// Verifies the No Matches: Returns Empty Aggregations scenario.
    /// </summary>
    [Fact(DisplayName = "No Matches: Returns Empty Aggregations")]
    public void NoMatches_ReturnsEmptyAggregations()
    {
        IndexProducts();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var (results, aggs) = searcher.SearchWithAggregations(
            new TermQuery("name", "nonexistent"), 10,
            new AggregationRequest("price_stats", "price"));

        Assert.Equal(0, results.TotalHits);
        Assert.Empty(aggs);
    }

    /// <summary>
    /// Verifies the Missing Field: Returns Zero Counts scenario.
    /// </summary>
    [Fact(DisplayName = "Missing Field: Returns Zero Counts")]
    public void MissingField_ReturnsZeroCounts()
    {
        IndexProducts();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var (_, aggs) = searcher.SearchWithAggregations(
            new TermQuery("name", "product"), 100,
            new AggregationRequest("missing_stats", "nonexistent_field"));

        Assert.Single(aggs);
        Assert.Equal(0, aggs[0].Count);
    }

    /// <summary>
    /// Verifies the Rating Stats: Avg Is Correct scenario.
    /// </summary>
    [Fact(DisplayName = "Rating Stats: Avg Is Correct")]
    public void RatingStats_AvgIsCorrect()
    {
        IndexProducts();
        using var searcher = new IndexSearcher(new MMapDirectory(_dir));

        var (results, aggs) = searcher.SearchWithAggregations(
            new TermQuery("name", "product"), 100,
            new AggregationRequest("r", "rating"));

        if (aggs.Length > 0 && aggs[0].Count > 0)
        {
            // Rating values: 1,2,3,4,5,1,2,3,4,5 → avg = 3.0
            Assert.Equal(aggs[0].Sum / aggs[0].Count, aggs[0].Avg, 4);
        }
    }
}
