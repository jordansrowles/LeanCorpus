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
using Rowles.LeanLucene.Search.Suggestions;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Performance;

/// <summary>
/// Contains unit tests for Allocation Profile.
/// </summary>
[Trait("Category", "Performance")]
public class AllocationProfileTest : IDisposable
{
    private readonly string _path;
    private readonly MMapDirectory _dir;
    private readonly IndexSearcher _searcher;
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public AllocationProfileTest(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        _path = Path.Combine(Path.GetTempPath(), $"leanlucene-alloc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_path);
        _dir = new MMapDirectory(_path);

        using var writer = new IndexWriter(_dir, new IndexWriterConfig { MaxBufferedDocs = 10000, RamBufferSizeMB = 256 });
        for (int i = 0; i < 10_000; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("id", i.ToString()));
            var kw = (i % 3) switch { 0 => "search", 1 => "vector", _ => "performance" };
            doc.Add(new TextField("body", $"doc {i} {kw} benchmark dotnet segment index bm25 retrieval latency throughput memory mapped files"));
            writer.AddDocument(doc);
        }
        writer.Commit();
        _searcher = new IndexSearcher(_dir);
    }

    public void Dispose()
    {
        _searcher.Dispose();
        if (Directory.Exists(_path)) Directory.Delete(_path, true);
    }

    /// <summary>
    /// Verifies the Term Query: Allocation Per Query Under 5 KB scenario.
    /// </summary>
    [Fact(DisplayName = "Term Query: Allocation Per Query Under 5 KB")]
    public void TermQuery_AllocationPerQuery_Under5KB()
    {
        for (int w = 0; w < 50; w++) _searcher.Search(new TermQuery("body", "search"), 25);

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        long before = GC.GetAllocatedBytesForCurrentThread();

        const int iterations = 1000;
        for (int q = 0; q < iterations; q++)
            _searcher.Search(new TermQuery("body", "search"), 25);

        long totalAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
        long perQuery = totalAlloc / iterations;

        _output.WriteLine($"TermQuery: {perQuery} B/query, total {totalAlloc / 1024.0:F1} KB for {iterations} queries");
        Assert.True(perQuery < 5120, $"TermQuery allocates {perQuery} B/query (expected < 5120)");
    }

    /// <summary>
    /// Verifies the Boolean Query: Allocation Per Query Under 20 KB scenario.
    /// </summary>
    [Fact(DisplayName = "Boolean Query: Allocation Per Query Under 20 KB")]
    public void BooleanQuery_AllocationPerQuery_Under20KB()
    {
        var warmBq = new BooleanQuery();
        warmBq.Add(new TermQuery("body", "search"), Occur.Must);
        warmBq.Add(new TermQuery("body", "benchmark"), Occur.Must);
        for (int w = 0; w < 50; w++)
            _searcher.Search(warmBq, 25);

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        long before = GC.GetAllocatedBytesForCurrentThread();

        const int iterations = 1000;
        for (int q = 0; q < iterations; q++)
        {
            var bq = new BooleanQuery();
            bq.Add(new TermQuery("body", "search"), Occur.Must);
            bq.Add(new TermQuery("body", "benchmark"), Occur.Must);
            _searcher.Search(bq, 25);
        }

        long totalAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
        long perQuery = totalAlloc / iterations;

        _output.WriteLine($"BooleanQuery: {perQuery} B/query, total {totalAlloc / 1024.0:F1} KB for {iterations} queries");
        Assert.True(perQuery < 20480, $"BooleanQuery allocates {perQuery} B/query (expected < 20480)");
    }

