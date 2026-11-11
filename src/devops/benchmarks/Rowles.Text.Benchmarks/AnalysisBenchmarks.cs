using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures analysis pipeline throughput: tokenisation + lowercase + stop-word removal.
/// Compares LeanCorpus StandardAnalyser against Lucene.NET StandardAnalyzer. Reading rows copy every
/// term; <c>_CountOnly</c> rows only count tokens. Setup proves equal output on the first 500
/// documents. Directional head-to-head rows.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class AnalysisBenchmarks
{
    public static IEnumerable<int> DocCounts => TextBenchmarkData.GetDocCounts(TextBenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private StandardAnalyser _leanAnalyser = null!;
    private StandardAnalyzer _luceneAnalyzer = null!;
    private CountingTokenSink _sink = null!;
    private ReadingTokenSink _readingSink = null!;

    [GlobalSetup]
    public void Setup()
    {
        _documents = TextBenchmarkData.BuildDocuments(DocumentCount);
        _leanAnalyser = new StandardAnalyser();
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        _sink = new CountingTokenSink();
        _readingSink = new ReadingTokenSink();
        TextOutputPreflight.Compare("analysis/standard", _documents,
            document => TextOutputPreflight.CaptureLean(sink => _leanAnalyser.Analyse(document.AsSpan(), sink)),
            document =>
            {
                using var stream = _luceneAnalyzer.GetTokenStream("body", new System.IO.StringReader(document));
                return TextOutputPreflight.CaptureLucene(stream);
            },
            PreflightAllowList.Cjk);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _luceneAnalyzer?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Analyse()
    {
        int totalTokens = 0;
        for (int i = 0; i < _documents.Length; i++)
        {
            _readingSink.Reset();
            _leanAnalyser.Analyse(_documents[i].AsSpan(), _readingSink);
            totalTokens += _readingSink.Count;
        }
        return totalTokens;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Analyse()
    {
        int totalTokens = 0;
        for (int i = 0; i < _documents.Length; i++)
        {
            using var reader = new System.IO.StringReader(_documents[i]);
            using var stream = _luceneAnalyzer.GetTokenStream("body", reader);
            totalTokens += LuceneTermReader.ReadAll(stream);
        }
        return totalTokens;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_Analyse_CountOnly()
    {
        int totalTokens = 0;
        for (int i = 0; i < _documents.Length; i++)
        {
            _sink.Reset();
            _leanAnalyser.Analyse(_documents[i].AsSpan(), _sink);
            totalTokens += _sink.Count;
        }
        return totalTokens;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Analyse_CountOnly()
    {
        int totalTokens = 0;
        for (int i = 0; i < _documents.Length; i++)
        {
            using var reader = new System.IO.StringReader(_documents[i]);
            using var stream = _luceneAnalyzer.GetTokenStream("body", reader);
            stream.Reset();
            while (stream.IncrementToken())
                totalTokens++;
            stream.End();
        }
        return totalTokens;
    }
}
