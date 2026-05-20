using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// End-to-end integration tests verifying that custom analysers (NGramTokeniser,
/// EdgeNGramTokeniser, AccentFoldingFilter) work correctly through the full
/// index→search pipeline.
/// </summary>
[Trait("Category", "Analysis")]
[Trait("Category", "Integration")]
public sealed class AnalysisIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AnalysisIntegrationTests(TestDirectoryFixture fixture, ITestOutputHelper output)
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

    // ── NGramTokeniser end-to-end───────────────────────────────────────────

    /// <summary>
    /// Verifies the NGram Tokeniser: Index Partial Word Search Finds Document scenario.
    /// </summary>
    [Fact(DisplayName = "NGram Tokeniser: Index Partial Word Search Finds Document")]
    public void NGramTokeniser_Index_PartialWordSearchFindsDocument()
    {
        var analyser = new Analyser(new NGramTokeniser(2, 3));
        var dir = new MMapDirectory(SubDir("ngram_e2e"));
        var config = new IndexWriterConfig { DefaultAnalyser = analyser };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        // "ll" is a bigram of "hello"
        var results = searcher.Search(new TermQuery("body", "ll"), 10);
        _output.WriteLine($"NGram E2E: TermQuery('ll') on 'hello' → {results.TotalHits} hits");
        Assert.Equal(1, results.TotalHits);

        // "ell" is a trigram of "hello"
        var results2 = searcher.Search(new TermQuery("body", "ell"), 10);
        _output.WriteLine($"NGram E2E: TermQuery('ell') on 'hello' → {results2.TotalHits} hits");
        Assert.Equal(1, results2.TotalHits);
    }

    /// <summary>
    /// Verifies the NGram Tokeniser: Bigram On Index Matches Expected Docs scenario.
    /// </summary>
    [Fact(DisplayName = "NGram Tokeniser: Bigram On Index Matches Expected Docs")]
    public void NGramTokeniser_BigramOnIndex_MatchesExpectedDocs()
    {
        var analyser = new Analyser(new NGramTokeniser(2, 2));
        var dir = new MMapDirectory(SubDir("ngram_bigram"));
        var config = new IndexWriterConfig { DefaultAnalyser = analyser };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc1 = new LeanDocument();
            doc1.Add(new TextField("body", "cat"));
            writer.AddDocument(doc1);

            var doc2 = new LeanDocument();
            doc2.Add(new TextField("body", "cart"));
            writer.AddDocument(doc2);

            var doc3 = new LeanDocument();
            doc3.Add(new TextField("body", "dog"));
            writer.AddDocument(doc3);

            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        // "ca" bigram should match "cat" and "cart" but not "dog"
        var results = searcher.Search(new TermQuery("body", "ca"), 10);
        _output.WriteLine($"NGram bigram: TermQuery('ca') → {results.TotalHits} hits");
        Assert.Equal(2, results.TotalHits);
    }

    /// <summary>
    /// Verifies span analysis is used by indexing without falling back to the legacy token list path.
    /// </summary>
    [Fact(DisplayName = "NGram Tokeniser: Span Analysis Indexes Without Legacy Analyse")]
    public void NGramTokeniser_SpanAnalysis_IndexesWithoutLegacyAnalyse()
    {
        var analyser = new SpanOnlyNGramAnalyser();
        var dir = new MMapDirectory(SubDir("ngram_span_only"));
        var config = new IndexWriterConfig { DefaultAnalyser = analyser };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "abcd"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "bc"), 10);
        Assert.Equal(1, results.TotalHits);
    }

    /// <summary>
    /// Verifies span analysis with a lowercase filter indexes without falling back to legacy tokenisation.
    /// </summary>
    [Fact(DisplayName = "NGram Tokeniser: Span Lowercase Indexes Without Legacy Tokenise")]
    public void NGramTokeniser_SpanLowercase_IndexesWithoutLegacyTokenise()
    {
        var analyser = new Analyser(new SpanOnlyUpperTokeniser(), new LowercaseFilter());
        var dir = new MMapDirectory(SubDir("ngram_span_lowercase"));
        var config = new IndexWriterConfig { DefaultAnalyser = analyser };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "ignored"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "ab"), 10);
        Assert.Equal(1, results.TotalHits);
    }


    // ── EdgeNGramTokeniser end-to-end ───────────────────────────────────────

    /// <summary>
    /// Verifies the EdgeNGram Tokeniser: Prefix Autocomplete Term Query Finds Doc scenario.
    /// </summary>
    [Fact(DisplayName = "EdgeNGram Tokeniser: Prefix Autocomplete Term Query Finds Doc")]
    public void EdgeNGramTokeniser_PrefixAutocomplete_TermQueryFindsDoc()
    {
        var analyser = new Analyser(new EdgeNGramTokeniser(1, 4));
        var dir = new MMapDirectory(SubDir("edgengram_e2e"));
        var config = new IndexWriterConfig { DefaultAnalyser = analyser };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello world"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        // "he" is an edge n-gram of "hello"
        var results = searcher.Search(new TermQuery("body", "he"), 10);
        _output.WriteLine($"EdgeNGram E2E: TermQuery('he') on 'hello world' → {results.TotalHits} hits");
        Assert.Equal(1, results.TotalHits);

        // "wo" is an edge n-gram of "world"
        var results2 = searcher.Search(new TermQuery("body", "wo"), 10);
        _output.WriteLine($"EdgeNGram E2E: TermQuery('wo') on 'hello world' → {results2.TotalHits} hits");
        Assert.Equal(1, results2.TotalHits);
    }

    /// <summary>
    /// Verifies the EdgeNGram Tokeniser: Non Edge Substring Does Not Match scenario.
    /// </summary>
    [Fact(DisplayName = "EdgeNGram Tokeniser: Non Edge Substring Does Not Match")]
    public void EdgeNGramTokeniser_NonEdgeSubstring_DoesNotMatch()
    {
        var analyser = new Analyser(new EdgeNGramTokeniser(2, 3));
        var dir = new MMapDirectory(SubDir("edgengram_nonedge"));
        var config = new IndexWriterConfig { DefaultAnalyser = analyser };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "hello"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        // "lo" is NOT an edge n-gram of "hello" (it's in the middle)
        var results = searcher.Search(new TermQuery("body", "lo"), 10);
        _output.WriteLine($"EdgeNGram: TermQuery('lo') on 'hello' → {results.TotalHits} hits (expected 0)");
        Assert.Equal(0, results.TotalHits);
    }

    // ── AccentFoldingFilter end-to-end ──────────────────────────────────────

    /// <summary>
    /// Verifies the Accent Folding Filter: Index Accented Search Unaccented Finds Doc scenario.
    /// </summary>
    [Fact(DisplayName = "Accent Folding Filter: Index Accented Search Unaccented Finds Doc")]
    public void AccentFoldingFilter_IndexAccented_SearchUnaccented_FindsDoc()
    {
        var analyser = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new AccentFoldingFilter());

        var dir = new MMapDirectory(SubDir("accent_e2e"));
        var config = new IndexWriterConfig { DefaultAnalyser = analyser };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "café naïve résumé"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);

        foreach (var term in new[] { "cafe", "naive", "resume" })
        {
            var results = searcher.Search(new TermQuery("body", term), 10);
            _output.WriteLine($"AccentFolding E2E: TermQuery('{term}') → {results.TotalHits} hits");
            Assert.Equal(1, results.TotalHits);
        }
    }

    /// <summary>
    /// Verifies the Accent Folding Filter: Index Unaccented Search Unaccented Still Works scenario.
    /// </summary>
    [Fact(DisplayName = "Accent Folding Filter: Index Unaccented Search Unaccented Still Works")]
    public void AccentFoldingFilter_IndexUnaccented_SearchUnaccented_StillWorks()
    {
        var analyser = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new AccentFoldingFilter());

        var dir = new MMapDirectory(SubDir("accent_noaccent"));
        var config = new IndexWriterConfig { DefaultAnalyser = analyser };

        using (var writer = new IndexWriter(dir, config))
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", "cafe naive resume"));
            writer.AddDocument(doc);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var results = searcher.Search(new TermQuery("body", "cafe"), 10);
        _output.WriteLine($"AccentFolding (no accents): TermQuery('cafe') → {results.TotalHits} hits");
        Assert.Equal(1, results.TotalHits);
    }

    private sealed class SpanOnlyNGramAnalyser : IAnalyser, ISpanAnalyser
    {
        private readonly NGramTokeniser _tokeniser = new(2, 2);

        public List<Token> Analyse(ReadOnlySpan<char> input)
            => throw new InvalidOperationException("Legacy analysis path should not be used.");

        public bool TryAnalyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            _tokeniser.Tokenise(input, sink);
            return true;
        }
    }

    private sealed class SpanOnlyUpperTokeniser : ITokeniser, ISpanTokeniser
    {
        public List<Token> Tokenise(ReadOnlySpan<char> input)
            => throw new InvalidOperationException("Legacy tokenisation path should not be used.");

        public void Tokenise(ReadOnlySpan<char> input, ISpanTokenSink sink)
            => sink.Add("AB".AsSpan(), 0, 2);
    }
}
