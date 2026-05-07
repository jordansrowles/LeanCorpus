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

namespace Rowles.LeanLucene.Tests.Codecs;

/// <summary>
/// BKD tree edge case tests: numeric field indexing and range queries,
/// testing boundary conditions, duplicate values, empty ranges, and negative numbers.
/// BKD (Block KD-Tree) is used internally for efficient numeric range queries.
/// </summary>
[Trait("Category", "Codecs")]
public sealed class BKDTreeEdgeCaseTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BKDTreeEdgeCaseTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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

    // ── All same value ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the All Same Value: Range Query Finds All scenario.
    /// </summary>
    [Fact(DisplayName = "All Same Value: Range Query Finds All")]
    public void AllSameValue_RangeQueryFindsAll()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("bkd_same_value"));
        const int docCount = 100;
        const double samePrice = 42.0;

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new NumericField("price", samePrice));
                doc.Add(new StringField("id", i.ToString()));
                doc.Add(new TextField("body", $"product {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _output.WriteLine($"Indexed {docCount} documents all with price={samePrice}");

        // Act - Query with range that includes the value
        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new RangeQuery("price", 40.0, 45.0), topN: docCount + 1);

        // Assert
        _output.WriteLine($"RangeQuery [40.0, 45.0] returned {results.TotalHits} hits");
        Assert.Equal(docCount, results.TotalHits);
        Assert.Equal(docCount, results.ScoreDocs.Length);

        // Verify stored fields
        var firstDoc = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        var lastDoc = searcher.GetStoredFields(results.ScoreDocs[^1].DocId);
        _output.WriteLine($"First doc: id={firstDoc["id"][0]}, price={firstDoc["price"][0]}");
        _output.WriteLine($"Last doc: id={lastDoc["id"][0]}, price={lastDoc["price"][0]}");
        Assert.Equal(samePrice.ToString(), firstDoc["price"][0]);
        Assert.Equal(samePrice.ToString(), lastDoc["price"][0]);

        _output.WriteLine("✓ Range query correctly finds all documents with identical numeric value");
    }

    // ── Empty range ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Empty Range: Returns No Results scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Range: Returns No Results")]
    public void EmptyRange_ReturnsNoResults()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("bkd_empty_range"));
        const int docCount = 100;

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 1; i <= docCount; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new NumericField("price", i)); // Prices from 1 to 100
                doc.Add(new StringField("id", i.ToString()));
                doc.Add(new TextField("body", $"item {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _output.WriteLine($"Indexed {docCount} documents with prices 1 to {docCount}");

        // Act - Query for range outside all values
        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new RangeQuery("price", 200.0, 300.0), topN: 10);

        // Assert
        _output.WriteLine($"RangeQuery [200.0, 300.0] returned {results.TotalHits} hits");
        Assert.Equal(0, results.TotalHits);
        Assert.Empty(results.ScoreDocs);

        // Also test range below all values
        var resultsBelow = searcher.Search(new RangeQuery("price", -100.0, 0.0), topN: 10);
        _output.WriteLine($"RangeQuery [-100.0, 0.0] returned {resultsBelow.TotalHits} hits");
        Assert.Equal(0, resultsBelow.TotalHits);

        _output.WriteLine("✓ Range queries outside data range correctly return zero results");
    }

    // ── Single point range ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Single Point Range: Finds Exact Match scenario.
    /// </summary>
    [Fact(DisplayName = "Single Point Range: Finds Exact Match")]
    public void SinglePointRange_FindsExactMatch()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("bkd_single_point"));
        const int docCount = 100;
        const int targetPrice = 50;

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 1; i <= docCount; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new NumericField("price", i)); // Prices from 1 to 100
                doc.Add(new StringField("id", i.ToString()));
                doc.Add(new TextField("body", $"product {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _output.WriteLine($"Indexed {docCount} documents with prices 1 to {docCount}");

        // Act - Query for exact single value (min == max)
        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new RangeQuery("price", targetPrice, targetPrice), topN: 10);

        // Assert
        _output.WriteLine($"RangeQuery [{targetPrice}, {targetPrice}] (single point) returned {results.TotalHits} hits");
        Assert.Equal(1, results.TotalHits);
        Assert.Single(results.ScoreDocs);

        // Verify it's the correct document
        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        _output.WriteLine($"Found document: id={stored["id"][0]}, price={stored["price"][0]}");
        Assert.Equal(targetPrice.ToString(), stored["id"][0]);
        Assert.Equal(targetPrice.ToString(), stored["price"][0]);

        // Test with a value that doesn't exist
        var noResults = searcher.Search(new RangeQuery("price", 50.5, 50.5), topN: 10);
        _output.WriteLine($"RangeQuery [50.5, 50.5] (non-existent point) returned {noResults.TotalHits} hits");
        Assert.Equal(0, noResults.TotalHits);

        _output.WriteLine("✓ Single-point range query works for exact matching");
    }

    // ── Negative values ─────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Negative Values: Handled Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Negative Values: Handled Correctly")]
    public void NegativeValues_HandledCorrectly()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("bkd_negative"));
        var temperatures = new[] { -20.0, -10.0, -5.0, 0.0, 5.0, 10.0, 20.0 };

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < temperatures.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new NumericField("temp", temperatures[i]));
                doc.Add(new StringField("id", $"reading{i}"));
                doc.Add(new TextField("body", $"temperature reading {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _output.WriteLine($"Indexed {temperatures.Length} documents with temperatures: {string.Join(", ", temperatures)}");

        using var searcher = new IndexSearcher(dir);

        // Act & Assert - Test 1: Range covering negative values
        var negativeResults = searcher.Search(new RangeQuery("temp", -20.0, 0.0), topN: 10);
        _output.WriteLine($"RangeQuery [-20.0, 0.0] returned {negativeResults.TotalHits} hits");
        Assert.Equal(4, negativeResults.TotalHits); // -20, -10, -5, 0

        // Test 2: Range covering only negative values (excluding zero)
        var strictlyNegative = searcher.Search(new RangeQuery("temp", -20.0, -0.1), topN: 10);
        _output.WriteLine($"RangeQuery [-20.0, -0.1] returned {strictlyNegative.TotalHits} hits");
        Assert.Equal(3, strictlyNegative.TotalHits); // -20, -10, -5

        // Test 3: Range spanning negative to positive
        var crossZero = searcher.Search(new RangeQuery("temp", -5.0, 10.0), topN: 10);
        _output.WriteLine($"RangeQuery [-5.0, 10.0] returned {crossZero.TotalHits} hits");
        Assert.Equal(4, crossZero.TotalHits); // -5, 0, 5, 10

        // Test 4: All values
        var allValues = searcher.Search(new RangeQuery("temp", -100.0, 100.0), topN: 10);
        _output.WriteLine($"RangeQuery [-100.0, 100.0] returned {allValues.TotalHits} hits");
        Assert.Equal(temperatures.Length, allValues.TotalHits);

        // Test 5: Verify stored field values
        var coldestDoc = searcher.GetStoredFields(negativeResults.ScoreDocs[0].DocId);
        _output.WriteLine($"Coldest reading: id={coldestDoc["id"][0]}, temp={coldestDoc["temp"][0]}");
        
        _output.WriteLine("✓ Negative numeric values handled correctly in BKD tree");
    }

    // ── Mixed precision values ──────────────────────────────────────────────

    /// <summary>
    /// Verifies the Mixed Precision Values: Range Query Correct scenario.
    /// </summary>
    [Fact(DisplayName = "Mixed Precision Values: Range Query Correct")]
    public void MixedPrecisionValues_RangeQueryCorrect()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("bkd_precision"));
        var values = new[] { 0.1, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 10.0, 100.0, 1000.0 };

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < values.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new NumericField("value", values[i]));
                doc.Add(new StringField("id", i.ToString()));
                doc.Add(new TextField("body", $"measurement {values[i]}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _output.WriteLine($"Indexed {values.Length} documents with mixed precision values");

        using var searcher = new IndexSearcher(dir);

        // Act & Assert - Test small range
        var smallRange = searcher.Search(new RangeQuery("value", 1.0, 3.0), topN: 10);
        _output.WriteLine($"RangeQuery [1.0, 3.0] returned {smallRange.TotalHits} hits");
        Assert.Equal(5, smallRange.TotalHits); // 1.0, 1.5, 2.0, 2.5, 3.0

        // Test fractional range
        var fractional = searcher.Search(new RangeQuery("value", 0.0, 1.0), topN: 10);
        _output.WriteLine($"RangeQuery [0.0, 1.0] returned {fractional.TotalHits} hits");
        Assert.Equal(3, fractional.TotalHits); // 0.1, 0.5, 1.0

        // Test large value range
        var largeRange = searcher.Search(new RangeQuery("value", 10.0, 1000.0), topN: 10);
        _output.WriteLine($"RangeQuery [10.0, 1000.0] returned {largeRange.TotalHits} hits");
        Assert.Equal(3, largeRange.TotalHits); // 10.0, 100.0, 1000.0

        _output.WriteLine("✓ Mixed precision numeric values handled correctly");
    }

    // ── Boundary inclusive behavior ─────────────────────────────────────────

    /// <summary>
    /// Verifies the Range Boundaries: Are Inclusive scenario.
    /// </summary>
    [Fact(DisplayName = "Range Boundaries: Are Inclusive")]
    public void RangeBoundaries_AreInclusive()
    {
        // Arrange
        var dir = new MMapDirectory(SubDir("bkd_boundaries"));
        var values = new[] { 10.0, 20.0, 30.0, 40.0, 50.0 };

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < values.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new NumericField("value", values[i]));
                doc.Add(new StringField("id", i.ToString()));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        // Act & Assert - Verify both boundaries are inclusive
        var results = searcher.Search(new RangeQuery("value", 20.0, 40.0), topN: 10);
        _output.WriteLine($"RangeQuery [20.0, 40.0] returned {results.TotalHits} hits");
        Assert.Equal(3, results.TotalHits); // Should include 20.0, 30.0, 40.0

        // Verify the actual values
        var docValues = new List<double>();
        foreach (var scoreDoc in results.ScoreDocs)
        {
            var stored = searcher.GetStoredFields(scoreDoc.DocId);
            var value = double.Parse(stored["value"][0]);
            docValues.Add(value);
            _output.WriteLine($"  Found doc with value={value}");
        }

        Assert.Contains(20.0, docValues);
        Assert.Contains(30.0, docValues);
        Assert.Contains(40.0, docValues);

        _output.WriteLine("✓ Range query boundaries are inclusive on both ends");
    }
}
