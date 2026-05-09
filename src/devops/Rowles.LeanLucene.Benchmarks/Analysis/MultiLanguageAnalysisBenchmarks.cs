using BenchmarkDotNet.Attributes;
using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;

namespace Rowles.LeanLucene.Benchmarks;

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
            total += _analyser.Analyse(_sample.AsSpan()).Count;
        return total;
    }

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
