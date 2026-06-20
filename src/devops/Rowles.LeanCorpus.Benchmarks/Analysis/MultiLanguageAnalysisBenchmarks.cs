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
[SimpleJob]
public class MultiLanguageAnalysisBenchmarks
{
    public static IEnumerable<string> Languages => AnalyserFactory.SupportedLanguages;

    [ParamsSource(nameof(Languages))]
    public string Language { get; set; } = "en";

    private IAnalyser _analyser = null!;
    private string _sample = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _analyser = AnalyserFactory.Create(Language);
        _sample = Samples[Language];
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

    private static readonly Dictionary<string, string> Samples = new()
    {
        ["en"] = "The running foxes jumped over the lazy dogs in the warm afternoon sun while children watched.",
        ["fr"] = "Les enfants regardaient les renards courir sur la colline pendant que le soleil se couchait lentement.",
        ["de"] = "Die schnellen Füchse sprangen über die faulen Hunde während die Häuser in der Sonne glänzten.",
        ["es"] = "Los zorros rápidos saltaron sobre los perros perezosos mientras los niños observaban tranquilamente.",
        ["it"] = "Le volpi veloci saltavano sopra i cani pigri mentre i bambini guardavano dalla finestra.",
        ["pt"] = "As raposas rápidas saltaram sobre os cães preguiçosos enquanto as crianças observavam pela janela.",
        ["nl"] = "De snelle vossen sprongen over de luie honden terwijl de kinderen vanuit het raam toekeken.",
        ["ru"] = "Быстрые лисы прыгали через ленивых собак пока дети смотрели в окно тихим вечером.",
        ["ar"] = "قفزت الثعالب السريعة فوق الكلاب الكسولة بينما كان الأطفال يراقبون من النافذة.",
        ["zh"] = "敏捷的狐狸跳过了懒惰的狗孩子们从窗户里观看安静的下午阳光。",
        ["ja"] = "素早い狐が怠け者の犬を飛び越え子供たちは窓から静かな午後の日差しを眺めていました。",
        ["ko"] = "빠른 여우가 게으른 개를 뛰어넘는 동안 아이들은 창문에서 고요한 오후 햇살을 바라보았습니다."
    };
}
