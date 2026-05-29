using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Search.Highlighting;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="Highlighter.GetBestFragment"/> throughput across different snippet lengths
/// and query-term densities.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class HighlighterBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Maximum character length of the returned snippet.</summary>
    [Params(100, 200, 500)]
    public int MaxSnippetLength { get; set; } = 200;

    private string[] _documents = [];
    private Highlighter _highlighter = null!;

    // Simulate a two-term and a five-term query
    private static readonly IReadOnlySet<string> TwoTerms =
        new HashSet<string>(["government", "market"], StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlySet<string> FiveTerms =
        new HashSet<string>(["government", "market", "people", "national", "said"], StringComparer.OrdinalIgnoreCase);

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _highlighter = new Highlighter("<b>", "</b>");
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Highlight_TwoTerms()
    {
        int total = 0;
        foreach (var doc in _documents)
            total += _highlighter.GetBestFragment(doc, TwoTerms, MaxSnippetLength).Length;
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Highlight_FiveTerms()
    {
        int total = 0;
        foreach (var doc in _documents)
            total += _highlighter.GetBestFragment(doc, FiveTerms, MaxSnippetLength).Length;
        return total;
    }
}
