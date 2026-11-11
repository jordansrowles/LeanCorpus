using System.Globalization;
using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Ar;
using Lucene.Net.Analysis.De;
using Lucene.Net.Analysis.En;
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
/// Compares the <see cref="AnalyserFactory"/> pipeline for each language that has a Lucene.NET
/// analyser of the same family, over a multi-sentence DataForge input. Lucene.NET analysers are
/// built once in setup and disposed in cleanup. <c>en</c> uses <see cref="EnglishAnalyzer"/>, the
/// Porter pipeline matching LeanCorpus. Setup records each language's term-overlap class
/// (<c>matched</c> or <c>same-family</c>) and vocabulary size in evidence. Directional head-to-head
/// rows: only a <c>matched</c> language may support a ratio, after qualification. <c>ja</c> and
/// <c>zh</c> have no Lucene.NET pair and are measured by <see cref="MultiLanguageInternalAnalysisBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class MultiLanguageAnalysisBenchmarks
{
    public static IEnumerable<string> Languages => RowlesTextMultilingualProfile.Languages.Where(MultilingualInput.HasLucenePair);

    [ParamsSource(nameof(Languages))]
    public string Language { get; set; } = "en";

    private IAnalyser _analyser = null!;
    private Analyzer _luceneAnalyser = null!;
    private string _sample = string.Empty;
    private readonly ReadingTokenSink _readingSink = new();

    [GlobalSetup]
    public void Setup()
    {
        _analyser = AnalyserFactory.Create(Language);
        _luceneAnalyser = MultilingualInput.CreateLuceneAnalyser(Language);
        _sample = MultilingualInput.Build(Language);

        var lean = TextOutputPreflight.CaptureLean(sink => _analyser.Analyse(_sample.AsSpan(), sink));
        IReadOnlyList<AnalysedToken> lucene;
        using (var stream = _luceneAnalyser.GetTokenStream("text", new StringReader(_sample)))
            lucene = TextOutputPreflight.CaptureLucene(stream);

        bool matched = lean.Select(static t => t.Term).SequenceEqual(lucene.Select(static t => t.Term));
        MultilingualInput.Record(Language, matched ? "matched" : "same-family", lean, lucene.Count);
        if (lean.Count == 0 || lucene.Count == 0)
            throw new InvalidOperationException($"Multilingual setup for '{Language}' produced no terms on one engine.");
    }

    [GlobalCleanup]
    public void Cleanup() => _luceneAnalyser?.Dispose();

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Analyse()
    {
        int total = 0;
        for (int i = 0; i < MultilingualInput.Repetitions; i++)
        {
            _readingSink.Reset();
            _analyser.Analyse(_sample.AsSpan(), _readingSink);
            total += _readingSink.Count;
        }
        return total;
    }

    [Benchmark(Description = "Lucene.NET Analyse")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Analyse()
    {
        int total = 0;
        for (int i = 0; i < MultilingualInput.Repetitions; i++)
        {
            using var stream = _luceneAnalyser.GetTokenStream("text", new StringReader(_sample));
            total += LuceneTermReader.ReadAll(stream);
        }
        return total;
    }
}

/// <summary>
/// Measures the <c>ja</c> and <c>zh</c> <see cref="AnalyserFactory"/> pipelines with no Lucene.NET
/// pair (<c>internal-only</c>): the Lucene.NET Kuromoji and SmartCn analysers are not referenced.
/// Setup fails when the language's lexicon is absent and records its path and SHA-256.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class MultiLanguageInternalAnalysisBenchmarks
{
    public static IEnumerable<string> Languages => RowlesTextMultilingualProfile.Languages.Where(static l => !MultilingualInput.HasLucenePair(l));

    [ParamsSource(nameof(Languages))]
    public string Language { get; set; } = "zh";

    private IAnalyser _analyser = null!;
    private string _sample = string.Empty;
    private readonly ReadingTokenSink _readingSink = new();

    [GlobalSetup]
    public void Setup()
    {
        TextLexicons.RequireAndRecord(Language, Language switch
        {
            "zh" => "chinese-dict.txt",
            "ja" => "japanese.jlc",
            _ => throw new InvalidOperationException($"No lexicon is declared for internal-only language '{Language}'."),
        });
        _analyser = AnalyserFactory.Create(Language);
        _sample = MultilingualInput.Build(Language);
        var lean = TextOutputPreflight.CaptureLean(sink => _analyser.Analyse(_sample.AsSpan(), sink));
        MultilingualInput.Record(Language, "internal-only", lean, luceneTerms: null);
        if (lean.Count == 0)
            throw new InvalidOperationException($"Multilingual setup for '{Language}' produced no terms.");
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int Analyse()
    {
        int total = 0;
        for (int i = 0; i < MultilingualInput.Repetitions; i++)
        {
            _readingSink.Reset();
            _analyser.Analyse(_sample.AsSpan(), _readingSink);
            total += _readingSink.Count;
        }
        return total;
    }
}

/// <summary>Shared multilingual input, pairing and evidence.</summary>
internal static class MultilingualInput
{
    /// <summary>DataForge records joined into one multi-sentence input per language.</summary>
    public const int Sentences = 16;

    /// <summary>Analyses of the input per invocation, amortising per-call overhead.</summary>
    public const int Repetitions = 10;

    public static bool HasLucenePair(string language) => language is not ("ja" or "zh");

    public static string Build(string language)
        => string.Join(" ", TextBenchmarkData.BuildMultilingualInputs(language, Sentences));

    public static Analyzer CreateLuceneAnalyser(string language) => language switch
    {
        "en" => new EnglishAnalyzer(LuceneVersion.LUCENE_48),
        "fr" => new FrenchAnalyzer(LuceneVersion.LUCENE_48),
        "de" => new GermanAnalyzer(LuceneVersion.LUCENE_48),
        "es" => new SpanishAnalyzer(LuceneVersion.LUCENE_48),
        "it" => new ItalianAnalyzer(LuceneVersion.LUCENE_48),
        "pt" => new PortugueseAnalyzer(LuceneVersion.LUCENE_48),
        "nl" => new DutchAnalyzer(LuceneVersion.LUCENE_48),
        "ru" => new RussianAnalyzer(LuceneVersion.LUCENE_48),
        "ar" => new ArabicAnalyzer(LuceneVersion.LUCENE_48),
        // Hangul runs: StandardAnalyzer emits the same whitespace-separated words as LeanCorpus.
        "ko" => new StandardAnalyzer(LuceneVersion.LUCENE_48),
        _ => throw new InvalidOperationException($"Language '{language}' has no Lucene.NET pair."),
    };

    /// <summary>Logs the overlap class, sizes and leading terms of one language as benchmark evidence.</summary>
    public static void Record(string language, string overlap, IReadOnlyList<AnalysedToken> lean, int? luceneTerms)
    {
        int vocabulary = lean.Select(static t => t.Term).Distinct(StringComparer.Ordinal).Count();
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Benchmark multilingual {language}: overlap={overlap}, sentences={Sentences}, leanTerms={lean.Count}, luceneTerms={(luceneTerms is { } n ? n.ToString(CultureInfo.InvariantCulture) : "none")}, vocabulary={vocabulary}, first=[{string.Join(" ", lean.Take(8).Select(static t => t.Term))}]"));
    }
}
