using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Ar;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.De;
using Lucene.Net.Analysis.Es;
using Lucene.Net.Analysis.Fr;
using Lucene.Net.Analysis.It;
using Lucene.Net.Analysis.Nl;
using Lucene.Net.Analysis.Pt;
using Lucene.Net.Analysis.Ru;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Util;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures analysis pipeline throughput across all twelve languages supported
/// by <see cref="AnalyserFactory"/>. Each iteration analyses a representative
/// sample paragraph for the language under test.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class MultiLanguageAnalysisBenchmarks
{
    public static IEnumerable<string> Languages => RowlesTextMultilingualProfile.Languages;

    [ParamsSource(nameof(Languages))]
    public string Language { get; set; } = "en";

    private IAnalyser _analyser = null!;
    private string _sample = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _analyser = AnalyserFactory.Create(Language);
        _sample = TextBenchmarkData.BuildMultilingualInputs(Language, 1)[0];
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Analyse()
    {
        int total = 0;
        // Repeat the sample to amortise per-call overhead.
        for (int i = 0; i < 100; i++)
        {
            var sink = new CountingTokenSink();
            _analyser.Analyse(_sample.AsSpan(), sink);
            total += sink.Count;
        }
        return total;
    }

    [Benchmark(Description = "Lucene.NET Analyse")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Analyse()
    {
        int total = 0;
        if (LuceneAnalysers.TryGetValue(Language, out var factory))
        {
            var analyser = factory();
            for (int i = 0; i < 100; i++)
            {
                using var ts = analyser.GetTokenStream("text", new System.IO.StringReader(_sample));
                ts.Reset();
                while (ts.IncrementToken())
                    total++;
                ts.End();
            }
        }
        return total;
    }

    private static readonly Dictionary<string, Func<Analyzer>> LuceneAnalysers = new()
    {
        ["en"] = () => new StandardAnalyzer(LuceneVersion.LUCENE_48),
        ["fr"] = () => new FrenchAnalyzer(LuceneVersion.LUCENE_48),
        ["de"] = () => new GermanAnalyzer(LuceneVersion.LUCENE_48),
        ["es"] = () => new SpanishAnalyzer(LuceneVersion.LUCENE_48),
        ["it"] = () => new ItalianAnalyzer(LuceneVersion.LUCENE_48),
        ["pt"] = () => new PortugueseAnalyzer(LuceneVersion.LUCENE_48),
        ["nl"] = () => new DutchAnalyzer(LuceneVersion.LUCENE_48),
        ["ru"] = () => new RussianAnalyzer(LuceneVersion.LUCENE_48),
        ["ar"] = () => new ArabicAnalyzer(LuceneVersion.LUCENE_48),
        // zh/ja/ko use StandardAnalyzer — language-specific packages not referenced.
        ["zh"] = () => new StandardAnalyzer(LuceneVersion.LUCENE_48),
        ["ja"] = () => new StandardAnalyzer(LuceneVersion.LUCENE_48),
        ["ko"] = () => new StandardAnalyzer(LuceneVersion.LUCENE_48),
    };

}
