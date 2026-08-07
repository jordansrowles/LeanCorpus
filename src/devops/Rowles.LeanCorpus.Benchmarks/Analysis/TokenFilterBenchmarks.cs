using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares token filter throughput between LeanCorpus batch
/// (<see cref="ISpanTokenFilter"/>) and Lucene.NET streaming implementations.
/// Reused and cold pipeline construction are measured separately.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[InvocationCount(1)]
public class TokenFilterBenchmarks
{
    [Params(
        "length-noop",
        "length-mutating",
        "truncate-noop",
        "truncate-mutating",
        "unique-mutating",
        "reverse-mutating",
        "elision-mutating")]
    public string Scenario { get; set; } = "length-noop";

    private Token[] _source = [];
    private ISpanTokenFilter _filter = null!;
    private ISpanTokenFilter _iterationFilter = null!;
    private string _luceneInput = string.Empty;
    private Lucene.Net.Analysis.TokenStream? _luceneFilter;
    private int _expectedLeanCount;
    private int _expectedLuceneCount;

    [GlobalSetup]
    public void Setup()
    {
        (Token[] source, ISpanTokenFilter filter, string input) configured = Scenario switch
        {
            "length-noop" => (BuildTokens(["quick", "brown", "fox"]), new LengthFilter(2, 8), "quick brown fox"),
            "length-mutating" => (BuildTokens(["a", "quick", "extraordinary"]), new LengthFilter(2, 8), "a quick extraordinary"),
            "truncate-noop" => (BuildTokens(["quick", "brown", "fox"]), new TruncateTokenFilter(12), "quick brown fox"),
            "truncate-mutating" => (BuildTokens(["extraordinary", "token"]), new TruncateTokenFilter(6), "extraordinary token"),
            "unique-mutating" => (BuildTokens(["fast", "quick", "fast", "rapid"]), new UniqueTokenFilter(), "fast quick fast rapid"),
            "reverse-mutating" => (BuildTokens(["abcdef", "café"]), new ReverseStringFilter(), "abcdef café"),
            "elision-mutating" => (BuildTokens(["l'avion", "qu\u2019elle"]), new ElisionFilter(), "l'avion qu\u2019elle"),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'.")
        };

        _source = configured.source;
        _filter = configured.filter;
        _luceneInput = configured.input;

        var expectedLeanTerms = CaptureLean(_filter.Clone());
        _expectedLeanCount = expectedLeanTerms.Count;
        using var baseStream = BuildLuceneBaseStream(_luceneInput);
        using var luceneFilter = BuildLuceneFilter(baseStream);
        var expectedLuceneTerms = CaptureLucene(luceneFilter);
        _expectedLuceneCount = expectedLuceneTerms.Count;
        if (!expectedLeanTerms.SequenceEqual(expectedLuceneTerms, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Token-filter fixture '{Scenario}' is not comparable: " +
                $"LeanCorpus emitted [{string.Join(", ", expectedLeanTerms)}], " +
                $"Lucene.NET emitted [{string.Join(", ", expectedLuceneTerms)}].");
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _iterationFilter = _filter.Clone();
        _luceneFilter = BuildLuceneFilter(BuildLuceneBaseStream(_luceneInput));
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _luceneFilter?.Dispose();
        _luceneFilter = null;
    }

    [Benchmark(Baseline = true, Description = "LeanCorpus reused filter")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Apply()
        => ValidateCount(ApplyLean(_iterationFilter), _expectedLeanCount, "LeanCorpus");

    [Benchmark(Description = "Lucene.NET reused filter")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Apply()
    {
        var filter = _luceneFilter ?? throw new InvalidOperationException("Lucene filter was not prepared.");
        return ValidateCount(ApplyLucene(filter), _expectedLuceneCount, "Lucene.NET");
    }

    [Benchmark(Description = "LeanCorpus cold filter pipeline")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ApplyColdPipeline()
        => ValidateCount(ApplyLean(_filter.Clone()), _expectedLeanCount, "LeanCorpus cold");

    [Benchmark(Description = "Lucene.NET cold filter pipeline")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_ApplyColdPipeline()
    {
        using var baseStream = BuildLuceneBaseStream(_luceneInput);
        using var filter = BuildLuceneFilter(baseStream);
        return ValidateCount(ApplyLucene(filter), _expectedLuceneCount, "Lucene.NET cold");
    }

    private int ApplyLean(ISpanTokenFilter filter)
    {
        var sink = new CountingTokenSink();
        foreach (var token in _source)
        {
            filter.Apply(token.Text.AsSpan(), token.StartOffset, token.EndOffset,
                token.Type, token.PositionIncrement, token.Payload, sink);
        }
        filter.Finish(sink);
        return sink.Count;
    }

    private static int ApplyLucene(Lucene.Net.Analysis.TokenStream filter)
    {
        int total = 0;
        filter.Reset();
        while (filter.IncrementToken())
            total++;
        filter.End();
        return total;
    }

    private IReadOnlyList<string> CaptureLean(ISpanTokenFilter filter)
    {
        var sink = new TermCaptureSink();
        foreach (var token in _source)
        {
            filter.Apply(token.Text.AsSpan(), token.StartOffset, token.EndOffset,
                token.Type, token.PositionIncrement, token.Payload, sink);
        }
        filter.Finish(sink);
        return sink.Terms;
    }

    private static IReadOnlyList<string> CaptureLucene(Lucene.Net.Analysis.TokenStream filter)
    {
        var terms = new List<string>();
        var term = filter.AddAttribute<Lucene.Net.Analysis.TokenAttributes.ICharTermAttribute>();
        filter.Reset();
        while (filter.IncrementToken())
            terms.Add(term.ToString());
        filter.End();
        return terms;
    }

    private static int ValidateCount(int actual, int expected, string implementation)
    {
        if (actual != expected)
            throw new InvalidOperationException(
                $"{implementation} token-filter output changed: expected {expected}, got {actual}.");
        return actual;
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

    private static Lucene.Net.Analysis.TokenStream BuildLuceneBaseStream(string input)
        => new Lucene.Net.Analysis.Core.WhitespaceTokenizer(
            Lucene.Net.Util.LuceneVersion.LUCENE_48,
            new System.IO.StringReader(input));

    private Lucene.Net.Analysis.TokenStream BuildLuceneFilter(Lucene.Net.Analysis.TokenStream input)
        => Scenario switch
        {
            "length-noop" or "length-mutating" =>
                new Lucene.Net.Analysis.Miscellaneous.LengthFilter(
                    Lucene.Net.Util.LuceneVersion.LUCENE_48, input, 2, 8),
            "truncate-noop" or "truncate-mutating" =>
                new Lucene.Net.Analysis.Miscellaneous.TruncateTokenFilter(
                    input, Scenario == "truncate-mutating" ? 6 : 12),
            "unique-mutating" =>
                new Lucene.Net.Analysis.Miscellaneous.RemoveDuplicatesTokenFilter(input),
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
            _ => input
        };

    private sealed class TermCaptureSink : ISpanTokenSink
    {
        internal List<string> Terms { get; } = [];

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
            => Terms.Add(text.ToString());
    }
}
