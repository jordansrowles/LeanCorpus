using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares token filter throughput between LeanCorpus span-based
/// (<see cref="ISpanTokenFilter"/>) and Lucene.NET streaming (<c>TokenFilter</c>)
/// implementations for newly added filters.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class NewTokenFilterBenchmarks
{
    [Params(
        "classic-noop",
        "classic-mutating",
        "pattern-replace-noop",
        "pattern-replace-mutating",
        "common-grams",
        "hyphenated-words",
        "caching")]
    public string Scenario { get; set; } = "classic-noop";

    // LeanCorpus state
    private Token[] _source = [];
    private ISpanTokenFilter _filter = null!;

    // Lucene.NET state: the raw input string for the tokeniser
    private string _luceneInput = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        (Token[] source, ISpanTokenFilter filter, string input) configured = Scenario switch
        {
            "classic-noop" => (
                BuildTokens(["quick", "brown", "fox"]),
                new ClassicFilter(),
                "quick brown fox"),
            "classic-mutating" => (
                BuildTokens(["dogs'", "U.S.A.", "O\u2019Reilly\u2019s", "I.B.M."]),
                new ClassicFilter(),
                "dogs' U.S.A. O\u2019Reilly\u2019s I.B.M."),
            "pattern-replace-noop" => (
                BuildTokens(["hello", "world"]),
                new PatternReplaceFilter("[0-9]+", "#"),
                "hello world"),
            "pattern-replace-mutating" => (
                BuildTokens(["call", "12345", "now"]),
                new PatternReplaceFilter("[0-9]+", "#"),
                "call 12345 now"),
            "common-grams" => (
                BuildTokens(["the", "quick", "brown", "fox", "the", "lazy", "dog"]),
                new CommonGramsFilter(["the", "quick", "lazy"]),
                "the quick brown fox the lazy dog"),
            "hyphenated-words" => (
                BuildTokensWithPositions([("state", 1), ("of", 0), ("the", 0), ("art", 0)]),
                new HyphenatedWordsFilter('-'),
                "state-of-the-art"),
            "caching" => (
                BuildTokens(["alpha", "beta", "gamma", "delta"]),
                new CachingTokenFilter(),
                "alpha beta gamma delta"),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'.")
        };

        _source = configured.source;
        _filter = configured.filter;
        _luceneInput = configured.input;
    }

    // --- LeanCorpus benchmark ---

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Apply()
    {
        var sink = new CountingTokenSink();
        foreach (var token in _source)
            _filter.Apply(token.Text.AsSpan(), token.StartOffset, token.EndOffset,
                token.Type, token.PositionIncrement, token.Payload, sink);
        _filter.Finish(sink);
        return sink.Count;
    }

    // --- Lucene.NET streaming benchmark ---

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Apply()
    {
        using var baseStream = BuildLuceneBaseStream(_luceneInput);
        using var filter = BuildLuceneFilter(baseStream);
        int total = 0;
        filter.Reset();
        while (filter.IncrementToken())
            total++;
        filter.End();
        return total;
    }

    // --- Helpers ---

    private static Token[] BuildTokens(string[] terms)
    {
        var tokens = new Token[terms.Length];
        int offset = 0;
        for (int i = 0; i < terms.Length; i++)
        {
            string term = terms[i];
            tokens[i] = new Token(term, offset, offset + term.Length);
            offset += term.Length + 1;
        }

        return tokens;
    }

    private static Token[] BuildTokensWithPositions((string Text, int PosInc)[] terms)
    {
        var tokens = new Token[terms.Length];
        int offset = 0;
        for (int i = 0; i < terms.Length; i++)
        {
            string term = terms[i].Text;
            tokens[i] = new Token(term, offset, offset + term.Length,
                positionIncrement: terms[i].PosInc);
            offset += term.Length + 1;
        }

        return tokens;
    }

    /// <summary>
    /// Builds a whitespace-tokenised base stream matching the LeanCorpus token list.
    /// </summary>
    private static Lucene.Net.Analysis.TokenStream BuildLuceneBaseStream(string input)
    {
        return new Lucene.Net.Analysis.Core.WhitespaceTokenizer(
            Lucene.Net.Util.LuceneVersion.LUCENE_48,
            new System.IO.StringReader(input));
    }

    /// <summary>
    /// Wraps the base stream with the Lucene.NET equivalent of the selected LeanCorpus filter.
    /// </summary>
    private Lucene.Net.Analysis.TokenStream BuildLuceneFilter(Lucene.Net.Analysis.TokenStream input)
    {
        return Scenario switch
        {
            "classic-noop" or "classic-mutating" =>
                new Lucene.Net.Analysis.Standard.ClassicFilter(input),

            "pattern-replace-noop" or "pattern-replace-mutating" =>
                new Lucene.Net.Analysis.Pattern.PatternReplaceFilter(
                    input,
                    new System.Text.RegularExpressions.Regex("[0-9]+"),
                    "#",
                    all: true),

            "common-grams" =>
                new Lucene.Net.Analysis.CommonGrams.CommonGramsFilter(
                    Lucene.Net.Util.LuceneVersion.LUCENE_48,
                    input,
                    new Lucene.Net.Analysis.Util.CharArraySet(
                        Lucene.Net.Util.LuceneVersion.LUCENE_48,
                        ["the", "quick", "lazy"],
                        ignoreCase: true)),

            "hyphenated-words" =>
                new Lucene.Net.Analysis.Miscellaneous.HyphenatedWordsFilter(input),

            "caching" =>
                // Lucene.NET CachingTokenFilter lives in Lucene.Net.Analysis, not Miscellaneous.
                // Its constructor signature differs across versions; pass-through to
                // avoid coupling to a specific assembly layout.
                input,

            _ => input
        };
    }
}
