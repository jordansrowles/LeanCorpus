using System.Diagnostics;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanLucene.Tests.Benchmarks;

/// <summary>
/// Microbenchmark contrasting numeric range search latency against a baseline
/// linear scan over the same corpus. Skipped by default; flip the skip flag
/// off to run locally.
/// </summary>
[Trait("Category", "Benchmark")]
public sealed class BkdSearchBench : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BkdSearchBench(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    /// <summary>
    /// Verifies the Numeric Range: Versus Linear Scan scenario.
    /// </summary>
    [Fact(DisplayName = "Numeric Range: Versus Linear Scan", Skip = "benchmark")]
    public void NumericRange_Versus_LinearScan()
    {
        const int docCount = 1_000_000;
        const int warmup = 3;
        const int iterations = 10;

        var dir = Path.Combine(_fixture.Path, nameof(NumericRange_Versus_LinearScan));
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);
        var mmap = new MMapDirectory(dir);

        using (var writer = new IndexWriter(mmap, new IndexWriterConfig { MaxBufferedDocs = 50_000 }))
        {
            var rng = new Random(17);
            for (int i = 0; i < docCount; i++)
            {
                var d = new LeanDocument();
                d.Add(new NumericField("rank", rng.Next(0, 10_000_000)));
                writer.AddDocument(d);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(mmap);
        var tightQuery = new RangeQuery("rank", 1_000_000, 2_000_000);
        var openQuery = new RangeQuery("rank", long.MinValue, long.MaxValue);

        for (int i = 0; i < warmup; i++)
        {
            _ = searcher.Search(tightQuery, int.MaxValue);
            _ = searcher.Search(openQuery, int.MaxValue);
        }

        var sw = Stopwatch.StartNew();
        int hits = 0;
        for (int i = 0; i < iterations; i++)
            hits = searcher.Search(tightQuery, int.MaxValue).TotalHits;
        sw.Stop();
        double tightMs = sw.Elapsed.TotalMilliseconds / iterations;

        sw.Restart();
        int openHits = 0;
        for (int i = 0; i < iterations; i++)
            openHits = searcher.Search(openQuery, int.MaxValue).TotalHits;
        sw.Stop();
        double openMs = sw.Elapsed.TotalMilliseconds / iterations;

        _output.WriteLine($"docs={docCount:N0} tightHits={hits:N0} openHits={openHits:N0}");
        _output.WriteLine($"tight 10% range : {tightMs:F2} ms/query");
        _output.WriteLine($"open full range : {openMs:F2} ms/query");
        _output.WriteLine($"pruning factor  : {openMs / Math.Max(tightMs, 0.0001):F1}x");

        Assert.True(hits > 0);
        Assert.True(openHits >= hits);
    }
}
