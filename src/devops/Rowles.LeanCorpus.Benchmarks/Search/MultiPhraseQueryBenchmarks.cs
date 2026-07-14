using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="MultiPhraseQuery"/> throughput against Lucene.NET <c>MultiPhraseQuery</c>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class MultiPhraseQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

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
    public int LeanCorpus_MultiPhraseQuery()
    {
        // Slot 0: "united" or "federal"; Slot 1: "states" or "government"
        var q = new Rowles.LeanCorpus.Search.Queries.MultiPhraseQuery(
            "body",
            [["united", "federal"], ["states", "government"]]);
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_MultiPhraseQuery()
    {
        var q = new Lucene.Net.Search.MultiPhraseQuery();
        q.Add(new[] { new Term("body", "united"), new Term("body", "federal") });
        q.Add(new[] { new Term("body", "states"), new Term("body", "government") });
        return SharedStandardIndex.LuceneSearcher.Search(q, TopN).TotalHits;
    }
}
