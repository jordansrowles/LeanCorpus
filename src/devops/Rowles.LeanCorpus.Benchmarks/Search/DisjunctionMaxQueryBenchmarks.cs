using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="DisjunctionMaxQuery"/> throughput against Lucene.NET <c>DisjunctionMaxQuery</c>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class DisjunctionMaxQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(0.0f, 0.1f, 0.5f)]
    public float TieBreakerMultiplier { get; set; } = 0.1f;

    private LeanIndexSearcher? _leanSearcher;

    [GlobalSetup]
    public void Setup()
    {
        SharedStandardIndex.EnsureInitialised(DocumentCount);
        _leanSearcher = SharedStandardIndex.LeanSearcher;
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        // All resources are owned by SharedStandardIndex; do not dispose.
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_DisjunctionMaxQuery()
    {
        var dmq = new Rowles.LeanCorpus.Search.Queries.DisjunctionMaxQuery(TieBreakerMultiplier);
        dmq.Add(new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "government"));
        dmq.Add(new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "market"));
        dmq.Add(new Rowles.LeanCorpus.Search.Queries.TermQuery("body", "people"));
        return _leanSearcher!.Search(dmq, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_DisjunctionMaxQuery()
    {
        var disjuncts = new Lucene.Net.Search.Query[]
        {
            new Lucene.Net.Search.TermQuery(new Term("body", "government")),
            new Lucene.Net.Search.TermQuery(new Term("body", "market")),
            new Lucene.Net.Search.TermQuery(new Term("body", "people"))
        };
        var q = new Lucene.Net.Search.DisjunctionMaxQuery(disjuncts, TieBreakerMultiplier);
        return SharedStandardIndex.LuceneSearcher.Search(q, TopN).TotalHits;
    }

}
