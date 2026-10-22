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
    private long _noHitQueryOrdinal;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
        var scenario = ResolveScenario(Scenario);
        _leanQuery = new Rowles.LeanCorpus.Search.Queries.FuzzyQuery("body", scenario.QueryTerm, scenario.MaxEdits);
        _luceneQuery = new Lucene.Net.Search.FuzzyQuery(new Term("body", scenario.QueryTerm), scenario.MaxEdits);
        if (Scenario == "nohit-edit2"
            && (_leanSearcher.Search(_leanQuery!, TopN).TotalHits != 0
                || SharedStandardIndex.LuceneSearcher.Search(_luceneQuery!, TopN).TotalHits != 0))
            throw new InvalidOperationException("The no-hit fuzzy benchmark setup query returned a result.");
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
        if (Scenario == "nohit-edit2")
        {
            // Core caches empty fuzzy expansions, so give each measured query a fresh term.
            var query = new Rowles.LeanCorpus.Search.Queries.FuzzyQuery("body", NextNoHitTerm(), maxEdits: 2);
            return _leanSearcher!.Search(query, TopN).TotalHits;
        }
        return _leanSearcher!.Search(_leanQuery!, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_FuzzyQuery()
    {
        if (Scenario == "nohit-edit2")
        {
            var query = new Lucene.Net.Search.FuzzyQuery(new Term("body", NextNoHitTerm()), maxEdits: 2);
            return SharedStandardIndex.LuceneSearcher.Search(query, TopN).TotalHits;
        }
        return SharedStandardIndex.LuceneSearcher.Search(_luceneQuery!, TopN).TotalHits;
    }

    private string NextNoHitTerm() => $"zzzznomatch{Interlocked.Increment(ref _noHitQueryOrdinal):x16}";

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
