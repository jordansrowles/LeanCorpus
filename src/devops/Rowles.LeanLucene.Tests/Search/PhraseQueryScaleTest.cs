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

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for Phrase Query Scale.
/// </summary>
public class PhraseQueryScaleTest(ITestOutputHelper output) : IDisposable
{
    private readonly List<string> _paths = [];

    public void Dispose()
    {
        foreach (var p in _paths)
            if (Directory.Exists(p)) Directory.Delete(p, true);
    }

    /// <summary>
    /// Verifies the Phrase Query: Three Words Large Corpus Returns Results scenario.
    /// </summary>
    /// <param name="docCount">The docCount value for the test case.</param>
    [Theory(DisplayName = "Phrase Query: Three Words Large Corpus Returns Results")]
    [InlineData(1000)]
    [InlineData(10000)]
    [InlineData(50000)]
    public void PhraseQuery_ThreeWords_LargeCorpus_ReturnsResults(int docCount)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-phrase3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _paths.Add(path);

        var dir = new MMapDirectory(path);
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = docCount + 100,
            RamBufferSizeMB = 1024,
            MaxQueuedDocs = 0  // disable backpressure for single-threaded test
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < docCount; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString()));
            doc.Add(new TextField("body", $"prefix{i % 10} alpha beta gamma suffix{i % 7}"));
            writer.AddDocument(doc);
        }
        output.WriteLine($"N={docCount}: AddDocument took {sw.ElapsedMilliseconds}ms");

        sw.Restart();
        writer.Commit();
        output.WriteLine($"N={docCount}: Commit took {sw.ElapsedMilliseconds}ms");

        var searcher = new IndexSearcher(dir);

        sw.Restart();
        var results2 = searcher.Search(new PhraseQuery("body", ["alpha", "beta"]), 10);
        output.WriteLine($"N={docCount}: 2-word phrase hits={results2.TotalHits} took {sw.ElapsedMilliseconds}ms");
        Assert.True(results2.TotalHits > 0);

        sw.Restart();
        var results3 = searcher.Search(new PhraseQuery("body", ["alpha", "beta", "gamma"]), 10);
        output.WriteLine($"N={docCount}: 3-word phrase hits={results3.TotalHits} took {sw.ElapsedMilliseconds}ms");
        Assert.True(results3.TotalHits > 0);
        Assert.Equal(docCount, results3.TotalHits);

        searcher.Dispose();
    }

    /// <summary>
    /// Verifies the Backpressure Flush: Does Not Deadlock scenario.
    /// </summary>
    [Fact(DisplayName = "Backpressure Flush: Does Not Deadlock")]
    public void BackpressureFlush_DoesNotDeadlock()
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-bp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _paths.Add(path);

        var dir = new MMapDirectory(path);
        // MaxQueuedDocs=20K default but MaxBufferedDocs=50K — previously deadlocked
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = 50_000,
            RamBufferSizeMB = 1024
        });

        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 25000; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString()));
            doc.Add(new TextField("body", "alpha beta gamma"));
            writer.AddDocument(doc);
        }
        writer.Commit();
        output.WriteLine($"25K docs with backpressure flush: {sw.ElapsedMilliseconds}ms");

        var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PhraseQuery("body", ["alpha", "beta", "gamma"]), 10);
        output.WriteLine($"TotalHits={results.TotalHits}");
        Assert.True(results.TotalHits > 0);
        searcher.Dispose();
    }
}
