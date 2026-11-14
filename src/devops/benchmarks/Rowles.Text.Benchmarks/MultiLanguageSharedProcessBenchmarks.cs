using System.Globalization;
using BenchmarkDotNet.Attributes;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures one language's <see cref="AnalyserFactory"/> pipeline after global setup has warmed
/// <see cref="WarmLanguages"/> languages in the same process: the measured language alone (1),
/// it and one other (2), or all twelve multilingual profile languages (12). BenchmarkDotNet runs
/// each case in its own process, so the single-language rows cannot show the cost of sharing
/// type checks, call sites and tiered code with other languages; this class makes that visible.
/// Every warmed analyser runs the same input and repetitions as the measured one, and setup
/// fails if any warmed language produces no terms. Internal comparison only.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
public class MultiLanguageSharedProcessBenchmarks
{
    /// <summary>Analyses of each warmed language's input during setup.</summary>
    private const int WarmupPasses = 200;

    public static IEnumerable<string> Languages => RowlesTextMultilingualProfile.Languages.Where(MultilingualInput.HasLucenePair);

    [ParamsSource(nameof(Languages))]
    public string Language { get; set; } = "en";

    /// <summary>Number of languages analysed in this process before measurement, including <see cref="Language"/>.</summary>
    [Params(1, 2, 12)]
    public int WarmLanguages { get; set; }

    private IAnalyser _analyser = null!;
    private string _sample = string.Empty;
    private readonly ReadingTokenSink _readingSink = new();

    [GlobalSetup]
    public void Setup()
    {
        var warmed = WarmSet(Language, WarmLanguages);
        foreach (string language in warmed)
        {
            if (language is "ja" or "zh")
                TextLexicons.RequireAndRecord(language, language == "ja" ? "japanese.jlc" : "chinese-dict.txt");

            var analyser = AnalyserFactory.Create(language);
            string sample = MultilingualInput.Build(language);
            int terms = 0;
            for (int pass = 0; pass < WarmupPasses; pass++)
            {
                _readingSink.Reset();
                analyser.Analyse(sample.AsSpan(), _readingSink);
                terms = _readingSink.Count;
            }
            if (terms == 0)
                throw new InvalidOperationException($"Shared-process warm-up for '{language}' produced no terms.");

            if (language == Language)
            {
                _analyser = analyser;
                _sample = sample;
            }
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Benchmark shared-process {Language}: warmed {warmed.Count} languages [{string.Join(" ", warmed)}], {WarmupPasses} passes each."));
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

    /// <summary>
    /// The measured language last, so the most recently run code is its own, preceded by the
    /// next <paramref name="count"/> - 1 profile languages in a fixed rotation.
    /// </summary>
    internal static IReadOnlyList<string> WarmSet(string measured, int count)
    {
        var all = RowlesTextMultilingualProfile.Languages;
        if (count < 1 || count > all.Count)
            throw new ArgumentOutOfRangeException(nameof(count), count, $"Warm language count must be between 1 and {all.Count}.");

        int start = 0;
        for (int i = 0; i < all.Count; i++)
            if (all[i] == measured)
                start = i;

        var set = new List<string>(count);
        for (int i = 1; i < count; i++)
            set.Add(all[(start + i) % all.Count]);
        set.Add(measured);
        return set;
    }
}
