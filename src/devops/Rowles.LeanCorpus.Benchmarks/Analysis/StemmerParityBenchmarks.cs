using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.En;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="StemmedAnalyser"/> throughput against Lucene.NET <c>EnglishAnalyzer</c>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class StemmerParityBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private StemmedAnalyser _leanAnalyser = null!;
    private EnglishAnalyzer _luceneAnalyser = null!;

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _leanAnalyser = new StemmedAnalyser();
        _luceneAnalyser = new EnglishAnalyzer(LuceneVersion.LUCENE_48);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _luceneAnalyser.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_StemmedAnalyser()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            var sink = new CountingTokenSink();
            _leanAnalyser.Analyse(doc.AsSpan(), sink);
            total += sink.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_EnglishAnalyzer()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var ts = _luceneAnalyser.GetTokenStream("body", doc);
            ts.Reset();
            while (ts.IncrementToken())
                total++;
            ts.End();
        }
        return total;
    }
}
