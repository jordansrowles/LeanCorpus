using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures analysis pipeline throughput: tokenisation + lowercase + stop-word removal.
/// Compares LeanCorpus StandardAnalyser against Lucene.NET StandardAnalyzer.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class AnalysisBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private StandardAnalyser _leanAnalyser = null!;
    private StandardAnalyzer _luceneAnalyzer = null!;
    private CountingTokenSink _sink = null!;

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _leanAnalyser = new StandardAnalyser();
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        _sink = new CountingTokenSink();
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
            _sink.Reset();
            _leanAnalyser.Analyse(_documents[i].AsSpan(), _sink);
            totalTokens += _sink.Count;
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
            stream.Reset();
            while (stream.IncrementToken())
                totalTokens++;
            stream.End();
        }
        return totalTokens;
    }
}
