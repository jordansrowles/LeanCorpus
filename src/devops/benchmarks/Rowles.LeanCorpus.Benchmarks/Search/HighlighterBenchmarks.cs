using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Search.Highlighting;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures <see cref="Highlighter.GetBestFragment"/> throughput across different snippet lengths
/// and query-term densities. Each invocation highlights <see cref="DocumentsPerOperation"/>
/// documents and rows report the time per document. Both engines honour
/// <see cref="MaxSnippetLength"/>. Reused-instance rows build the highlighter in setup;
/// construction-included rows build it on every invocation. Outputs differ between engines, so
/// rows are directional only.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class HighlighterBenchmarks
{
    private const int DocumentsPerOperation = 100;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Maximum character length of the returned snippet.</summary>
    [Params(100, 200, 500)]
    public int MaxSnippetLength { get; set; } = 200;

    private string[] _documents = [];
    private Highlighter _highlighter = null!;
    private int _documentOffset;

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
        _luceneTwoTermHighlighter = CreateLuceneHighlighter(_luceneTwoTermQuery, MaxSnippetLength);
        _luceneFiveTermHighlighter = CreateLuceneHighlighter(_luceneFiveTermQuery, MaxSnippetLength);

        RunPreflight();
    }

    [Benchmark(Baseline = true, OperationsPerInvoke = DocumentsPerOperation)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Highlight_TwoTerms()
        => HighlightLean(_highlighter, TwoTerms);

    [Benchmark(OperationsPerInvoke = DocumentsPerOperation)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Highlight_FiveTerms()
        => HighlightLean(_highlighter, FiveTerms);

    [Benchmark(OperationsPerInvoke = DocumentsPerOperation, Description = "LeanCorpus two terms, construction included")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Highlight_TwoTerms_Constructed()
        => HighlightLean(new Highlighter("<b>", "</b>"), TwoTerms);

    [Benchmark(OperationsPerInvoke = DocumentsPerOperation, Description = "LeanCorpus five terms, construction included")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Highlight_FiveTerms_Constructed()
        => HighlightLean(new Highlighter("<b>", "</b>"), FiveTerms);

    [Benchmark(OperationsPerInvoke = DocumentsPerOperation)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Highlight_TwoTerms()
        => HighlightLucene(_luceneTwoTermHighlighter);

    [Benchmark(OperationsPerInvoke = DocumentsPerOperation)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Highlight_FiveTerms()
        => HighlightLucene(_luceneFiveTermHighlighter);

    [Benchmark(OperationsPerInvoke = DocumentsPerOperation, Description = "Lucene.NET two terms, construction included")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Highlight_TwoTerms_Constructed()
        => HighlightLucene(CreateLuceneHighlighter(_luceneTwoTermQuery, MaxSnippetLength));

    [Benchmark(OperationsPerInvoke = DocumentsPerOperation, Description = "Lucene.NET five terms, construction included")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Highlight_FiveTerms_Constructed()
        => HighlightLucene(CreateLuceneHighlighter(_luceneFiveTermQuery, MaxSnippetLength));

    private int HighlightLean(Highlighter highlighter, IReadOnlySet<string> terms)
    {
        int total = 0;
        int offset = NextDocumentOffset();
        for (int i = 0; i < DocumentsPerOperation; i++)
        {
            string doc = _documents[(offset + i) % _documents.Length];
            total += highlighter.GetBestFragment(doc, terms, MaxSnippetLength).Length;
        }
        return total;
    }

    private int HighlightLucene(Lucene.Net.Search.Highlight.Highlighter highlighter)
    {
        int total = 0;
        int offset = NextDocumentOffset();
        for (int i = 0; i < DocumentsPerOperation; i++)
        {
            string doc = _documents[(offset + i) % _documents.Length];
            var fragment = highlighter.GetBestFragment(_luceneAnalyzer, "body", doc);
            if (fragment is not null)
                total += fragment.Length;
        }
        return total;
    }

    private int NextDocumentOffset()
    {
        int offset = _documentOffset;
        _documentOffset = (_documentOffset + DocumentsPerOperation) % _documents.Length;
        return offset;
    }

    /// <summary>Runs every row once outside timing and fails when a row produces no output.</summary>
    private void RunPreflight()
    {
        string caseName = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"MaxSnippetLength={MaxSnippetLength}");
        BenchmarkFixtureEvidence.RequireWork("highlighter", string.Concat(caseName, " two terms"), BenchmarkFixtureEvidence.LeanCorpusEngine, LeanCorpus_Highlight_TwoTerms());
        BenchmarkFixtureEvidence.RequireWork("highlighter", string.Concat(caseName, " five terms"), BenchmarkFixtureEvidence.LeanCorpusEngine, LeanCorpus_Highlight_FiveTerms());
        BenchmarkFixtureEvidence.RequireWork("highlighter", string.Concat(caseName, " two terms"), BenchmarkFixtureEvidence.LuceneNetEngine, LuceneNet_Highlight_TwoTerms());
        BenchmarkFixtureEvidence.RequireWork("highlighter", string.Concat(caseName, " five terms"), BenchmarkFixtureEvidence.LuceneNetEngine, LuceneNet_Highlight_FiveTerms());
        _documentOffset = 0;
    }

    // --- Lucene.NET state ---

    private Lucene.Net.Analysis.Standard.StandardAnalyzer _luceneAnalyzer = null!;
    private Lucene.Net.Search.Query _luceneTwoTermQuery = null!;
    private Lucene.Net.Search.Query _luceneFiveTermQuery = null!;
    private Lucene.Net.Search.Highlight.Highlighter _luceneTwoTermHighlighter = null!;
    private Lucene.Net.Search.Highlight.Highlighter _luceneFiveTermHighlighter = null!;

    /// <summary>Builds a Lucene.NET highlighter whose fragmenter honours the snippet length.</summary>
    internal static Lucene.Net.Search.Highlight.Highlighter CreateLuceneHighlighter(Lucene.Net.Search.Query query, int maxSnippetLength)
        => new(new Lucene.Net.Search.Highlight.SimpleHTMLFormatter("<b>", "</b>"), new Lucene.Net.Search.Highlight.QueryScorer(query))
        {
            TextFragmenter = new Lucene.Net.Search.Highlight.SimpleFragmenter(maxSnippetLength)
        };

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
