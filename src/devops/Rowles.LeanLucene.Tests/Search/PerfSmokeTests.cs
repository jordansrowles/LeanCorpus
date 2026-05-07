using System.Diagnostics;
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
/// Contains unit tests for Perf Smoke.
/// </summary>
[Trait("Category", "Perf")]
[Trait("Coverage", "Skip")]
public sealed class PerfSmokeTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public PerfSmokeTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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

    /// <summary>
    /// Verifies the Term Query: Perf Smoke Measures Latency And Allocations scenario.
    /// </summary>
    [Fact(DisplayName = "Term Query: Perf Smoke Measures Latency And Allocations")]
    public void TermQuery_PerfSmoke_MeasuresLatencyAndAllocations()
    {
        const int docCount = 500;
        const int warmup = 200;
        const int iterations = 500;
        const int topN = 25;
        const string queryTerm = "search";

        // Arrange: build index
        var dir = new MMapDirectory(SubDir("perf_smoke"));
        var rng = new Random(42);
        string[] terms = ["search", "index", "performance", "vector", "document", "query", "engine", "test", "data", "field"];

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 256 }))
        {
            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                // Each doc gets ~10 words, roughly 1/3 will contain "search"
                var words = Enumerable.Range(0, 10).Select(_ => terms[rng.Next(terms.Length)]);
                doc.Add(new TextField("body", string.Join(" ", words)));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new TermQuery("body", queryTerm);

        // Warmup
        for (int i = 0; i < warmup; i++)
            searcher.Search(query, topN);

        // Measure
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
            searcher.Search(query, topN);

        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgUs = sw.Elapsed.TotalMicroseconds / iterations;
        double avgKb = (double)(allocAfter - allocBefore) / iterations / 1024.0;

        _output.WriteLine($"TermQuery(\"{queryTerm}\") over {docCount} docs, top {topN}:");
        _output.WriteLine($"  Avg latency:    {avgUs:F1} µs");
        _output.WriteLine($"  Avg allocation: {avgKb:F2} KB");
        _output.WriteLine($"  Total hits:     {searcher.Search(query, topN).TotalHits}");

        // Tightened bounds: 3× of typical measured baseline (was 50,000 µs / 5,000 KB)
        Assert.True(avgUs < 2_000, $"Latency {avgUs:F0} µs exceeds 2,000 µs budget");
        Assert.True(avgKb < 100, $"Allocation {avgKb:F0} KB exceeds 100 KB budget");
    }
}
