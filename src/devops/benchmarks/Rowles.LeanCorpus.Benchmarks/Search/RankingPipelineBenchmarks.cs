using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Search.Ranking;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>Measures ranking-profile orchestration, rules, score functions, reranking and cache identity.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class RankingPipelineBenchmarks
{
    private const int TopN = 25;
    private const int CandidateBudget = 100;
    private string _path = string.Empty;
    private IndexSearcher? _searcher;
    private IndexSearcher? _cachedSearcher;
    private readonly TermQuery _query = new("body", "government");
    private RankingSearchRequest? _emptyRequest;
    private RankingSearchRequest? _rulesRequest;
    private RankingSearchRequest? _functionRequest;
    private RankingSearchRequest? _rescoreRequest;
    private RankingSearchRequest? _cachedRequest;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _path = Path.Combine(BenchmarkHelpers.TempRoot, $"ranking-pipeline-{Guid.NewGuid():N}");
        RecentFeatureBenchmarkIndex.Build(_path, BenchmarkData.BuildDocuments(DocumentCount));
        _searcher = new IndexSearcher(new MMapDirectory(_path));
        _cachedSearcher = new IndexSearcher(new MMapDirectory(_path),
            new IndexSearcherConfig { EnableQueryCache = true });

        var emptyProfile = new RankingProfile("web", "1");
        _emptyRequest = new RankingSearchRequest(_query, TopN, emptyProfile,
            context: new RankingRequestContext(safeCacheIdentity: "benchmark"));

        var rule = new QueryRule("curated", 10, new QueryRuleMatch("government"),
        [
            new FilterQueryRuleAction(new TermQuery("category", "category-0")),
            new ScoreQueryRuleAction(Enumerable.Range(0, CandidateBudget).Where(static id => id % 10 == 0).ToArray(), 1.5f),
            new PinQueryRuleAction(new Dictionary<int, int> { [0] = 1, [32] = 2 })
        ]);
        _rulesRequest = new RankingSearchRequest(_query, TopN, emptyProfile,
            new QueryRuleSet([rule]), new RankingRequestContext("government", safeCacheIdentity: "benchmark"));

        var functionPipeline = new RankingPipeline([
            new ScoreFunctionStage("rank", DoubleValuesSource.FromDoubleField("rank"),
                RankingScoreCombination.Add, CandidateBudget)
        ]);
        _functionRequest = new RankingSearchRequest(_query, TopN,
            new RankingProfile("web", "function", functionPipeline));

        var rescorePipeline = new RankingPipeline([
            new QueryRescorerStage("rescore", new QueryRescorer(new TermQuery("body", "people"), 1f, 2f), CandidateBudget)
        ]);
        _rescoreRequest = new RankingSearchRequest(_query, TopN,
            new RankingProfile("web", "rescore", rescorePipeline));

        _cachedRequest = new RankingSearchRequest(_query, TopN, new RankingProfile("web", "cached"),
            context: new RankingRequestContext(safeCacheIdentity: "benchmark"));
        _ = _cachedSearcher.Search(_cachedRequest);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _searcher?.Dispose();
        _cachedSearcher?.Dispose();
        RecentFeatureBenchmarkIndex.Delete(_path);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int DirectSearch() => _searcher!.Search(_query, TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int EmptyProfile() => _searcher!.Search(_emptyRequest!).TopDocs.TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int RulesFiltersScoresAndPins() => _searcher!.Search(_rulesRequest!).TopDocs.TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int ScoreFunctionPipeline() => _searcher!.Search(_functionRequest!).TopDocs.TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int QueryRescorerPipeline() => _searcher!.Search(_rescoreRequest!).TopDocs.TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int CachedProfileHit() => _cachedSearcher!.Search(_cachedRequest!).TopDocs.TotalHits;
}
