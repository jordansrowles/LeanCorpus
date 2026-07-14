using BenchmarkDotNet.Attributes;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Search.Spans;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanSpanQuery = Rowles.LeanCorpus.Search.Queries.SpanQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="SpanNearQuery"/>, <see cref="SpanOrQuery"/>, and <see cref="SpanNotQuery"/>
/// throughput against Lucene.NET span queries.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class SpanQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("Near", "Or", "Not")]
    public string SpanType { get; set; } = "Near";

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
    public int LeanCorpus_SpanQuery()
    {
        var q = BuildLeanSpanQuery(SpanType);
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SpanQuery()
    {
        var q = BuildLuceneSpanQuery(SpanType);
        return SharedStandardIndex.LuceneSearcher.Search(q, TopN).TotalHits;
    }

    private static LeanSpanQuery BuildLeanSpanQuery(string type)
    {
        var t1 = new Rowles.LeanCorpus.Search.Queries.SpanTermQuery("body", "government");
        var t2 = new Rowles.LeanCorpus.Search.Queries.SpanTermQuery("body", "people");
        var t3 = new Rowles.LeanCorpus.Search.Queries.SpanTermQuery("body", "market");

        return type switch
        {
            "Near" => new Rowles.LeanCorpus.Search.Queries.SpanNearQuery([t1, t2], slop: 5, inOrder: true),
            "Or"   => new Rowles.LeanCorpus.Search.Queries.SpanOrQuery(t1, t2, t3),
            "Not"  => new Rowles.LeanCorpus.Search.Queries.SpanNotQuery(new Rowles.LeanCorpus.Search.Queries.SpanNearQuery([t1, t2], slop: 10), t3),
            _      => throw new InvalidOperationException($"Unknown span type '{type}'.")
        };
    }

    private static Lucene.Net.Search.Query BuildLuceneSpanQuery(string type)
    {
        var t1 = new Lucene.Net.Search.Spans.SpanTermQuery(new Term("body", "government"));
        var t2 = new Lucene.Net.Search.Spans.SpanTermQuery(new Term("body", "people"));
        var t3 = new Lucene.Net.Search.Spans.SpanTermQuery(new Term("body", "market"));

        return type switch
        {
            "Near" => new Lucene.Net.Search.Spans.SpanNearQuery([t1, t2], slop: 5, inOrder: true),
            "Or"   => new Lucene.Net.Search.Spans.SpanOrQuery(t1, t2, t3),
            "Not"  => new Lucene.Net.Search.Spans.SpanNotQuery(new Lucene.Net.Search.Spans.SpanNearQuery([t1, t2], 10, true), t3),
            _      => throw new InvalidOperationException($"Unknown span type '{type}'.")
        };
    }
}
