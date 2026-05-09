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

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Large-scale stress tests: 100K document indexing, segment merging behavior,
/// performance characteristics under high load, and memory-mapped file scalability.
/// </summary>
[Trait("Category", "Stress")]
public sealed class LargeScaleStressTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public LargeScaleStressTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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

    // ── Index 100K documents ───────────────────────────────────────────────

    /// <summary>
    /// Verifies the Index 20 K Docs: All Searchable scenario.
    /// </summary>
    [Fact(DisplayName = "Index 20 K Docs: All Searchable")]
    public void Index20KDocs_AllSearchable()
    {
        // Arrange — 20K is enough to exercise multi-segment merge without taking minutes
        var dir = new MMapDirectory(SubDir("index_20k"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 10_000 };
        const int totalDocs = 20_000;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < totalDocs; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"document {i} with content"));
                doc.Add(new StringField("id", i.ToString()));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        stopwatch.Stop();
        _output.WriteLine($"Indexing completed in {stopwatch.ElapsedMilliseconds:N0}ms ({totalDocs / stopwatch.Elapsed.TotalSeconds:N0} docs/sec)");

        // Assert - Verify documents are searchable (use modest topN)
        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "document"), topN: totalDocs + 1);

        _output.WriteLine($"Search returned {results.TotalHits:N0} hits");
        Assert.Equal(totalDocs, results.TotalHits);
        Assert.NotEmpty(results.ScoreDocs);

        var firstDoc = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        var lastDoc = searcher.GetStoredFields(results.ScoreDocs[^1].DocId);
        Assert.NotNull(firstDoc);
        Assert.NotNull(lastDoc);
        Assert.Contains("id", firstDoc.Keys);
        Assert.Contains("id", lastDoc.Keys);

        _output.WriteLine($"First doc ID={firstDoc["id"][0]}, Last doc ID={lastDoc["id"][0]}");
    }

    // ── Merge after many small segments ────────────────────────────────────

    /// <summary>
    /// Verifies the Merge After Many Small Segments: Preserves All Docs scenario.
    /// </summary>
    [Fact(DisplayName = "Merge After Many Small Segments: Preserves All Docs")]
    public void MergeAfterManySmallSegments_PreservesAllDocs()
    {
        // Arrange — small buffer forces multiple segments, exercising merge
        var dir = new MMapDirectory(SubDir("merge_small_segments"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 500 };
        const int totalDocs = 2_000;

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < totalDocs; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"segment document {i}"));
                doc.Add(new StringField("id", i.ToString()));
                doc.Add(new NumericField("sequence", i));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        stopwatch.Stop();
        _output.WriteLine($"Indexing with small segments completed in {stopwatch.ElapsedMilliseconds:N0}ms");

        var segmentFiles = Directory.GetFiles(dir.DirectoryPath, "*.pos");
        _output.WriteLine($"Found {segmentFiles.Length} postings files (indicating segments)");

        // Assert - all documents searchable after merge
        using var searcher = new IndexSearcher(dir);

        var allDocsResults = searcher.Search(new TermQuery("body", "segment"), topN: totalDocs + 1);
        _output.WriteLine($"Search for 'segment' returned {allDocsResults.TotalHits:N0} hits");
        Assert.Equal(totalDocs, allDocsResults.TotalHits);

        var specificResults = searcher.Search(new TermQuery("body", "1500"), topN: 10);
        _output.WriteLine($"Search for '1500' returned {specificResults.TotalHits:N0} hits");
        Assert.True(specificResults.TotalHits > 0, "Should find document 1500");

        var rangeResults = searcher.Search(new RangeQuery("sequence", 0, totalDocs - 1), topN: totalDocs + 1);
        _output.WriteLine($"Range query [0, {totalDocs - 1}] returned {rangeResults.TotalHits:N0} hits");
        Assert.Equal(totalDocs, rangeResults.TotalHits);

        var sampleDoc = searcher.GetStoredFields(allDocsResults.ScoreDocs[0].DocId);
        Assert.Contains("id", sampleDoc.Keys);
        Assert.Contains("sequence", sampleDoc.Keys);
        _output.WriteLine($"Sample doc id={sampleDoc["id"][0]}, sequence={sampleDoc["sequence"][0]}");
        _output.WriteLine("✓ All documents searchable after merge policy applied");
    }
}
