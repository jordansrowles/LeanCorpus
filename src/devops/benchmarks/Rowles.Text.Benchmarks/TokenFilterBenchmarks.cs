using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using LeanToken = Rowles.LeanCorpus.Analysis.Token;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares token filter throughput between LeanCorpus batch (<see cref="ISpanTokenFilter"/>) and
/// Lucene.NET streaming implementations, per token over a pregenerated stream of
/// <see cref="TokenFilterStreams.TokensPerInvoke"/> tokens. Lucene.NET reads the same tokens from a
/// <see cref="ReplayTokenStream"/>, so no tokeniser runs inside timing. Setup proves equal terms and
/// that the reused rows reach their reset path. Reused and cold pipeline construction are measured
/// separately, and an A/A row repeats the LeanCorpus reused body. Directional head-to-head rows.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class TokenFilterBenchmarks
{
    private const int Tokens = TokenFilterStreams.TokensPerInvoke;

    [Params(
        "length-noop",
        "length-mutating",
        "truncate-noop",
        "truncate-mutating",
        "unique-mutating",
        "reverse-mutating",
        "elision-mutating")]
    public string Scenario { get; set; } = "length-noop";

    private LeanToken[] _source = [];
    private ISpanTokenFilter _filter = null!;
    private ISpanTokenFilter _reusedFilter = null!;
    private readonly CountingTokenSink _sink = new();
    private ReplayTokenStream _luceneSource = null!;
    private Lucene.Net.Analysis.TokenStream _luceneReused = null!;
    private int _expectedCount;

    [GlobalSetup]
    public void Setup()
    {
        (_source, _filter) = Scenario switch
        {
            "length-noop" => (Words("quick", "brown", "fox", "jumps"), (ISpanTokenFilter)new LengthFilter(2, 8)),
            "length-mutating" => (Words("a", "quick", "extraordinary", "dog"), new LengthFilter(2, 8)),
            "truncate-noop" => (Words("quick", "brown", "fox", "jumps"), new TruncateTokenFilter(12)),
            "truncate-mutating" => (Words("extraordinary", "token"), new TruncateTokenFilter(6)),
            // Duplicates at the same position (increment 0), which both filters remove.
            "unique-mutating" => (TokenFilterStreams.Build(("fast", 1), ("fast", 0), ("quick", 1), ("rapid", 1)), new UniqueTokenFilter()),
            "reverse-mutating" => (Words("abcdef", "café"), new ReverseStringFilter()),
            "elision-mutating" => (Words("l'avion", "qu’elle"), new ElisionFilter()),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'.")
        };

        var lean = TokenFilterStreams.CaptureLean(_source, _filter.Clone());
        string[] lucene;
        using (var cold = BuildLuceneFilter(new ReplayTokenStream(_source)))
            lucene = TokenFilterStreams.CaptureLucene(cold);
        if (!lean.SequenceEqual(lucene, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Token-filter fixture '{Scenario}' is not comparable: LeanCorpus emitted {lean.Length} terms "
                + $"[{string.Join(", ", lean.Take(8))}], Lucene.NET emitted {lucene.Length} [{string.Join(", ", lucene.Take(8))}].");
        _expectedCount = lean.Length;
        if (Scenario == "unique-mutating" && _expectedCount != Tokens / 4 * 3)
            throw new InvalidOperationException($"unique-mutating must remove one same-position duplicate per group; emitted {_expectedCount} of {Tokens}.");

        _reusedFilter = _filter.Clone();
        _luceneSource = new ReplayTokenStream(_source);
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
        => TokenFilterStreams.Validate(TokenFilterStreams.ApplyLucene(_luceneReused), _expectedCount, "Lucene.NET");

    [Benchmark(Description = "LeanCorpus cold filter pipeline", OperationsPerInvoke = Tokens)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ApplyColdPipeline()
    {
        var sink = new CountingTokenSink();
        TokenFilterStreams.ApplyLean(_source, _filter.Clone(), sink);
        return TokenFilterStreams.Validate(sink.Count, _expectedCount, "LeanCorpus cold");
    }

    [Benchmark(Description = "Lucene.NET cold filter pipeline", OperationsPerInvoke = Tokens)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_ApplyColdPipeline()
    {
        using var filter = BuildLuceneFilter(new ReplayTokenStream(_source));
        return TokenFilterStreams.Validate(TokenFilterStreams.ApplyLucene(filter), _expectedCount, "Lucene.NET cold");
    }

    private int ApplyReused(string row)
    {
        _sink.Reset();
        TokenFilterStreams.ApplyLean(_source, _reusedFilter, _sink);
        return TokenFilterStreams.Validate(_sink.Count, _expectedCount, row);
    }

    private static LeanToken[] Words(params string[] terms)
        => TokenFilterStreams.Build(terms.Select(static term => (term, 1)).ToArray());

    private Lucene.Net.Analysis.TokenStream BuildLuceneFilter(Lucene.Net.Analysis.TokenStream input)
        => Scenario switch
        {
            "length-noop" or "length-mutating" =>
                new Lucene.Net.Analysis.Miscellaneous.LengthFilter(Lucene.Net.Util.LuceneVersion.LUCENE_48, input, 2, 8),
            "truncate-noop" or "truncate-mutating" =>
                new Lucene.Net.Analysis.Miscellaneous.TruncateTokenFilter(input, Scenario == "truncate-mutating" ? 6 : 12),
            "unique-mutating" =>
                new Lucene.Net.Analysis.Miscellaneous.RemoveDuplicatesTokenFilter(input),
            "reverse-mutating" =>
                new Lucene.Net.Analysis.Reverse.ReverseStringFilter(Lucene.Net.Util.LuceneVersion.LUCENE_48, input),
            "elision-mutating" =>
                new Lucene.Net.Analysis.Util.ElisionFilter(input,
                    new Lucene.Net.Analysis.Util.CharArraySet(
                        Lucene.Net.Util.LuceneVersion.LUCENE_48,
                        ["l", "m", "t", "qu", "n", "s", "j", "d", "c", "jusqu", "quoiqu", "lorsqu", "puisqu"],
                        ignoreCase: true)),
            _ => throw new InvalidOperationException($"Unknown scenario '{Scenario}'.")
        };
}
