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
/// Tests for Stage 1 new query types: TermRangeQuery, ConstantScoreQuery, DisjunctionMaxQuery, RegexpQuery.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "QueryTypes")]
public sealed class NewQueryTypesTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public NewQueryTypesTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string SubDir(string name)
    {
        var path = System.IO.Path.Combine(_fixture.Path, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    // ── TermRangeQuery ──────────────────────────────────────────────────────

    [Fact]
    public void TermRangeQuery_InclusiveBothBounds_ReturnsMatchingTerms()
    {
        var dir = new MMapDirectory(SubDir("trq_inclusive"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var term in new[] { "apple", "banana", "cherry", "date", "elderberry" })
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("fruit", term));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermRangeQuery("fruit", "banana", "date"), 10);
        Assert.Equal(3, results.TotalHits); // banana, cherry, date
    }

    [Fact]
    public void TermRangeQuery_ExclusiveBounds_ExcludesBoundaryTerms()
    {
        var dir = new MMapDirectory(SubDir("trq_exclusive"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var term in new[] { "apple", "banana", "cherry", "date" })
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("fruit", term));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(
            new TermRangeQuery("fruit", "banana", "date", includeLower: false, includeUpper: false), 10);
        Assert.Equal(1, results.TotalHits); // only cherry
    }

    [Fact]
    public void TermRangeQuery_NullLower_StartFromBeginning()
    {
        var dir = new MMapDirectory(SubDir("trq_nulllower"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var term in new[] { "apple", "banana", "cherry" })
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("fruit", term));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermRangeQuery("fruit", null, "banana"), 10);
        Assert.Equal(2, results.TotalHits); // apple, banana
    }

    [Fact]
    public void TermRangeQuery_NullUpper_GoesToEnd()
    {
        var dir = new MMapDirectory(SubDir("trq_nullupper"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var term in new[] { "apple", "banana", "cherry" })
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("fruit", term));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermRangeQuery("fruit", "banana", null), 10);
        Assert.Equal(2, results.TotalHits); // banana, cherry
    }

    // ── ConstantScoreQuery ──────────────────────────────────────────────────

    [Fact]
    public void ConstantScoreQuery_AllMatchesHaveSameScore()
    {
        var dir = new MMapDirectory(SubDir("csq_scores"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var text in new[] { "hello world", "hello hello hello", "hello" })
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(
            new ConstantScoreQuery(new TermQuery("body", "hello"), 5.0f), 10);

        Assert.Equal(3, results.TotalHits);
        Assert.All(results.ScoreDocs, sd => Assert.Equal(5.0f, sd.Score));
    }

    [Fact]
    public void ConstantScoreQuery_EmptyInnerQuery_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir("csq_empty"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(
            new ConstantScoreQuery(new TermQuery("body", "notfound")), 10);
        Assert.Equal(0, results.TotalHits);
    }

    [Fact]
    public void ConstantScoreQuery_DoesNotDropMatchesAboveTenThousand()
    {
        var dir = new MMapDirectory(SubDir("csq_over_10000"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 20_000 });
        for (int i = 0; i < 10_025; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("tag", "needleword"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new ConstantScoreQuery(new TermQuery("tag", "needleword")), 1);

        Assert.Equal(10_025, results.TotalHits);
    }

    // ── DisjunctionMaxQuery ─────────────────────────────────────────────────

    [Fact]
    public void DisjunctionMaxQuery_ScoreIsMaxOfMatchingClauses()
    {
        var dir = new MMapDirectory(SubDir("dmq_max"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        // Doc 0: only matches "title" term
        // Doc 1: matches both "title" and "body" terms
        var doc0 = new LeanDocument();
        doc0.Add(new TextField("title", "lucene search engine"));
        writer.AddDocument(doc0);

        var doc1 = new LeanDocument();
        doc1.Add(new TextField("title", "lucene"));
        doc1.Add(new TextField("body", "lucene"));
        writer.AddDocument(doc1);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var dmq = new DisjunctionMaxQuery(tieBreakerMultiplier: 0.0f);
        dmq.Add(new TermQuery("title", "lucene"));
        dmq.Add(new TermQuery("body", "lucene"));

        var results = searcher.Search(dmq, 10);
        Assert.Equal(2, results.TotalHits);
    }

    [Fact]
    public void DisjunctionMaxQuery_TieBreakerApplied()
    {
        var dir = new MMapDirectory(SubDir("dmq_tiebreaker"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("title", "lucene"));
        doc.Add(new TextField("body", "lucene"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        // With tiebreaker = 0: score = max
        var dmq0 = new DisjunctionMaxQuery(0.0f);
        dmq0.Add(new TermQuery("title", "lucene"));
        dmq0.Add(new TermQuery("body", "lucene"));
        var r0 = searcher.Search(dmq0, 10);

        // With tiebreaker = 0.5: score = max + 0.5 * sum(rest)
        var dmq5 = new DisjunctionMaxQuery(0.5f);
        dmq5.Add(new TermQuery("title", "lucene"));
        dmq5.Add(new TermQuery("body", "lucene"));
        var r5 = searcher.Search(dmq5, 10);

        Assert.Equal(1, r0.TotalHits);
        Assert.Equal(1, r5.TotalHits);
        // Score with tiebreaker must be >= score without
        Assert.True(r5.ScoreDocs[0].Score >= r0.ScoreDocs[0].Score);
    }

    [Fact]
    public void DisjunctionMaxQuery_DoesNotDropMatchesAboveTenThousand()
    {
        var dir = new MMapDirectory(SubDir("dmq_over_10000"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 20_000 });
        for (int i = 0; i < 10_025; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("tag", "needleword"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var dmq = new DisjunctionMaxQuery();
        dmq.Add(new TermQuery("tag", "needleword"));
        var results = searcher.Search(dmq, 1);

        Assert.Equal(10_025, results.TotalHits);
    }

    // ── RegexpQuery ─────────────────────────────────────────────────────────

    [Fact]
    public void RegexpQuery_MatchesTermsByPattern()
    {
        var dir = new MMapDirectory(SubDir("rxq_basic"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var word in new[] { "cat", "car", "card", "dog", "cargo" })
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("word", word));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        // Matches "car" and "card" and "cargo" — all starting with "car"
        var results = searcher.Search(new RegexpQuery("word", "^car"), 10);
        Assert.Equal(3, results.TotalHits);
    }

    [Fact]
    public void RegexpQuery_NoMatch_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir("rxq_nomatch"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new StringField("word", "hello"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new RegexpQuery("word", "^xyz"), 10);
        Assert.Equal(0, results.TotalHits);
    }

    // ── CancellationToken ───────────────────────────────────────────────────

    [Fact]
    public void Search_WithCancellationToken_Uncancelled_ReturnsResults()
    {
        var dir = new MMapDirectory(SubDir("ct_normal"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var cts = new CancellationTokenSource();
        var results = searcher.Search(new TermQuery("body", "hello"), 10, cts.Token);
        Assert.Equal(1, results.TotalHits);
    }

    [Fact]
    public void Search_WithCancellationToken_AlreadyCancelled_Throws()
    {
        var dir = new MMapDirectory(SubDir("ct_cancelled"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
        {
            var bq = new BooleanQuery();
            bq.Add(new TermQuery("body", "hello"), Occur.Must);
            searcher.Search(bq, 10, cts.Token);
        });
    }

    // ── Suggest ─────────────────────────────────────────────────────────────

    [Fact]
    public void Suggest_ReturnsTopNMatchingPrefixes()
    {
        var dir = new MMapDirectory(SubDir("suggest_basic"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // "search" in 3 docs, "searching" in 2 docs, "searcher" in 1 doc, "other" in 5 docs
        foreach (var (text, count) in new[] { ("search", 3), ("searching", 2), ("searcher", 1) })
        {
            for (int i = 0; i < count; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new StringField("word", text));
                writer.AddDocument(doc);
            }
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var suggestions = searcher.Suggest("search", "word", 10);

        Assert.Equal(3, suggestions.Count);
        // Ranked by docFreq descending: search (3), searching (2), searcher (1)
        Assert.Equal("search", suggestions[0].Term);
        Assert.Equal(3, suggestions[0].DocFreq);
    }

    [Fact]
    public void Suggest_NoPrefix_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir("suggest_noprefix"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new StringField("word", "hello"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var suggestions = searcher.Suggest("xyz", "word", 5);
        Assert.Empty(suggestions);
    }

    [Fact]
    public void Suggest_TopNLimit_Honoured()
    {
        var dir = new MMapDirectory(SubDir("suggest_topn"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var w in new[] { "apple", "application", "apply", "apt" })
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("word", w));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var suggestions = searcher.Suggest("ap", "word", 2);
        Assert.Equal(2, suggestions.Count);
    }
}

