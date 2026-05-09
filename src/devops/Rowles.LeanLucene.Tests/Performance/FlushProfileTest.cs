using System.Diagnostics;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Search.Queries;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;
using Xunit.Abstractions;

namespace Rowles.LeanLucene.Tests.Performance;

/// <summary>
/// Contains unit tests for Flush Profile.
/// </summary>
public class FlushProfileTest(ITestOutputHelper output) : IDisposable
{
    private readonly List<string> _paths = [];

    public void Dispose()
    {
        foreach (var p in _paths)
            if (Directory.Exists(p)) Directory.Delete(p, true);
    }

    private (TimeSpan addTime, TimeSpan commitTime, int termCount) IndexNDocs(int n)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-flush-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _paths.Add(path);

        var dir = new MMapDirectory(path);
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = n + 1000,
            RamBufferSizeMB = 1024
        });

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString()));
            doc.Add(new TextField("body",
                $"doc {i} search vector performance benchmark dotnet segment index bm25 retrieval latency throughput memory mapped files"));
            writer.AddDocument(doc);
        }
        var addTime = sw.Elapsed;

        sw.Restart();
        writer.Commit();
        var commitTime = sw.Elapsed;

        // Verify correctness
        var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "search"), 10);
        searcher.Dispose();
        if (results.TotalHits == 0) throw new Exception("No results after indexing!");

        return (addTime, commitTime, n);
    }

    /// <summary>
    /// Verifies the Flush Profile: Measure Add Vs Commit scenario.
    /// </summary>
    /// <param name="n">The n value for the test case.</param>
    [Theory(DisplayName = "Flush Profile: Measure Add Vs Commit")]
    [InlineData(100)]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(10000)]
    public void FlushProfile_MeasureAddVsCommit(int n)
    {
        // Warmup
        IndexNDocs(100);

        var (addTime, commitTime, _) = IndexNDocs(n);
        var total = addTime + commitTime;

        output.WriteLine($"N={n:N0}: Add={addTime.TotalMilliseconds:F1}ms, Commit={commitTime.TotalMilliseconds:F1}ms, Total={total.TotalMilliseconds:F1}ms");
        output.WriteLine($"  Add%={addTime / total * 100:F1}%, Commit%={commitTime / total * 100:F1}%");
    }
}
