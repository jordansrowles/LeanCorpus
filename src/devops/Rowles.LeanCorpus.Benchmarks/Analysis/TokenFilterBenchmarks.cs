using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Benchmarks;
/// <summary>
/// Compares token filter throughput between LeanCorpus batch (<see cref="ISpanTokenFilter"/>)
/// and Lucene.NET streaming (<c>TokenFilter</c>) implementations for equivalent filters.
/// DecimalDigitFilter has no Lucene.NET equivalent and is LeanCorpus-only.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class TokenFilterBenchmarks
{
    [Params(
        "length-noop",
        "length-mutating",
        "truncate-noop",
        "truncate-mutating",
        "unique-mutating",
        "decimal-digit-mutating",
        "reverse-mutating",
        "elision-mutating",
        "shingle-mutating",
        "word-delimiter-mutating")]
    public string Scenario { get; set; } = "length-noop";

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
            "length-noop" => (
                BuildTokens(["quick", "brown", "fox"]),
                new LengthFilter(2, 8),
                "quick brown fox"),
            "length-mutating" => (
                BuildTokens(["a", "quick", "extraordinary"]),
                new LengthFilter(2, 8),
                "a quick extraordinary"),
            "truncate-noop" => (
                BuildTokens(["quick", "brown", "fox"]),
                new TruncateTokenFilter(12),
                "quick brown fox"),
            "truncate-mutating" => (
                BuildTokens(["extraordinary", "token"]),
                new TruncateTokenFilter(6),
                "extraordinary token"),
            "unique-mutating" => (
                BuildTokens(["fast", "quick", "fast", "rapid"]),
                new UniqueTokenFilter(),
                "fast quick fast rapid"),
            "decimal-digit-mutating" => (
                BuildTokens(["\u0661\u06F2\uFF134", "plain"]),
                new DecimalDigitFilter(),
                $"\u0661\u06F2\uFF134 plain"),
            "reverse-mutating" => (
                BuildTokens(["abcdef", "café"]),
                new ReverseStringFilter(),
                "abcdef café"),
            "elision-mutating" => (
                BuildTokens(["l'avion", "qu\u2019elle"]),
                new ElisionFilter(),
                "l'avion qu\u2019elle"),
            "shingle-mutating" => (
                BuildTokens(["new", "york", "city"]),
                new ShingleFilter(2, 3),
                "new york city"),
            "word-delimiter-mutating" => (
                BuildTokens(["WiFi4Schools_test"]),
                new WordDelimiterFilter(),
                "WiFi4Schools_test"),
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
        var sink = new MaterialisingTokenSink();
        foreach (var token in _source)
            _filter.Apply(token.Text.AsSpan(), token.StartOffset, token.EndOffset, token.Type, token.PositionIncrement, token.Payload, sink);
        return sink.Tokens.Count;
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
            "length-noop" or "length-mutating" =>
                new Lucene.Net.Analysis.Miscellaneous.LengthFilter(
                    Lucene.Net.Util.LuceneVersion.LUCENE_48, input, 2, 8),
            "truncate-noop" or "truncate-mutating" =>
                new Lucene.Net.Analysis.Miscellaneous.TruncateTokenFilter(
                    input,
                    Scenario == "truncate-mutating" ? 6 : 12),

            "unique-mutating" =>
                new Lucene.Net.Analysis.Miscellaneous.RemoveDuplicatesTokenFilter(input),

            "decimal-digit-mutating" =>
                // No Lucene.NET equivalent; pass through unchanged.
                input,

            "reverse-mutating" =>
                new Lucene.Net.Analysis.Reverse.ReverseStringFilter(
                    Lucene.Net.Util.LuceneVersion.LUCENE_48, input),

            "elision-mutating" =>
                new Lucene.Net.Analysis.Util.ElisionFilter(input,
                    new Lucene.Net.Analysis.Util.CharArraySet(
                        Lucene.Net.Util.LuceneVersion.LUCENE_48,
                        ["l", "m", "t", "qu", "n", "s", "j", "d", "c",
                         "jusqu", "quoiqu", "lorsqu", "puisqu"],
                        ignoreCase: true)),

            "shingle-mutating" =>
                new Lucene.Net.Analysis.Shingle.ShingleFilter(input, 2, 3),

            "word-delimiter-mutating" =>
                new Lucene.Net.Analysis.Miscellaneous.WordDelimiterFilter(
                    Lucene.Net.Util.LuceneVersion.LUCENE_48, input,
                    Lucene.Net.Analysis.Miscellaneous.WordDelimiterFlags.GENERATE_WORD_PARTS |
                    Lucene.Net.Analysis.Miscellaneous.WordDelimiterFlags.GENERATE_NUMBER_PARTS |
                    Lucene.Net.Analysis.Miscellaneous.WordDelimiterFlags.CATENATE_WORDS |
                    Lucene.Net.Analysis.Miscellaneous.WordDelimiterFlags.CATENATE_NUMBERS |
                    Lucene.Net.Analysis.Miscellaneous.WordDelimiterFlags.SPLIT_ON_CASE_CHANGE |
                    Lucene.Net.Analysis.Miscellaneous.WordDelimiterFlags.SPLIT_ON_NUMERICS |
                    Lucene.Net.Analysis.Miscellaneous.WordDelimiterFlags.STEM_ENGLISH_POSSESSIVE,
                    Lucene.Net.Analysis.Util.CharArraySet.EMPTY_SET),
            _ => input
        };
    }
}
