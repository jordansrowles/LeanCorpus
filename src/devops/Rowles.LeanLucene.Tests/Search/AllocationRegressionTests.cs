using System.Diagnostics;
using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
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
/// Allocation regression guards for zero-alloc hot paths.
/// These tests verify that heavily-optimised paths (streaming BooleanQuery,
/// ArrayPool phrase positions, intern cache) maintain low allocation budgets.
/// </summary>
[Trait("Category", "Perf")]
[Trait("Category", "AllocationRegression")]
public sealed class AllocationRegressionTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AllocationRegressionTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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
    /// Verifies the Boolean Query: Must Streaming Path Low Allocation Per Query scenario.
    /// </summary>
    [Fact(DisplayName = "Boolean Query: Must Streaming Path Low Allocation Per Query")]
    public void BooleanQuery_Must_StreamingPath_LowAllocationPerQuery()
    {
        // Arrange: build a 500-doc index where ~half contain both target terms
        const int docCount = 500;
        const int warmup = 200;
        const int measured = 100;
        var dir = new MMapDirectory(SubDir("alloc_bool_must"));
        var rng = new Random(42);
        string[] pool = ["alpha", "beta", "gamma", "delta", "epsilon"];

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 256 }))
        {
            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                var words = Enumerable.Range(0, 8).Select(_ => pool[rng.Next(pool.Length)]);
                doc.Add(new TextField("body", string.Join(" ", words)));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "alpha"), Occur.Must);
        query.Add(new TermQuery("body", "beta"), Occur.Must);

        // Warmup
        for (int i = 0; i < warmup; i++)
            searcher.Search(query, 25);

        // Measure
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measured; i++)
            searcher.Search(query, 25);
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / measured;
        double avgUs = sw.Elapsed.TotalMicroseconds / measured;

        _output.WriteLine($"BooleanQuery Must(2 terms) over {docCount} docs:");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/query");
        _output.WriteLine($"  Avg latency:    {avgUs:F1} µs/query");
        _output.WriteLine($"  Total hits:     {searcher.Search(query, 25).TotalHits}");

        // Budget: ≤ 16 KB per query — v3 block codec uses constant-size block buffers
        Assert.True(avgBytes <= 16384,
            $"BooleanQuery Must streaming path allocated {avgBytes:F0} bytes/query, budget is 16384 bytes");
    }

    /// <summary>
    /// Verifies the Boolean Query: Should Only Streaming Path Low Allocation Per Query scenario.
    /// </summary>
    [Fact(DisplayName = "Boolean Query: Should Only Streaming Path Low Allocation Per Query")]
    public void BooleanQuery_ShouldOnly_StreamingPath_LowAllocationPerQuery()
    {
        // Arrange
        const int docCount = 500;
        const int warmup = 200;
        const int measured = 100;
        var dir = new MMapDirectory(SubDir("alloc_bool_should"));
        var rng = new Random(42);
        string[] pool = ["red", "green", "blue", "yellow", "orange"];

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 256 }))
        {
            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                var words = Enumerable.Range(0, 8).Select(_ => pool[rng.Next(pool.Length)]);
                doc.Add(new TextField("body", string.Join(" ", words)));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new BooleanQuery();
        query.Add(new TermQuery("body", "red"), Occur.Should);
        query.Add(new TermQuery("body", "green"), Occur.Should);
        query.Add(new TermQuery("body", "blue"), Occur.Should);

        // Warmup
        for (int i = 0; i < warmup; i++)
            searcher.Search(query, 25);

        // Measure
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measured; i++)
            searcher.Search(query, 25);
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / measured;
        double avgUs = sw.Elapsed.TotalMicroseconds / measured;

        _output.WriteLine($"BooleanQuery Should(3 terms) over {docCount} docs:");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/query");
        _output.WriteLine($"  Avg latency:    {avgUs:F1} µs/query");
        _output.WriteLine($"  Total hits:     {searcher.Search(query, 25).TotalHits}");

        // Budget: ≤ 16 KB per query — v3 block codec uses constant-size block buffers
        Assert.True(avgBytes <= 16384,
            $"BooleanQuery Should streaming path allocated {avgBytes:F0} bytes/query, budget is 16384 bytes");
    }

    /// <summary>
    /// Verifies the Phrase Query: Array Pool Positions Low Allocation Per Query scenario.
    /// </summary>
    [Fact(DisplayName = "Phrase Query: Array Pool Positions Low Allocation Per Query")]
    public void PhraseQuery_ArrayPoolPositions_LowAllocationPerQuery()
    {
        // Arrange
        const int docCount = 500;
        const int warmup = 200;
        const int measured = 100;
        var dir = new MMapDirectory(SubDir("alloc_phrase"));
        var rng = new Random(42);
        string[] pool = ["quick", "brown", "fox", "jumps", "over", "lazy", "dog"];

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 256 }))
        {
            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                var words = Enumerable.Range(0, 10).Select(_ => pool[rng.Next(pool.Length)]);
                doc.Add(new TextField("body", string.Join(" ", words)));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new PhraseQuery("body", "quick", "brown");

        // Warmup
        for (int i = 0; i < warmup; i++)
            searcher.Search(query, 25);

        // Measure
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measured; i++)
            searcher.Search(query, 25);
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / measured;
        double avgUs = sw.Elapsed.TotalMicroseconds / measured;

        _output.WriteLine($"PhraseQuery(\"quick\",\"brown\") over {docCount} docs:");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/query");
        _output.WriteLine($"  Avg latency:    {avgUs:F1} µs/query");
        _output.WriteLine($"  Total hits:     {searcher.Search(query, 25).TotalHits}");

        // Budget: ≤ 16 KB per query — v3 block codec uses constant-size block buffers
        Assert.True(avgBytes <= 16384,
            $"PhraseQuery allocated {avgBytes:F0} bytes/query, budget is 16384 bytes");
    }

    /// <summary>
    /// Verifies the Wildcard Query: Low Allocation Per Query scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Query: Low Allocation Per Query")]
    public void WildcardQuery_LowAllocationPerQuery()
    {
        // Arrange: build a 500-doc index with diverse terms
        const int docCount = 500;
        const int warmup = 200;
        const int measured = 100;
        var dir = new MMapDirectory(SubDir("alloc_wildcard"));
        var rng = new Random(42);
        string[] pool = ["benchmark", "benchpress", "benchtop", "searchable", "searching",
                         "searcher", "alpha", "beta", "gamma", "delta", "epsilon"];

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 256 }))
        {
            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                var words = Enumerable.Range(0, 8).Select(_ => pool[rng.Next(pool.Length)]);
                doc.Add(new TextField("body", string.Join(" ", words)));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new WildcardQuery("body", "bench*");

        // Warmup
        for (int i = 0; i < warmup; i++)
            searcher.Search(query, 25);

        // Measure
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measured; i++)
            searcher.Search(query, 25);
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / measured;
        double avgUs = sw.Elapsed.TotalMicroseconds / measured;

        _output.WriteLine($"WildcardQuery(\"bench*\") over {docCount} docs:");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/query");
        _output.WriteLine($"  Avg latency:    {avgUs:F1} µs/query");
        _output.WriteLine($"  Total hits:     {searcher.Search(query, 25).TotalHits}");

        // Budget: ≤ 50 KB per query — regression guard (was 402 KB, target <15 KB)
        Assert.True(avgBytes <= 51200,
            $"WildcardQuery allocated {avgBytes:F0} bytes/query, budget is 51200 bytes");
    }

    /// <summary>
    /// Verifies the Wildcard Query: Mid Pattern Short Prefix Low Allocation Per Query scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Query: Mid Pattern Short Prefix Low Allocation Per Query")]
    public void WildcardQuery_MidPatternShortPrefix_LowAllocationPerQuery()
    {
        const int nonMatchingTerms = 2_000;
        const int warmup = 100;
        const int measured = 50;
        var dir = new MMapDirectory(SubDir("alloc_wildcard_mid_prefix"));

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 512 }))
        {
            for (int i = 0; i < nonMatchingTerms; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"mismatch{i:D5}"));
                writer.AddDocument(doc);
            }

            for (int i = 0; i < 20; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", "market"));
                writer.AddDocument(doc);
            }

            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new WildcardQuery("body", "m*rket");

        for (int i = 0; i < warmup; i++)
            searcher.Search(query, 25);

        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measured; i++)
            searcher.Search(query, 25);
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / measured;
        double avgUs = sw.Elapsed.TotalMicroseconds / measured;

        _output.WriteLine($"WildcardQuery(\"m*rket\") over {nonMatchingTerms + 20} docs:");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/query");
        _output.WriteLine($"  Avg latency:    {avgUs:F1} µs/query");
        _output.WriteLine($"  Total hits:     {searcher.Search(query, 25).TotalHits}");

        Assert.True(avgBytes <= 102400,
            $"WildcardQuery allocated {avgBytes:F0} bytes/query, budget is 102400 bytes");
    }

    /// <summary>
    /// Verifies the Fuzzy Query: Low Allocation Per Query scenario.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Query: Low Allocation Per Query")]
    public void FuzzyQuery_LowAllocationPerQuery()
    {
        // Arrange: build a 500-doc index with diverse terms
        const int docCount = 500;
        const int warmup = 200;
        const int measured = 100;
        var dir = new MMapDirectory(SubDir("alloc_fuzzy"));
        var rng = new Random(42);
        string[] pool = ["benchmark", "benchpress", "benchtop", "searchable", "searching",
                         "searcher", "alpha", "beta", "gamma", "delta", "epsilon"];

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 256 }))
        {
            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                var words = Enumerable.Range(0, 8).Select(_ => pool[rng.Next(pool.Length)]);
                doc.Add(new TextField("body", string.Join(" ", words)));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new FuzzyQuery("body", "benchmork", maxEdits: 2);

        // Warmup
        for (int i = 0; i < warmup; i++)
            searcher.Search(query, 25);

        // Measure
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measured; i++)
            searcher.Search(query, 25);
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / measured;
        double avgUs = sw.Elapsed.TotalMicroseconds / measured;

        _output.WriteLine($"FuzzyQuery(\"benchmork\", maxEdits=2) over {docCount} docs:");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/query");
        _output.WriteLine($"  Avg latency:    {avgUs:F1} µs/query");
        _output.WriteLine($"  Total hits:     {searcher.Search(query, 25).TotalHits}");

        // Budget: ≤ 50 KB per query — regression guard (was 402 KB, target <15 KB)
        Assert.True(avgBytes <= 51200,
            $"FuzzyQuery allocated {avgBytes:F0} bytes/query, budget is 51200 bytes");
    }

    /// <summary>
    /// Verifies the Fuzzy Query: High Edit Distance Short Term Low Allocation Per Query scenario.
    /// </summary>
    [Fact(DisplayName = "Fuzzy Query: High Edit Distance Short Term Low Allocation Per Query")]
    public void FuzzyQuery_HighEditDistance_ShortTerm_LowAllocationPerQuery()
    {
        // Regression guard for the 398 KB regression with short query terms and maxEdits=2.
        // Short terms like "serch" (5 chars) with maxEdits=2 pass the byte-length pre-filter
        // for nearly every term, causing excessive DecodeKey allocations.
        const int docCount = 500;
        const int warmup = 200;
        const int measured = 100;
        var dir = new MMapDirectory(SubDir("alloc_fuzzy_high_edit"));
        var rng = new Random(42);
        string[] pool = ["search", "serve", "seven", "select", "simple",
                         "alpha", "beta", "gamma", "delta", "epsilon",
                         "vector", "vertex", "verify", "valid", "value"];

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 256 }))
        {
            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                var words = Enumerable.Range(0, 8).Select(_ => pool[rng.Next(pool.Length)]);
                doc.Add(new TextField("body", string.Join(" ", words)));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query1 = new FuzzyQuery("body", "serch", maxEdits: 2);
        var query2 = new FuzzyQuery("body", "vectr", maxEdits: 2);

        // Warmup
        for (int i = 0; i < warmup; i++)
        {
            searcher.Search(query1, 25);
            searcher.Search(query2, 25);
        }

        // Measure
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measured; i++)
        {
            searcher.Search(query1, 25);
            searcher.Search(query2, 25);
        }
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / (measured * 2);

        _output.WriteLine($"FuzzyQuery high-edit short terms over {docCount} docs:");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/query");

        Assert.True(avgBytes <= 51200,
            $"FuzzyQuery (high-edit short term) allocated {avgBytes:F0} bytes/query, budget is 51200 bytes");
    }

    /// <summary>
    /// Verifies the Standard Analyser: Intern Cache Stable Corpus No New String Allocations scenario.
    /// </summary>
    [Fact(DisplayName = "Standard Analyser: Intern Cache Stable Corpus No New String Allocations")]
    public void StandardAnalyser_InternCache_StableCorpusNoNewStringAllocations()
    {
        // After warmup on a stable corpus, repeated analysis should hit intern cache 100%
        const int warmup = 50;
        const int measured = 100;
        const string input = "the quick brown fox jumps over the lazy dog with some extra padding words here";

        var analyser = new StandardAnalyser();

        // Warmup — populate intern cache
        for (int i = 0; i < warmup; i++)
            analyser.Analyse(input);

        // Measure
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measured; i++)
            analyser.Analyse(input);
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / measured;

        _output.WriteLine($"StandardAnalyser intern cache (same {input.Split(' ').Length}-word input × {measured}):");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/call");

        // After warmup, only the offset buffer reuse and token list operations should allocate.
        // No new string allocations expected (all cached).
        Assert.True(avgBytes <= 256,
            $"StandardAnalyser allocated {avgBytes:F0} bytes/call after warmup, expected ≤ 256 (intern cache should be stable)");
    }

    /// <summary>
    /// Verifies the Term Query: Low Allocation Per Query scenario.
    /// </summary>
    [Fact(DisplayName = "Term Query: Low Allocation Per Query")]
    public void TermQuery_LowAllocationPerQuery()
    {
        const int docCount = 500;
        const int warmup = 200;
        const int measured = 100;
        var dir = new MMapDirectory(SubDir("alloc_term"));
        var rng = new Random(42);
        string[] pool = ["alpha", "beta", "gamma", "delta", "epsilon"];

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 256 }))
        {
            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                var words = Enumerable.Range(0, 8).Select(_ => pool[rng.Next(pool.Length)]);
                doc.Add(new TextField("body", string.Join(" ", words)));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new TermQuery("body", "alpha");

        for (int i = 0; i < warmup; i++)
            searcher.Search(query, 25);

        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measured; i++)
            searcher.Search(query, 25);
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / measured;
        _output.WriteLine($"TermQuery(\"alpha\") over {docCount} docs:");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/query");
        _output.WriteLine($"  Avg latency:    {sw.Elapsed.TotalMicroseconds / measured:F1} µs/query");

        Assert.True(avgBytes <= 4096,
            $"TermQuery allocated {avgBytes:F0} bytes/query, budget is 4096 bytes");
    }

    /// <summary>
    /// Verifies the Block Join Query: Low Allocation Per Query scenario.
    /// </summary>
    [Fact(DisplayName = "Block Join Query: Low Allocation Per Query")]
    public void BlockJoinQuery_LowAllocationPerQuery()
    {
        const int blockCount = 50;
        const int childrenPerBlock = 3;
        const int warmup = 100;
        const int measured = 50;
        var dir = new MMapDirectory(SubDir("alloc_blockjoin"));

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 256 }))
        {
            for (int i = 0; i < blockCount; i++)
            {
                var block = new List<LeanDocument>();
                for (int c = 0; c < childrenPerBlock; c++)
                {
                    var child = new LeanDocument();
                    child.Add(new TextField("body", $"child {c} comment on topic {i}"));
                    child.Add(new StringField("type", "child"));
                    block.Add(child);
                }
                var parent = new LeanDocument();
                parent.Add(new TextField("title", $"parent {i} post"));
                parent.Add(new StringField("type", "parent"));
                block.Add(parent);
                writer.AddDocumentBlock(block);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var query = new BlockJoinQuery(new TermQuery("body", "comment"));

        for (int i = 0; i < warmup; i++)
            searcher.Search(query, 10);

        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measured; i++)
            searcher.Search(query, 10);
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / measured;
        _output.WriteLine($"BlockJoinQuery over {blockCount} blocks:");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/query");
        _output.WriteLine($"  Avg latency:    {sw.Elapsed.TotalMicroseconds / measured:F1} µs/query");

        Assert.True(avgBytes <= 520_000,
            $"BlockJoinQuery allocated {avgBytes:F0} bytes/query, budget is 520,000 bytes");
    }

    /// <summary>
    /// Verifies the Did You Mean: Low Allocation Per Query scenario.
    /// </summary>
    [Fact(DisplayName = "Did You Mean: Low Allocation Per Query")]
    public void DidYouMean_LowAllocationPerQuery()
    {
        const int docCount = 500;
        const int warmup = 50;
        const int measured = 50;
        var dir = new MMapDirectory(SubDir("alloc_dym"));
        var rng = new Random(42);
        string[] pool = ["search", "serve", "seven", "select", "simple",
                         "alpha", "beta", "gamma", "delta", "epsilon"];

        using (var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 256 }))
        {
            for (int i = 0; i < docCount; i++)
            {
                var doc = new LeanDocument();
                var words = Enumerable.Range(0, 8).Select(_ => pool[rng.Next(pool.Length)]);
                doc.Add(new TextField("body", string.Join(" ", words)));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        for (int i = 0; i < warmup; i++)
            Rowles.LeanLucene.Search.Suggestions.DidYouMeanSuggester.Suggest(searcher, "body", "serch", maxEdits: 2, topN: 5);

        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < measured; i++)
            Rowles.LeanLucene.Search.Suggestions.DidYouMeanSuggester.Suggest(searcher, "body", "serch", maxEdits: 2, topN: 5);
        sw.Stop();
        long allocAfter = GC.GetAllocatedBytesForCurrentThread();

        double avgBytes = (double)(allocAfter - allocBefore) / measured;
        _output.WriteLine($"DidYouMean(\"serch\") over {docCount} docs:");
        _output.WriteLine($"  Avg allocation: {avgBytes:F0} bytes/query");
        _output.WriteLine($"  Avg latency:    {sw.Elapsed.TotalMicroseconds / measured:F1} µs/query");

        // Budget: <= 6 KB per query -- SpellIndex is cached after first call;
        // subsequent queries only allocate the overlap array (ArrayPool) and result list.
        Assert.True(avgBytes <= 6_000,
            $"DidYouMean allocated {avgBytes:F0} bytes/query, budget is 6,000 bytes");
    }
}
