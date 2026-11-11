using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.En;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="StemmedAnalyser"/> throughput against Lucene.NET <c>EnglishAnalyzer</c>.
/// Reading rows copy every term; <c>_CountOnly</c> rows allocate a counting sink per document and
/// only count tokens. Setup proves equal output on the first 500 documents. Directional
/// head-to-head rows.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class StemmerParityBenchmarks
{
    public static IEnumerable<int> DocCounts => TextBenchmarkData.GetDocCounts(TextBenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private StemmedAnalyser _leanAnalyser = null!;
    private EnglishAnalyzer _luceneAnalyser = null!;
    private readonly ReadingTokenSink _readingSink = new();

    [GlobalSetup]
    public void Setup()
    {
        _documents = TextBenchmarkData.BuildDocuments(DocumentCount);
        _leanAnalyser = new StemmedAnalyser();
        _luceneAnalyser = new EnglishAnalyzer(LuceneVersion.LUCENE_48);
        TextOutputPreflight.Compare("stemmer/english", _documents,
            document => TextOutputPreflight.CaptureLean(sink => _leanAnalyser.Analyse(document.AsSpan(), sink)),
            document =>
            {
                using var stream = _luceneAnalyser.GetTokenStream("body", document);
                return TextOutputPreflight.CaptureLucene(stream);
            },
            PreflightAllowList.Cjk);
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
            _readingSink.Reset();
            _leanAnalyser.Analyse(doc.AsSpan(), _readingSink);
            total += _readingSink.Count;
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
            total += LuceneTermReader.ReadAll(ts);
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_StemmedAnalyser_CountOnly()
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
    public int LuceneNet_EnglishAnalyzer_CountOnly()
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
