using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Analysis;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures allocation and throughput for token filters on no-op and mutating paths.
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

    private Token[] _source = [];
    private ITokenFilter _filter = null!;

    [GlobalSetup]
    public void Setup()
    {
        (Token[] Source, ITokenFilter Filter) configured = Scenario switch
        {
            "length-noop" => (BuildTokens(["quick", "brown", "fox"]), new LengthFilter(2, 8)),
            "length-mutating" => (BuildTokens(["a", "quick", "extraordinary"]), new LengthFilter(2, 8)),
            "truncate-noop" => (BuildTokens(["quick", "brown", "fox"]), new TruncateTokenFilter(12)),
            "truncate-mutating" => (BuildTokens(["extraordinary", "token"]), new TruncateTokenFilter(6)),
            "unique-mutating" => (BuildTokens(["fast", "quick", "fast", "rapid"]), new UniqueTokenFilter()),
            "decimal-digit-mutating" => (BuildTokens(["\u0661\u06F2\uFF134", "plain"]), new DecimalDigitFilter()),
            "reverse-mutating" => (BuildTokens(["abcdef", "café"]), new ReverseStringFilter()),
            "elision-mutating" => (BuildTokens(["l'avion", "qu\u2019elle"]), new ElisionFilter()),
            "shingle-mutating" => (BuildTokens(["new", "york", "city"]), new ShingleFilter(2, 3)),
            "word-delimiter-mutating" => (BuildTokens(["WiFi4Schools_test"]), new WordDelimiterFilter()),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'.")
        };

        _source = configured.Source;
        _filter = configured.Filter;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Apply()
    {
        var tokens = new List<Token>(_source);
        _filter.Apply(tokens);
        return tokens.Count;
    }

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
}
