using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures PhraseQuery performance: exact and slop phrase matching.
/// Compares LeanCorpus vs Lucene.NET (Lifti lacks first-class phrase support).
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class PhraseQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("ExactTwoWord", "ExactThreeWord", "SlopTwoWord")]
    public string PhraseType { get; set; } = "ExactTwoWord";

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
    public int LeanCorpus_PhraseQuery()
    {
        return PhraseType switch
        {
            "ExactTwoWord" => _leanSearcher!.Search(
                new Rowles.LeanCorpus.Search.Queries.PhraseQuery("body", "new", "york"), TopN).TotalHits,
            "ExactThreeWord" => _leanSearcher!.Search(
                new Rowles.LeanCorpus.Search.Queries.PhraseQuery("body", "new", "york", "stock"), TopN).TotalHits,
            "SlopTwoWord" => _leanSearcher!.Search(
                new Rowles.LeanCorpus.Search.Queries.PhraseQuery("body", slop: 2, "said", "government"), TopN).TotalHits,
            _ => 0
        };
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_PhraseQuery()
    {
        var pq = new Lucene.Net.Search.PhraseQuery();
        switch (PhraseType)
        {
            case "ExactTwoWord":
                pq.Add(new Term("body", "new"));
                pq.Add(new Term("body", "york"));
                break;
            case "ExactThreeWord":
                pq.Add(new Term("body", "new"));
                pq.Add(new Term("body", "york"));
                pq.Add(new Term("body", "stock"));
                break;
            case "SlopTwoWord":
                pq.Slop = 2;
                pq.Add(new Term("body", "said"));
                pq.Add(new Term("body", "government"));
                break;
        }
        return SharedStandardIndex.LuceneSearcher.Search(pq, TopN).TotalHits;
    }
}
