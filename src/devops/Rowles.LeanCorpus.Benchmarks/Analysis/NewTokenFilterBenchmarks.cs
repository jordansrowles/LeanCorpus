using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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
[RPlotExporter]
[InvocationCount(1)]
public class NewTokenFilterBenchmarks
{
    [Params(
        "classic-noop",
        "pattern-replace-noop",
        "pattern-replace-mutating",
        "hyphenated-words",
        "caching")]
    public string Scenario { get; set; } = "classic-noop";

    // LeanCorpus state
    private Token[] _source = [];
    private ISpanTokenFilter _filter = null!;
    private ISpanTokenFilter _iterationFilter = null!;

    // Lucene.NET state: the raw input string for the tokeniser
    private string _luceneInput = string.Empty;
    private Lucene.Net.Analysis.Tokenizer? _luceneBaseStream;
    private Lucene.Net.Analysis.TokenStream? _luceneFilter;
    private bool _luceneStreamConsumed;
    private int _expectedLeanCount;
    private int _expectedLuceneCount;

    [GlobalSetup]
    public void Setup()
    {
        (Token[] source, ISpanTokenFilter filter, string input) configured = Scenario switch
        {
            "classic-noop" => (
                BuildTokens(["quick", "brown", "fox"]),
                new ClassicFilter(),
                "quick brown fox"),
            "pattern-replace-noop" => (
                BuildTokens(["hello", "world"]),
                new PatternReplaceFilter("[0-9]+", "#"),
                "hello world"),
            "pattern-replace-mutating" => (
                BuildTokens(["call", "12345", "now"]),
                new PatternReplaceFilter("[0-9]+", "#"),
                "call 12345 now"),
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

        var expectedLeanTerms = CaptureLean(
            Scenario == "caching" ? new CachingTokenFilter() : _filter.Clone());
        _expectedLeanCount = expectedLeanTerms.Count;
        using var baseStream = BuildLuceneBaseStream(_luceneInput);
        using var luceneFilter = BuildLuceneFilter(baseStream);
        if (Scenario == "caching")
            baseStream.Reset();
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
        _iterationFilter = Scenario == "caching"
            ? new CachingTokenFilter()
            : _filter.Clone();
        _luceneBaseStream = BuildLuceneBaseStream(_luceneInput);
        _luceneFilter = BuildLuceneFilter(_luceneBaseStream);
        _luceneStreamConsumed = false;
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        if (_iterationFilter is CachingTokenFilter caching)
            caching.Reset();
        _luceneFilter?.Dispose();
        _luceneFilter = null;
        _luceneBaseStream = null;
        _luceneStreamConsumed = false;
    }

    // --- LeanCorpus benchmark ---

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Apply()
    {
        return ValidateCount(ApplyLean(_iterationFilter), _expectedLeanCount, "LeanCorpus");
    }

    // --- Lucene.NET streaming benchmark ---

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Apply()
    {
        var filter = _luceneFilter ?? throw new InvalidOperationException("Lucene filter was not prepared.");
        var baseStream = _luceneBaseStream
            ?? throw new InvalidOperationException("Lucene base stream was not prepared.");

        if (Scenario == "caching")
        {
            // CachingTokenFilter replays its cached token state after the first
            // pass and must not reset its already-consumed input stream.
            if (!_luceneStreamConsumed)
                baseStream.Reset();
        }
        else if (_luceneStreamConsumed)
        {
            // Lucene tokenizers require the completed stream to be disposed
            // before a fresh reader is supplied for the next invocation.
            filter.Dispose();
            baseStream.SetReader(new System.IO.StringReader(_luceneInput));
        }

        int total = 0;
        filter.Reset();
        while (filter.IncrementToken())
            total++;
        filter.End();
        _luceneStreamConsumed = true;
        return ValidateCount(total, _expectedLuceneCount, "Lucene.NET");
    }

    [Benchmark(Description = "LeanCorpus cold filter pipeline")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ApplyColdPipeline()
    {
        var filter = Scenario == "caching" ? new CachingTokenFilter() : _filter.Clone();
        return ValidateCount(ApplyLean(filter), _expectedLeanCount, "LeanCorpus cold");
    }

    [Benchmark(Description = "Lucene.NET cold filter pipeline")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_ApplyColdPipeline()
    {
        using var baseStream = BuildLuceneBaseStream(_luceneInput);
        using var filter = BuildLuceneFilter(baseStream);
        if (Scenario == "caching")
            baseStream.Reset();
        return ValidateCount(ApplyLucene(filter), _expectedLuceneCount, "Lucene.NET cold");
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
    private static Lucene.Net.Analysis.Tokenizer BuildLuceneBaseStream(string input)
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
            "classic-noop" =>
                new Lucene.Net.Analysis.Standard.ClassicFilter(input),

            "pattern-replace-noop" or "pattern-replace-mutating" =>
                new Lucene.Net.Analysis.Pattern.PatternReplaceFilter(
                    input,
                    new System.Text.RegularExpressions.Regex("[0-9]+", RegexOptions.None, TimeSpan.FromSeconds(1)),
                    "#",
                    all: true),

            "hyphenated-words" =>
                new Lucene.Net.Analysis.Miscellaneous.HyphenatedWordsFilter(input),

            "caching" =>
                new Lucene.Net.Analysis.CachingTokenFilter(input),

            _ => input
        };
    }

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
