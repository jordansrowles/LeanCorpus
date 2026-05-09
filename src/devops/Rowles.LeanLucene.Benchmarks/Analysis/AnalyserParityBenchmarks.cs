using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Util;
using LeanKeywordAnalyser = Rowles.LeanLucene.Analysis.Analysers.KeywordAnalyser;
using LeanSimpleAnalyser = Rowles.LeanLucene.Analysis.Analysers.SimpleAnalyser;
using LeanWhitespaceAnalyser = Rowles.LeanLucene.Analysis.Analysers.WhitespaceAnalyser;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures lightweight analyser throughput against Lucene.NET equivalents.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class AnalyserParityBenchmarks
{
    private const string Sample = "The QUICK brown-fox jumps over ID-123 and punctuation!";

    private LeanWhitespaceAnalyser _leanWhitespace = null!;
    private LeanKeywordAnalyser _leanKeyword = null!;
    private LeanSimpleAnalyser _leanSimple = null!;
    private WhitespaceAnalyzer _luceneWhitespace = null!;
    private KeywordAnalyzer _luceneKeyword = null!;
    private SimpleAnalyzer _luceneSimple = null!;

    [GlobalSetup]
    public void Setup()
    {
        _leanWhitespace = new LeanWhitespaceAnalyser();
        _leanKeyword = new LeanKeywordAnalyser();
        _leanSimple = new LeanSimpleAnalyser();
        _luceneWhitespace = new WhitespaceAnalyzer(LuceneVersion.LUCENE_48);
        _luceneKeyword = new KeywordAnalyzer();
        _luceneSimple = new SimpleAnalyzer(LuceneVersion.LUCENE_48);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _luceneWhitespace.Dispose();
        _luceneKeyword.Dispose();
        _luceneSimple.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Whitespace()
        => AnalyseLean(_leanWhitespace);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Whitespace()
        => AnalyseLucene(_luceneWhitespace);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Keyword()
        => AnalyseLean(_leanKeyword);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Keyword()
        => AnalyseLucene(_luceneKeyword);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Simple()
        => AnalyseLean(_leanSimple);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Simple()
        => AnalyseLucene(_luceneSimple);

    private static int AnalyseLean(Rowles.LeanLucene.Analysis.Analysers.IAnalyser analyser)
    {
        int total = 0;
        for (int i = 0; i < 100; i++)
            total += analyser.Analyse(Sample.AsSpan()).Count;
        return total;
    }

    private static int AnalyseLucene(Analyzer analyser)
    {
        int total = 0;
        for (int i = 0; i < 100; i++)
        {
            using var reader = new StringReader(Sample);
            using var stream = analyser.GetTokenStream("body", reader);
            stream.Reset();
            while (stream.IncrementToken())
                total++;
            stream.End();
        }

        return total;
    }
}
