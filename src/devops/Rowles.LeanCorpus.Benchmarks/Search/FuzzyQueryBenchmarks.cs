using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures FuzzyQuery performance across deterministic edit-distance shapes.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class FuzzyQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("short-edit1-common", "medium-edit1-common", "medium-edit2-common", "long-edit1-common", "nohit-edit2")]
    public string Scenario { get; set; } = "medium-edit1-common";

    private LeanIndexSearcher? _leanSearcher;
    private Rowles.LeanCorpus.Search.Queries.FuzzyQuery? _leanQuery;
    private Lucene.Net.Search.FuzzyQuery? _luceneQuery;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
        var scenario = ResolveScenario(Scenario);
        _leanQuery = new Rowles.LeanCorpus.Search.Queries.FuzzyQuery("body", scenario.QueryTerm, scenario.MaxEdits);
        _luceneQuery = new Lucene.Net.Search.FuzzyQuery(new Term("body", scenario.QueryTerm), scenario.MaxEdits);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // All resources are owned by SharedStandardIndex; do not dispose.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_FuzzyQuery()
    {
        return _leanSearcher!.Search(_leanQuery!, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_FuzzyQuery()
    {
        return SharedStandardIndex.LuceneSearcher.Search(_luceneQuery!, TopN).TotalHits;
    }

    private static (string QueryTerm, int MaxEdits) ResolveScenario(string scenario) => scenario switch
    {
        "short-edit1-common" => ("marke", 1),
        "medium-edit1-common" => ("goverment", 1),
        "medium-edit2-common" => ("presdnt", 2),
        "long-edit1-common" => ("econmic", 1),
        "nohit-edit2" => ("zzzznomatch", 2),
        _ => throw new InvalidOperationException($"Unknown fuzzy benchmark scenario '{scenario}'.")
    };

}
