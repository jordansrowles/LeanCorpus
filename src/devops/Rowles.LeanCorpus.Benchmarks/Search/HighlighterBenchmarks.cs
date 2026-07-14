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

        // Lucene.NET highlighter setup
        _luceneAnalyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(Lucene.Net.Util.LuceneVersion.LUCENE_48);
        _luceneTwoTermQuery = BuildLuceneQuery(TwoTerms);
        _luceneFiveTermQuery = BuildLuceneQuery(FiveTerms);
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

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Highlight_TwoTerms()
    {
        int total = 0;
        var scorer = new Lucene.Net.Search.Highlight.QueryScorer(_luceneTwoTermQuery);
        var formatter = new Lucene.Net.Search.Highlight.SimpleHTMLFormatter("<b>", "</b>");
        var hl = new Lucene.Net.Search.Highlight.Highlighter(formatter, scorer);
        foreach (var doc in _documents)
        {
            var fragment = hl.GetBestFragment(_luceneAnalyzer, "body", doc);
            if (fragment is not null)
                total += fragment.Length;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Highlight_FiveTerms()
    {
        int total = 0;
        var scorer = new Lucene.Net.Search.Highlight.QueryScorer(_luceneFiveTermQuery);
        var formatter = new Lucene.Net.Search.Highlight.SimpleHTMLFormatter("<b>", "</b>");
        var hl = new Lucene.Net.Search.Highlight.Highlighter(formatter, scorer);
        foreach (var doc in _documents)
        {
            var fragment = hl.GetBestFragment(_luceneAnalyzer, "body", doc);
            if (fragment is not null)
                total += fragment.Length;
        }
        return total;
    }

    // --- Lucene.NET state ---

    private Lucene.Net.Analysis.Standard.StandardAnalyzer _luceneAnalyzer = null!;
    private Lucene.Net.Search.Query _luceneTwoTermQuery = null!;
    private Lucene.Net.Search.Query _luceneFiveTermQuery = null!;

    private static Lucene.Net.Search.Query BuildLuceneQuery(IReadOnlySet<string> terms)
    {
        var bq = new Lucene.Net.Search.BooleanQuery();
        foreach (var term in terms)
            bq.Add(new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("body", term)), Lucene.Net.Search.Occur.SHOULD);
        return bq;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _luceneAnalyzer?.Dispose();
    }
}
