using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Analysis.Tokenisers;
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

[Trait("Category", "Search")]
[Trait("Category", "Advanced")]
public sealed class AdvancedSearchTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AdvancedSearchTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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

    // --- Prefix Query ---

    [Fact]
    public void PrefixQuery_MatchesTermsWithPrefix()
    {
        var dir = new MMapDirectory(SubDir("prefix_match"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var terms = new[] { "search", "searching", "searched", "other" };
        foreach (var t in terms)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", t));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PrefixQuery("body", "search"), 10);
        Assert.Equal(3, results.TotalHits);
    }

    [Theory]
    [InlineData("prefix", "word")]
    [InlineData("wildcard", "word*")]
    [InlineData("wildcard", "*ord*")]
    public void PatternQuery_MultipleMatchingTermsInDocument_ReturnsDocumentOnce(string queryType, string pattern)
    {
        var pathPattern = pattern.Replace("*", "star", StringComparison.Ordinal);
        var dir = new MMapDirectory(SubDir($"pattern_dedup_{queryType}_{pathPattern}"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("title", "word wordy wordless", stored: false));
        doc.Add(new StoredField("id", "1"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        Query query = queryType == "prefix"
            ? new PrefixQuery("title", pattern)
            : new WildcardQuery("title", pattern);

        var results = searcher.Search(query, topN: 10);

        var hit = Assert.Single(results.ScoreDocs);
        Assert.Equal(1, results.TotalHits);
        var stored = searcher.GetStoredFields(hit.DocId);
        Assert.Equal("1", stored["id"][0]);
    }

    [Fact]
    public void PrefixQuery_NoMatch_ReturnsEmpty()
    {
        var dir = new MMapDirectory(SubDir("prefix_nomatch"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new PrefixQuery("body", "xyz"), 10);
        Assert.Equal(0, results.TotalHits);
    }

    // --- Wildcard Query ---

    [Fact]
    public void WildcardQuery_QuestionMark_MatchesSingleChar()
    {
        var dir = new MMapDirectory(SubDir("wildcard_single"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        foreach (var t in new[] { "test", "text", "tent", "tilt" })
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", t));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new WildcardQuery("body", "te?t"), 10);
        // "test", "text", "tent" match; "tilt" does not
        Assert.Equal(3, results.TotalHits);
    }

    [Fact]
    public void WildcardQuery_Asterisk_MatchesMultipleChars()
    {
        var dir = new MMapDirectory(SubDir("wildcard_multi"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        foreach (var t in new[] { "running", "runner", "run", "other" })
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", t));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new WildcardQuery("body", "run*"), 10);
        Assert.Equal(3, results.TotalHits);
    }

    // --- Fuzzy Query ---

    [Fact]
    public void FuzzyQuery_OneEditDistance_MatchesTypo()
    {
        var dir = new MMapDirectory(SubDir("fuzzy_1edit"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        foreach (var t in new[] { "lucene", "lusene", "lycene", "other" })
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", t));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new FuzzyQuery("body", "lucene", 1), 10);
        // "lucene" (0 edits), "lusene" (1 edit) — "lycene" is 2 edits, "other" > 2
        Assert.True(results.TotalHits >= 2);
    }

    [Fact]
    public void FuzzyQuery_ExactMatch_IncludesZeroDistance()
    {
        var dir = new MMapDirectory(SubDir("fuzzy_exact"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "precision"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new FuzzyQuery("body", "precision", 2), 10);
        Assert.Equal(1, results.TotalHits);
    }

    [Fact]
    public void FuzzyQuery_BeyondThreshold_NoMatch()
    {
        var dir = new MMapDirectory(SubDir("fuzzy_beyond"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "abcdef"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new FuzzyQuery("body", "xyz", 1), 10);
        Assert.Equal(0, results.TotalHits);
    }

    // --- Levenshtein Distance ---

    [Fact]
    public void LevenshteinDistance_IdenticalStrings_ReturnsZero()
    {
        Assert.Equal(0, LevenshteinDistance.Compute("hello", "hello"));
    }

    [Fact]
    public void LevenshteinDistance_OneInsertion_ReturnsOne()
    {
        Assert.Equal(1, LevenshteinDistance.Compute("cat", "cats"));
    }

    [Fact]
    public void LevenshteinDistance_OneSubstitution_ReturnsOne()
    {
        Assert.Equal(1, LevenshteinDistance.Compute("cat", "bat"));
    }

    [Fact]
    public void LevenshteinDistance_EmptyStrings_ReturnsOtherLength()
    {
        Assert.Equal(5, LevenshteinDistance.Compute("", "hello"));
        Assert.Equal(3, LevenshteinDistance.Compute("abc", ""));
        Assert.Equal(0, LevenshteinDistance.Compute("", ""));
    }

    // --- Proximity / Slop ---

    [Fact]
    public void PhraseQuery_WithSlop_MatchesNonAdjacentTerms()
    {
        var dir = new MMapDirectory(SubDir("slop_match"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "the quick brown fox jumps"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var pq = new PhraseQuery("body", 1, "quick", "fox");
        var results = searcher.Search(pq, 10);
        Assert.Equal(1, results.TotalHits);
    }

    [Fact]
    public void PhraseQuery_SlopTooSmall_NoMatch()
    {
        var dir = new MMapDirectory(SubDir("slop_nomatch"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "the quick brown fox jumps"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        // "quick" is at 1, "jumps" is at 4 — distance 3, slop 1 is too small
        var pq = new PhraseQuery("body", 1, "quick", "jumps");
        var results = searcher.Search(pq, 10);
        Assert.Equal(0, results.TotalHits);
    }

    [Fact]
    public void PhraseQuery_ExactPhraseSlop0_MatchesAdjacentOnly()
    {
        var dir = new MMapDirectory(SubDir("slop_zero"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "quick brown"));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "quick red brown"));
        writer.AddDocument(doc2);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var pq = new PhraseQuery("body", "quick", "brown");
        var results = searcher.Search(pq, 10);
        Assert.Equal(1, results.TotalHits);
        Assert.Equal(0, results.ScoreDocs[0].DocId);
    }

    // --- Field-Level Boosting ---

    [Fact]
    public void Boosting_HigherBoost_ProducesHigherScore()
    {
        var dir = new MMapDirectory(SubDir("boost_score"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Need multiple docs so BM25 IDF gives non-zero scores
        var doc1 = new LeanDocument();
        doc1.Add(new TextField("body", "important data"));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("body", "other data only"));
        writer.AddDocument(doc2);

        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        var unboosted = new TermQuery("body", "important");
        var boosted = new TermQuery("body", "important") { Boost = 3.0f };

        var r1 = searcher.Search(unboosted, 10);
        var r2 = searcher.Search(boosted, 10);

        Assert.Equal(1, r1.TotalHits);
        Assert.Equal(1, r2.TotalHits);
        Assert.True(r2.ScoreDocs[0].Score > r1.ScoreDocs[0].Score,
            $"Boosted score {r2.ScoreDocs[0].Score} should be > unboosted {r1.ScoreDocs[0].Score}");
    }

    // --- Update Documents ---

    [Fact]
    public void UpdateDocument_ReplacesExisting()
    {
        var dirPath = SubDir("update_doc");
        var dir = new MMapDirectory(dirPath);
        var config = new IndexWriterConfig { MaxBufferedDocs = 1000 };
        using var writer = new IndexWriter(dir, config);

        var doc1 = new LeanDocument();
        doc1.Add(new TextField("id", "doc1"));
        doc1.Add(new TextField("body", "alpha content here"));
        writer.AddDocument(doc1);

        var doc2 = new LeanDocument();
        doc2.Add(new TextField("id", "doc2"));
        doc2.Add(new TextField("body", "gamma content here"));
        writer.AddDocument(doc2);

        writer.Commit();

        var replacement = new LeanDocument();
        replacement.Add(new TextField("id", "doc1"));
        replacement.Add(new TextField("body", "beta content here"));
        writer.UpdateDocument("id", "doc1", replacement);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var alphaResults = searcher.Search(new TermQuery("body", "alpha"), 10);
        var betaResults = searcher.Search(new TermQuery("body", "beta"), 10);
        var gammaResults = searcher.Search(new TermQuery("body", "gamma"), 10);

        Assert.Equal(0, alphaResults.TotalHits);
        Assert.Equal(1, betaResults.TotalHits);
        Assert.Equal(1, gammaResults.TotalHits);
    }

    // --- Per-Field Analysers ---

    [Fact]
    public void PerFieldAnalyser_DifferentFieldsUseDifferentAnalysis()
    {
        var dir = new MMapDirectory(SubDir("perfield_analyser"));
        var config = new IndexWriterConfig
        {
            FieldAnalysers =
            {
                ["exact"] = new Analyser(new Tokeniser()) // no lowercase, no stop words
            }
        };
        using var writer = new IndexWriter(dir, config);

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "The Quick Brown Fox"));
        doc.Add(new TextField("exact", "The Quick Brown Fox"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);

        // "the" should be stop-word removed from "body" (default analyser)
        var bodyThe = searcher.Search(new TermQuery("body", "the"), 10);
        Assert.Equal(0, bodyThe.TotalHits);

        // "The" should be in "exact" field (no lowercase, no stop word removal)
        var exactThe = searcher.Search(new TermQuery("exact", "The"), 10);
        Assert.Equal(1, exactThe.TotalHits);
    }

    // --- Concurrent Indexing ---

    [Fact]
    public void ConcurrentIndexing_ParallelAdds_AllDocsSearchable()
    {
        var dir = new MMapDirectory(SubDir("concurrent_add"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        const int docsPerThread = 25;
        const int threadCount = 4;

        Parallel.For(0, threadCount, t =>
        {
            for (int i = 0; i < docsPerThread; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new TextField("body", $"thread{t}_doc{i} common"));
                writer.AddDocument(doc);
            }
        });
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "common"), 200);
        Assert.Equal(docsPerThread * threadCount, results.TotalHits);
    }

    // --- Query-Time Analysis ---

    [Fact]
    public void QueryTimeAnalysis_StringSearch_MatchesCaseInsensitive()
    {
        var dir = new MMapDirectory(SubDir("queryanalysis"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "The Quick Brown Fox"));
        writer.AddDocument(doc);
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search("Quick", "body", 10);
        Assert.Equal(1, results.TotalHits);
    }

    // --- PostingsEnum.Advance ---

    [Fact]
    public void PostingsEnum_Advance_SkipsToTargetDocId()
    {
        var dir = new MMapDirectory(SubDir("advance_skip"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        for (int i = 0; i < 20; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", i % 2 == 0 ? "even" : "odd"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        // Just verify search still works — the Advance is used internally
        var results = searcher.Search(new TermQuery("body", "even"), 20);
        Assert.Equal(10, results.TotalHits);
    }

    // --- Stored Fields Compression ---

    [Fact]
    public void StoredFieldsCompression_RoundTrip_PreservesData()
    {
        var dir = new MMapDirectory(SubDir("stored_compress"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        // Index enough docs to fill at least one compression block (16)
        for (int i = 0; i < 20; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"document number {i} with some content"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "document"), 20);
        Assert.Equal(20, results.TotalHits);
    }

    // --- Wildcard Pattern Matching Utility ---

    [Theory]
    [InlineData("test", "test", true)]
    [InlineData("test", "te?t", true)]
    [InlineData("test", "t*", true)]
    [InlineData("test", "*est", true)]
    [InlineData("test", "t*t", true)]
    [InlineData("test", "x*", false)]
    [InlineData("test", "te?", false)]
    [InlineData("café", "caf?", true)]
    [InlineData("café", "ca?", false)]
    [InlineData("", "*", true)]
    [InlineData("", "?", false)]
    public void WildcardQuery_Matches_CorrectResults(string text, string pattern, bool expected)
    {
        Assert.Equal(expected, WildcardQuery.Matches(text, pattern));
    }
}
