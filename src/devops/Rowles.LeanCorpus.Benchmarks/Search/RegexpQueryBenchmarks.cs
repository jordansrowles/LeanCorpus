using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanRegexpQuery = Rowles.LeanCorpus.Search.Queries.RegexpQuery;
using LuceneRegexpQuery = Lucene.Net.Search.RegexpQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="RegexpQuery"/> throughput against Lucene.NET <c>RegexpQuery</c>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class RegexpQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("gov.*ment", "mark.*", ".*nation.*")]
    public string Pattern { get; set; } = "gov.*ment";

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
    public int LeanCorpus_RegexpQuery()
        => _leanSearcher!.Search(new LeanRegexpQuery("body", Pattern), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_RegexpQuery()
    {
        var q = new LuceneRegexpQuery(new Term("body", Pattern));
        return SharedStandardIndex.LuceneSearcher.Search(q, TopN).TotalHits;
    }
}
