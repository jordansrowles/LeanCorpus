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
/// Tests for edge cases and correctness gaps across query types:
/// ConstantScoreQuery wrapping, DisjunctionMaxQuery with deletes,
/// SpanNearQuery out-of-order, WildcardQuery leading wildcard,
/// RegexpQuery complex patterns, FunctionScoreQuery missing numeric field.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "EdgeCase")]
public sealed class SearchEdgeCaseTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public SearchEdgeCaseTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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

    // ── ConstantScoreQuery — wrapping various inner queries ─────────────────

    /// <summary>
    /// Verifies the Constant Score Query: Wrapping Boolean Query All Matches Have Same Score scenario.
    /// </summary>
    [Fact(DisplayName = "Constant Score Query: Wrapping Boolean Query All Matches Have Same Score")]
    public void ConstantScoreQuery_WrappingBooleanQuery_AllMatchesHaveSameScore()
    {
        var dir = new MMapDirectory(SubDir("csq_bool"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var text in new[] { "alpha beta", "alpha gamma", "beta gamma" })
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", text));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var boolQ = new BooleanQuery();
        boolQ.Add(new TermQuery("body", "alpha"), Occur.Should);
        boolQ.Add(new TermQuery("body", "beta"), Occur.Should);

        var results = searcher.Search(new ConstantScoreQuery(boolQ, 7.5f), 10);

        _output.WriteLine($"ConstantScoreQuery(BooleanQuery) → {results.TotalHits} hits");
        Assert.Equal(3, results.TotalHits);
        Assert.All(results.ScoreDocs, sd =>
        {
            _output.WriteLine($"  DocId={sd.DocId} Score={sd.Score:F2}");
            Assert.Equal(7.5f, sd.Score);
        });
    }

    /// <summary>
    /// Verifies the Constant Score Query: Wrapping Range Query All Matches Have Same Score scenario.
    /// </summary>
    [Fact(DisplayName = "Constant Score Query: Wrapping Range Query All Matches Have Same Score")]
    public void ConstantScoreQuery_WrappingRangeQuery_AllMatchesHaveSameScore()
    {
        var dir = new MMapDirectory(SubDir("csq_range"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        for (int i = 0; i < 5; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new NumericField("price", i * 10));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(
            new ConstantScoreQuery(new RangeQuery("price", 15, 35), 3.0f), 10);

        _output.WriteLine($"ConstantScoreQuery(RangeQuery 15..35) → {results.TotalHits} hits");
        Assert.Equal(2, results.TotalHits); // 20, 30
        Assert.All(results.ScoreDocs, sd => Assert.Equal(3.0f, sd.Score));
    }

    /// <summary>
    /// Verifies the Constant Score Query: Wrapping Zero Result Query Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Constant Score Query: Wrapping Zero Result Query Returns Empty")]
    public void ConstantScoreQuery_WrappingZeroResultQuery_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir("csq_zero"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(
            new ConstantScoreQuery(new TermQuery("body", "nonexistent"), 10f), 10);

        _output.WriteLine($"ConstantScoreQuery(zero result inner) → {results.TotalHits} hits");
        Assert.Equal(0, results.TotalHits);
    }

    // ── DisjunctionMaxQuery — with deleted documents ────────────────────────

    /// <summary>
    /// Verifies the Disjunction Max Query: Deleted Docs Excluded From Results scenario.
    /// </summary>
    [Fact(DisplayName = "Disjunction Max Query: Deleted Docs Excluded From Results")]
    public void DisjunctionMaxQuery_DeletedDocs_ExcludedFromResults()
    {
        var dir = new MMapDirectory(SubDir("dmq_deleted"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc0 = new LeanDocument();
        doc0.Add(new TextField("title", "lucene search"));
        writer.AddDocument(doc0);

        var doc1 = new LeanDocument();
        doc1.Add(new TextField("title", "lucene indexing"));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("title", "other topic"));
        writer.AddDocument(doc2);

        writer.Commit();

        // Delete doc 1
        writer.DeleteDocuments(new TermQuery("title", "indexing"));
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var dmq = new DisjunctionMaxQuery(0.0f);
        dmq.Add(new TermQuery("title", "lucene"));

        var results = searcher.Search(dmq, 10);
        _output.WriteLine($"DisjunctionMaxQuery after delete → {results.TotalHits} hits");
        var docIds = results.ScoreDocs.Select(sd => sd.DocId).ToHashSet();
        _output.WriteLine($"  DocIds: [{string.Join(", ", docIds)}]");

        Assert.Equal(1, results.TotalHits);
        Assert.DoesNotContain(1, docIds);
    }

    // ── SpanNearQuery — out-of-order matching ───────────────────────────────

    /// <summary>
    /// Verifies the Span Near Query: In Order False Matches Reversed Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: In Order False Matches Reversed Terms")]
    public void SpanNearQuery_InOrderFalse_MatchesReversedTerms()
    {
        var dir = new MMapDirectory(SubDir("span_ooo"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "the brown quick fox"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        // "quick" appears after "brown" — with inOrder=false and enough slop, should match
        var q = new SpanNearQuery(
            [new SpanTermQuery("body", "quick"), new SpanTermQuery("body", "brown")],
            slop: 2, inOrder: false);
        var results = searcher.Search(q, 10);

        _output.WriteLine($"SpanNearQuery inOrder=false (reversed terms) → {results.TotalHits} hits");
        Assert.Equal(1, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Span Near Query: In Order True Does Not Match Reversed Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Span Near Query: In Order True Does Not Match Reversed Terms")]
    public void SpanNearQuery_InOrderTrue_DoesNotMatchReversedTerms()
    {
        var dir = new MMapDirectory(SubDir("span_inorder"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "the brown quick fox"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        // "quick" appears after "brown" — with inOrder=true, should NOT match
        var q = new SpanNearQuery(
            [new SpanTermQuery("body", "quick"), new SpanTermQuery("body", "brown")],
            slop: 2, inOrder: true);
        var results = searcher.Search(q, 10);

        _output.WriteLine($"SpanNearQuery inOrder=true (reversed terms) → {results.TotalHits} hits");
        Assert.Equal(0, results.TotalHits);
    }

    // ── WildcardQuery — leading wildcard ────────────────────────────────────

    /// <summary>
    /// Verifies the Wildcard Query: Leading Wildcard Matches Suffix scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Query: Leading Wildcard Matches Suffix")]
    public void WildcardQuery_LeadingWildcard_MatchesSuffix()
    {
        var dir = new MMapDirectory(SubDir("wc_leading"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var t in new[] { "running", "swimming", "coding", "testing" })
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", t));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new WildcardQuery("body", "*ning"), 10);

        _output.WriteLine($"WildcardQuery('*ning') → {results.TotalHits} hits");
        // "running" and "swimming" end in "ning"... wait, only "running" ends in "ning"
        // swimming ends in "mming", testing ends in "ting"
        Assert.True(results.TotalHits >= 1,
            $"Expected at least 1 doc matching '*ning', got {results.TotalHits}");
    }

    /// <summary>
    /// Verifies the Wildcard Query: Leading Wildcard No Match Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "Wildcard Query: Leading Wildcard No Match Returns Empty")]
    public void WildcardQuery_LeadingWildcard_NoMatch_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir("wc_leading_nomatch"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new WildcardQuery("body", "*xyz"), 10);

        _output.WriteLine($"WildcardQuery('*xyz') → {results.TotalHits} hits");
        Assert.Equal(0, results.TotalHits);
    }

    // ── RegexpQuery — complex patterns ──────────────────────────────────────

    /// <summary>
    /// Verifies the Regexp Query: Character Class Matches Vowel Only Terms scenario.
    /// </summary>
    [Fact(DisplayName = "Regexp Query: Character Class Matches Vowel Only Terms")]
    public void RegexpQuery_CharacterClass_MatchesVowelOnlyTerms()
    {
        var dir = new MMapDirectory(SubDir("rxq_charclass"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var word in new[] { "aaa", "bee", "ooo", "cat", "dog" })
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("word", word));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        // Match terms consisting only of vowels
        var results = searcher.Search(new RegexpQuery("word", "^[aeiou]+$"), 10);

        _output.WriteLine($"RegexpQuery('^[aeiou]+$') → {results.TotalHits} hits");
        // "aaa" and "ooo" are all-vowel; "bee" has 'b'
        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Regexp Query: Anchored Full Match Only Exact Term Matches scenario.
    /// </summary>
    [Fact(DisplayName = "Regexp Query: Anchored Full Match Only Exact Term Matches")]
    public void RegexpQuery_AnchoredFullMatch_OnlyExactTermMatches()
    {
        var dir = new MMapDirectory(SubDir("rxq_anchored"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var word in new[] { "exact", "exactly", "inexact" })
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("word", word));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new RegexpQuery("word", "^exact$"), 10);

        _output.WriteLine($"RegexpQuery('^exact$') → {results.TotalHits} hits");
        Assert.Equal(1, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Regexp Query: Dot Wildcard Matches Single Char scenario.
    /// </summary>
    [Fact(DisplayName = "Regexp Query: Dot Wildcard Matches Single Char")]
    public void RegexpQuery_DotWildcard_MatchesSingleChar()
    {
        var dir = new MMapDirectory(SubDir("rxq_dot"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        foreach (var word in new[] { "cat", "cut", "cot", "coat" })
        {
            var doc = new LeanDocument();
            doc.Add(new StringField("word", word));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        // "c.t" matches 3-char words: cat, cut, cot — not coat (4 chars)
        var results = searcher.Search(new RegexpQuery("word", "^c.t$"), 10);

        _output.WriteLine($"RegexpQuery('^c.t$') → {results.TotalHits} hits");
        Assert.Equal(3, results.TotalHits);
    }

    // ── FunctionScoreQuery — missing numeric field ──────────────────────────

    /// <summary>
    /// Verifies the Function Score Query: Missing Numeric Field Doc Still Returned scenario.
    /// </summary>
    [Fact(DisplayName = "Function Score Query: Missing Numeric Field Doc Still Returned")]
    public void FunctionScoreQuery_MissingNumericField_DocStillReturned()
    {
        var dir = new MMapDirectory(SubDir("fsq_missing"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Doc 0: has the boost field
        var doc0 = new LeanDocument();
        doc0.Add(new TextField("body", "hello world"));
        doc0.Add(new NumericField("boost", 10.0));
        writer.AddDocument(doc0);

        // Doc 1: matches inner query but has NO boost field
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "hello world"));
        writer.AddDocument(doc1);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var fsq = new FunctionScoreQuery(
            new TermQuery("body", "hello"), "boost", ScoreMode.Multiply);
        var results = searcher.Search(fsq, 10);

        _output.WriteLine($"FunctionScoreQuery with missing field → {results.TotalHits} hits");
        foreach (var sd in results.ScoreDocs)
            _output.WriteLine($"  DocId={sd.DocId} Score={sd.Score:F4}");

        // Both docs should be returned (doc without boost should get a default score)
        Assert.Equal(2, results.TotalHits);
    }

    // ── TopDocs — topN > totalHits ──────────────────────────────────────────

    /// <summary>
    /// Verifies the Search: Top N Greater Than Total Hits Returns Total Hits Only scenario.
    /// </summary>
    [Fact(DisplayName = "Search: Top N Greater Than Total Hits Returns Total Hits Only")]
    public void Search_TopNGreaterThanTotalHits_ReturnsTotalHitsOnly()
    {
        var dir = new MMapDirectory(SubDir("topn_exceeds"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        for (int i = 0; i < 3; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "match"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "match"), 100);

        _output.WriteLine($"TopN=100, actual matches=3 → ScoreDocs.Length={results.ScoreDocs.Length}");
        Assert.Equal(3, results.TotalHits);
        Assert.Equal(3, results.ScoreDocs.Length);
    }
}
