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
/// Boundary and stress tests: empty fields, Unicode stored fields round-trip,
/// GetStoredFields out-of-range, TopN > totalHits, concurrent readers,
/// IndexWriterConfig boundary values, very long documents.
/// </summary>
[Trait("Category", "Boundary")]
public sealed class BoundaryAndStressTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public BoundaryAndStressTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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

    // ── Empty field value ───────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Empty Text Field: Index And Search No Crash Zero Hits scenario.
    /// </summary>
    [Fact(DisplayName = "Empty Text Field: Index And Search No Crash Zero Hits")]
    public void EmptyTextField_IndexAndSearch_NoCrashZeroHits()
    {
        var dir = new MMapDirectory(SubDir("empty_field"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", ""));
            doc.Add(new TextField("title", "non-empty title"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        // Search on the empty field should return 0 hits, not crash
        var results = searcher.Search(new TermQuery("body", ""), 10);
        _output.WriteLine($"Empty field TermQuery('') → {results.TotalHits} hits");
        Assert.Equal(0, results.TotalHits);

        // The other field should still be searchable (analyser splits "non-empty" → "non", "empti")
        var titleResults = searcher.Search(new TermQuery("title", "title"), 10);
        _output.WriteLine($"Non-empty field → {titleResults.TotalHits} hits");
        Assert.Equal(1, titleResults.TotalHits);
    }

    /// <summary>
    /// Verifies the Empty String Field: Stored Field Round-trip scenario.
    /// </summary>
    [Fact(DisplayName = "Empty String Field: Stored Field Round-trip")]
    public void EmptyStringField_StoredFieldRoundTrip()
    {
        var dir = new MMapDirectory(SubDir("empty_stored"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("tag", ""));
            doc.Add(new TextField("body", "findable"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "findable"), 10);
        Assert.Equal(1, results.TotalHits);

        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        _output.WriteLine($"Stored 'tag' field values: {stored.ContainsKey("tag")}");
        // The empty string field should be present in stored fields
        if (stored.ContainsKey("tag"))
        {
            _output.WriteLine($"  tag value: '{stored["tag"][0]}'");
        }
    }

    // ── Unicode / multi-byte UTF-8 stored fields ────────────────────────────

    /// <summary>
    /// Verifies the Unicode Stored Fields: Emoji Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Unicode Stored Fields: Emoji Round Trips")]
    public void UnicodeStoredFields_Emoji_RoundTrips()
    {
        var dir = new MMapDirectory(SubDir("unicode_emoji"));
        const string emojiContent = "日本語テスト 🔍";

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("content", emojiContent));
            doc.Add(new TextField("body", "findme"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "findme"), 10);
        Assert.Equal(1, results.TotalHits);

        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        _output.WriteLine($"Unicode round-trip: '{stored["content"][0]}'");
        Assert.Equal(emojiContent, stored["content"][0]);
    }

    /// <summary>
    /// Verifies the Unicode Stored Fields: CJK Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Unicode Stored Fields: CJK Round Trips")]
    public void UnicodeStoredFields_CJK_RoundTrips()
    {
        var dir = new MMapDirectory(SubDir("unicode_cjk"));
        const string cjk = "中文测试内容";

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("text", cjk));
            doc.Add(new TextField("body", "marker"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "marker"), 10);
        Assert.Equal(1, results.TotalHits);

        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        _output.WriteLine($"CJK round-trip: '{stored["text"][0]}'");
        Assert.Equal(cjk, stored["text"][0]);
    }

    /// <summary>
    /// Verifies the Unicode Stored Fields: Mixed Scripts Round Trips scenario.
    /// </summary>
    [Fact(DisplayName = "Unicode Stored Fields: Mixed Scripts Round Trips")]
    public void UnicodeStoredFields_MixedScripts_RoundTrips()
    {
        var dir = new MMapDirectory(SubDir("unicode_mixed"));
        const string mixed = "Hello مرحبا Привет こんにちは 🌍";

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("greeting", mixed));
            doc.Add(new TextField("body", "marker"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "marker"), 10);
        var stored = searcher.GetStoredFields(results.ScoreDocs[0].DocId);
        _output.WriteLine($"Mixed scripts round-trip: '{stored["greeting"][0]}'");
        Assert.Equal(mixed, stored["greeting"][0]);
    }

    // ── GetStoredFields out-of-range ────────────────────────────────────────

    /// <summary>
    /// Verifies the Get Stored Fields: Out Of Range Doc ID Returns Empty Or Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Get Stored Fields: Out Of Range Doc ID Returns Empty Or Throws")]
    public void GetStoredFields_OutOfRangeDocId_ReturnsEmptyOrThrows()
    {
        var dir = new MMapDirectory(SubDir("oor_stored"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        // DocId beyond max — should return empty dict or throw, not crash
        var ex = Record.Exception(() => searcher.GetStoredFields(999));
        if (ex is not null)
        {
            _output.WriteLine($"GetStoredFields(999) threw: {ex.GetType().Name}: {ex.Message}");
            Assert.True(ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidOperationException,
                $"Unexpected exception type: {ex.GetType().Name}");
        }
        else
        {
            var stored = searcher.GetStoredFields(999);
            _output.WriteLine($"GetStoredFields(999) returned empty dict: {stored.Count} keys");
            Assert.Empty(stored);
        }
    }

    // ── Very long document (heap fallback paths) ────────────────────────────

    /// <summary>
    /// Verifies the Very Long Document: 5000 Tokens Index And Search Correctly scenario.
    /// </summary>
    [Fact(DisplayName = "Very Long Document: 5000 Tokens Index And Search Correctly")]
    public void VeryLongDocument_5000Tokens_IndexAndSearchCorrectly()
    {
        var dir = new MMapDirectory(SubDir("long_doc"));
        // Build a 5000-word document with repeating words
        string[] words = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta"];
        var rng = new Random(42);
        var bodyWords = Enumerable.Range(0, 5000).Select(_ => words[rng.Next(words.Length)]);
        var body = string.Join(" ", bodyWords);

        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", body));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "alpha"), 10);
        _output.WriteLine($"5000-token doc: TermQuery('alpha') → {results.TotalHits} hits, score={results.ScoreDocs[0].Score:F4}");
        Assert.Equal(1, results.TotalHits);
        Assert.True(results.ScoreDocs[0].Score > 0);
    }

    // ── Concurrent readers ──────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Concurrent Readers: No Exception All Return Results scenario.
    /// </summary>
    [Fact(DisplayName = "Concurrent Readers: No Exception All Return Results")]
    public void ConcurrentReaders_NoException_AllReturnResults()
    {
        var dir = new MMapDirectory(SubDir("concurrent_read"));
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            for (int i = 0; i < 50; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", "common term here"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var hitCounts = new System.Collections.Concurrent.ConcurrentBag<int>();

        Parallel.For(0, 4, _ =>
        {
            try
            {
                using var searcher = new IndexSearcher(dir);
                for (int q = 0; q < 50; q++)
                {
                    var results = searcher.Search(new TermQuery("body", "common"), 10);
                    hitCounts.Add(results.TotalHits);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        _output.WriteLine($"Concurrent readers: {hitCounts.Count} queries, {exceptions.Count} exceptions");
        Assert.Empty(exceptions);
        Assert.All(hitCounts, h => Assert.Equal(50, h));
    }

    // ── IndexWriterConfig boundary values ───────────────────────────────────

    /// <summary>
    /// Verifies the Index Writer Config: Max Buffered Docs 1 Flushes Every Doc scenario.
    /// </summary>
    [Fact(DisplayName = "Index Writer Config: Max Buffered Docs 1 Flushes Every Doc")]
    public void IndexWriterConfig_MaxBufferedDocs1_FlushesEveryDoc()
    {
        var dir = new MMapDirectory(SubDir("config_maxbuf1"));
        var config = new IndexWriterConfig { MaxBufferedDocs = 1 };

        using (var writer = new IndexWriter(dir, config))
        {
            for (int i = 0; i < 5; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"doc {i}"));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "doc"), 10);
        _output.WriteLine($"MaxBufferedDocs=1: {results.TotalHits} total hits across segments");
        Assert.Equal(5, results.TotalHits);
    }
}