    /// <summary>
    /// Verifies the Phrase Query: Allocation Per Query Under 20 KB scenario.
    /// </summary>
    [Fact(DisplayName = "Phrase Query: Allocation Per Query Under 20 KB")]
    public void PhraseQuery_AllocationPerQuery_Under20KB()
    {
        for (int w = 0; w < 50; w++) _searcher.Search(new PhraseQuery("body", "dotnet", "segment"), 25);

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        long before = GC.GetAllocatedBytesForCurrentThread();

        const int iterations = 1000;
        for (int q = 0; q < iterations; q++)
            _searcher.Search(new PhraseQuery("body", "dotnet", "segment"), 25);

        long totalAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
        long perQuery = totalAlloc / iterations;

        _output.WriteLine($"PhraseQuery: {perQuery} B/query, total {totalAlloc / 1024.0:F1} KB for {iterations} queries");
        Assert.True(perQuery < 20480, $"PhraseQuery allocates {perQuery} B/query (expected < 20480)");
    }

    /// <summary>
    /// Verifies the Wildcard Query: Allocation Per Query Under 100 KB scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Query: Allocation Per Query Under 100 KB")]
    public void WildcardQuery_AllocationPerQuery_Under100KB()
    {
        for (int w = 0; w < 50; w++) _searcher.Search(new WildcardQuery("body", "search*"), 25);

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        long before = GC.GetAllocatedBytesForCurrentThread();

        const int iterations = 1000;
        for (int q = 0; q < iterations; q++)
            _searcher.Search(new WildcardQuery("body", "search*"), 25);

        long totalAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
        long perQuery = totalAlloc / iterations;

        _output.WriteLine($"WildcardQuery: {perQuery} B/query, total {totalAlloc / 1024.0:F1} KB for {iterations} queries");
        Assert.True(perQuery < 102400, $"WildcardQuery allocates {perQuery} B/query (expected < 100KB)");
    }

    /// <summary>
    /// Verifies the Fuzzy Query: Allocation Per Query Under 100 KB scenario.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Query: Allocation Per Query Under 100 KB")]
    public void FuzzyQuery_AllocationPerQuery_Under100KB()
    {
        for (int w = 0; w < 50; w++) _searcher.Search(new FuzzyQuery("body", "serch"), 25);

        GC.Collect(2, GCCollectionMode.Forced, true, true);
        long before = GC.GetAllocatedBytesForCurrentThread();

        const int iterations = 1000;
        for (int q = 0; q < iterations; q++)
            _searcher.Search(new FuzzyQuery("body", "serch"), 25);

        long totalAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
        long perQuery = totalAlloc / iterations;

        _output.WriteLine($"FuzzyQuery: {perQuery} B/query, total {totalAlloc / 1024.0:F1} KB for {iterations} queries");
        Assert.True(perQuery < 102400, $"FuzzyQuery allocates {perQuery} B/query (expected < 100KB)");
    }

    /// <summary>
    /// Verifies the Indexing 10 K: Allocation Under 50 MB scenario.
    /// </summary>
    [Fact(DisplayName = "Indexing 10 K: Allocation Under 50 MB")]
    public void Indexing10K_AllocationUnder50MB()
    {
        var testPath = Path.Combine(Path.GetTempPath(), $"leanlucene-idx-alloc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(testPath);

        try
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            long before = GC.GetAllocatedBytesForCurrentThread();

            var testDir = new MMapDirectory(testPath);
            using var writer = new IndexWriter(testDir, new IndexWriterConfig { MaxBufferedDocs = 10000, RamBufferSizeMB = 256 });
            for (int i = 0; i < 10_000; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("id", i.ToString()));
                doc.Add(new TextField("body", $"doc {i} search benchmark dotnet segment index bm25"));
                writer.AddDocument(doc);
            }
            writer.Commit();

            long totalAlloc = GC.GetAllocatedBytesForCurrentThread() - before;
            double mb = totalAlloc / 1024.0 / 1024.0;

            _output.WriteLine($"Indexing 10K: {mb:F1} MB total allocation");
            Assert.True(mb < 50, $"Indexing 10K docs allocates {mb:F1} MB (expected < 50 MB)");
        }
        finally
        {
            if (Directory.Exists(testPath)) Directory.Delete(testPath, true);
        }
    }
}
