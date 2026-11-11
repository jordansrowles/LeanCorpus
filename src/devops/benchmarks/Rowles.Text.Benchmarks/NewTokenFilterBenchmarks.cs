using System.Text.RegularExpressions;
using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using LeanToken = Rowles.LeanCorpus.Analysis.Token;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares token filter throughput between LeanCorpus span-based (<see cref="ISpanTokenFilter"/>)
/// and Lucene.NET streaming (<c>TokenFilter</c>) implementations for newly added filters, per token
/// over a pregenerated stream of <see cref="TokenFilterStreams.TokensPerInvoke"/> tokens replayed to
/// Lucene.NET with no tokeniser in timing. Setup proves equal terms and that the reused rows reach
/// their reset path. An A/A row repeats the LeanCorpus reused body. The hyphenated-words filters are
/// different algorithms, so that filter is measured by <see cref="HyphenatedWordsFilterBenchmarks"/>.
/// Directional head-to-head rows.
/// </summary>
/// <remarks>
/// Lucene.NET's <c>CachingTokenFilter</c> cannot clear its cache, so its reused row wraps the reused
/// source in a new cache each invocation; LeanCorpus resets its reused cache. Both capture and
/// forward every token.
/// </remarks>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class NewTokenFilterBenchmarks
{
    private const int Tokens = TokenFilterStreams.TokensPerInvoke;

    [Params(
        "classic-noop",
        "pattern-replace-noop",
        "pattern-replace-mutating",
        "caching")]
    public string Scenario { get; set; } = "classic-noop";

    private LeanToken[] _source = [];
    private Func<ISpanTokenFilter> _create = null!;
    private ISpanTokenFilter _reusedFilter = null!;
    private readonly CountingTokenSink _sink = new();
    private ReplayTokenStream _luceneSource = null!;
    private Lucene.Net.Analysis.TokenStream? _luceneReused;
    private int _expectedCount;

    [GlobalSetup]
    public void Setup()
    {
        (_source, _create) = Scenario switch
        {
            "classic-noop" => (Words("quick", "brown", "fox", "jumps"), (Func<ISpanTokenFilter>)(static () => new ClassicFilter())),
            "pattern-replace-noop" => (Words("hello", "world"), static () => new PatternReplaceFilter("[0-9]+", "#")),
            "pattern-replace-mutating" => (Words("call", "12345", "now", "then"), static () => new PatternReplaceFilter("[0-9]+", "#")),
            "caching" => (Words("alpha", "beta", "gamma", "delta"), static () => new CachingTokenFilter()),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'.")
        };

        var lean = TokenFilterStreams.CaptureLean(_source, _create());
        string[] lucene;
        var coldSource = new ReplayTokenStream(_source);
        using (var cold = BuildLuceneFilter(coldSource))
        {
            if (Scenario == "caching")
                coldSource.Reset();
            lucene = TokenFilterStreams.CaptureLucene(cold);
        }
        if (!lean.SequenceEqual(lucene, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Token-filter fixture '{Scenario}' is not comparable: LeanCorpus emitted {lean.Length} terms "
                + $"[{string.Join(", ", lean.Take(8))}], Lucene.NET emitted {lucene.Length} [{string.Join(", ", lucene.Take(8))}].");
        _expectedCount = lean.Length;

        _reusedFilter = _create();
        _luceneSource = new ReplayTokenStream(_source);
        if (Scenario != "caching")
            _luceneReused = BuildLuceneFilter(_luceneSource);

        // Run each reused path twice: the second pass must go through the reset path and match.
        for (int pass = 0; pass < 2; pass++)
        {
            LeanCorpus_Apply();
            LuceneNet_Apply();
        }
        if (_luceneSource.ResetCount != 2)
            throw new InvalidOperationException($"The reused Lucene.NET filter did not reset its source: {_luceneSource.ResetCount} resets.");
    }

    [GlobalCleanup]
    public void Cleanup() => _luceneReused?.Dispose();

    [Benchmark(Baseline = true, Description = "LeanCorpus reused filter", OperationsPerInvoke = Tokens)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Apply() => ApplyReused("LeanCorpus");

    [Benchmark(Description = "LeanCorpus reused filter (A/A)", OperationsPerInvoke = Tokens)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Apply_Repeat() => ApplyReused("LeanCorpus A/A");

    [Benchmark(Description = "Lucene.NET reused filter", OperationsPerInvoke = Tokens)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Apply()
    {
        if (_luceneReused is { } reused)
            return TokenFilterStreams.Validate(TokenFilterStreams.ApplyLucene(reused), _expectedCount, "Lucene.NET");

        // Caching: Lucene.NET's cache does not reset its input, so reset the reused source directly.
        _luceneSource.Reset();
        var cache = new Lucene.Net.Analysis.CachingTokenFilter(_luceneSource);
        return TokenFilterStreams.Validate(TokenFilterStreams.ApplyLucene(cache), _expectedCount, "Lucene.NET");
    }

    [Benchmark(Description = "LeanCorpus cold filter pipeline", OperationsPerInvoke = Tokens)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ApplyColdPipeline()
    {
        var sink = new CountingTokenSink();
        TokenFilterStreams.ApplyLean(_source, _create(), sink);
        return TokenFilterStreams.Validate(sink.Count, _expectedCount, "LeanCorpus cold");
    }

    [Benchmark(Description = "Lucene.NET cold filter pipeline", OperationsPerInvoke = Tokens)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_ApplyColdPipeline()
    {
        var source = new ReplayTokenStream(_source);
        using var filter = BuildLuceneFilter(source);
        if (Scenario == "caching")
            source.Reset();
        return TokenFilterStreams.Validate(TokenFilterStreams.ApplyLucene(filter), _expectedCount, "Lucene.NET cold");
    }

    private int ApplyReused(string row)
    {
        _sink.Reset();
        if (_reusedFilter is CachingTokenFilter caching)
            caching.Reset();
        TokenFilterStreams.ApplyLean(_source, _reusedFilter, _sink);
        return TokenFilterStreams.Validate(_sink.Count, _expectedCount, row);
    }

    private static LeanToken[] Words(params string[] terms)
        => TokenFilterStreams.Build(terms.Select(static term => (term, 1)).ToArray());

    private Lucene.Net.Analysis.TokenStream BuildLuceneFilter(Lucene.Net.Analysis.TokenStream input)
        => Scenario switch
        {
            "classic-noop" => new Lucene.Net.Analysis.Standard.ClassicFilter(input),
            "pattern-replace-noop" or "pattern-replace-mutating" =>
                new Lucene.Net.Analysis.Pattern.PatternReplaceFilter(
                    input, new Regex("[0-9]+", RegexOptions.None, TimeSpan.FromSeconds(1)), "#", all: true),
            "caching" => new Lucene.Net.Analysis.CachingTokenFilter(input),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'.")
        };
}

/// <summary>
/// Measures <see cref="HyphenatedWordsFilter"/> per token over a pregenerated stream. LeanCorpus-only:
/// Lucene.NET's <c>HyphenatedWordsFilter</c> is a different algorithm. Includes an A/A row.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class HyphenatedWordsFilterBenchmarks
{
    private const int Tokens = TokenFilterStreams.TokensPerInvoke;

    private LeanToken[] _source = [];
    private ISpanTokenFilter _reusedFilter = null!;
    private readonly CountingTokenSink _sink = new();
    private int _expectedCount;

    [GlobalSetup]
    public void Setup()
    {
        _source = TokenFilterStreams.Build(("state", 1), ("of", 0), ("the", 0), ("art", 0));
        _expectedCount = TokenFilterStreams.CaptureLean(_source, new HyphenatedWordsFilter('-')).Length;
        if (_expectedCount == 0)
            throw new InvalidOperationException("The hyphenated-words fixture emitted no tokens.");
        _reusedFilter = new HyphenatedWordsFilter('-');
        LeanCorpus_Apply();
        LeanCorpus_Apply();
    }

    [Benchmark(Baseline = true, Description = "LeanCorpus reused filter", OperationsPerInvoke = Tokens)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Apply() => ApplyReused("LeanCorpus");

    [Benchmark(Description = "LeanCorpus reused filter (A/A)", OperationsPerInvoke = Tokens)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Apply_Repeat() => ApplyReused("LeanCorpus A/A");

    [Benchmark(Description = "LeanCorpus cold filter pipeline", OperationsPerInvoke = Tokens)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ApplyColdPipeline()
    {
        var sink = new CountingTokenSink();
        TokenFilterStreams.ApplyLean(_source, new HyphenatedWordsFilter('-'), sink);
        return TokenFilterStreams.Validate(sink.Count, _expectedCount, "LeanCorpus cold");
    }

    private int ApplyReused(string row)
    {
        _sink.Reset();
        TokenFilterStreams.ApplyLean(_source, _reusedFilter, _sink);
        return TokenFilterStreams.Validate(_sink.Count, _expectedCount, row);
    }
}
