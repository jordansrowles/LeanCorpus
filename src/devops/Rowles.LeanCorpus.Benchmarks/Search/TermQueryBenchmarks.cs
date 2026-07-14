using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanTermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;

namespace Rowles.LeanCorpus.Benchmarks;

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class TermQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("said", "government", "people")]
    public string QueryTerm { get; set; } = "said";

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
    public int LeanCorpus_TermQuery()
    {
        var topDocs = _leanSearcher!.Search(new LeanTermQuery("body", QueryTerm), TopN);
        return topDocs.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_TermQuery()
    {
        var query = new LuceneTermQuery(new Term("body", QueryTerm));
        var topDocs = SharedStandardIndex.LuceneSearcher.Search(query, TopN);
        return topDocs.TotalHits;
    }
}
