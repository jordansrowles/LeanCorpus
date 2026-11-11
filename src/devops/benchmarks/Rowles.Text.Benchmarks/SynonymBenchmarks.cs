using BenchmarkDotNet.Attributes;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using LeanSynonymGraphFilter = Rowles.LeanCorpus.Analysis.Filters.SynonymGraphFilter;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares synonym expansion throughput between <see cref="LeanSynonymGraphFilter"/> and
/// Lucene.NET <c>SynonymFilter</c> at different synonym map sizes. LeanCorpus runs
/// Tokeniser, LowercaseFilter, StopWordFilter and the synonym filter; Lucene.NET runs
/// StandardAnalyzer and SynonymFilter. Both maps hold the same source-to-replacement pairs, built
/// from either the most frequent or the least frequent corpus terms, and setup proves the token
/// graphs are equal, including position lengths. The two no-synonym rows on LeanCorpus form an A/A
/// pair. Directional head-to-head rows.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class SynonymBenchmarks
{
    public static IEnumerable<int> DocCounts => TextBenchmarkData.GetDocCounts(TextBenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Number of synonym mappings in the synonym map.</summary>
    [Params(10, 50, 200)]
    public int SynonymCount { get; set; }

    /// <summary>Whether synonym sources are the most frequent or the least frequent corpus terms.</summary>
    [Params("frequent", "sparse")]
    public string MapTerms { get; set; } = "frequent";

    private string[] _documents = [];

    // LeanCorpus state
    private StandardAnalyser _baseAnalyser = null!;
    private LeanSynonymGraphFilter _synonymFilter = null!;
    private Analyser _noSynonymAnalyser = null!;
    private Analyser _synonymAnalyser = null!;

    // Lucene.NET state
    private Lucene.Net.Analysis.Standard.StandardAnalyzer _luceneAnalyzer = null!;
    private Lucene.Net.Analysis.Synonym.SynonymMap _luceneSynonymMap = null!;

    [GlobalSetup]
    public void Setup()
    {
        _documents = TextBenchmarkData.BuildDocuments(DocumentCount);
        _baseAnalyser = new StandardAnalyser();

        var sources = BuildSynonymSources(_documents);
        _synonymFilter = BuildLeanSynonymFilter(sources);
        _noSynonymAnalyser = new Analyser(new Tokeniser(), new LowercaseFilter(), new StopWordFilter());
        _synonymAnalyser = new Analyser(new Tokeniser(), new LowercaseFilter(), new StopWordFilter(), _synonymFilter);
        _luceneSynonymMap = BuildLuceneSynonymMap(sources);
        _luceneAnalyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(LuceneVersion.LUCENE_48);

        var noSynonyms = TextOutputPreflight.Compare(string.Concat("synonym/none/", MapTerms, "/", SynonymCount.ToString(System.Globalization.CultureInfo.InvariantCulture)), _documents,
            document => TextOutputPreflight.CaptureLean(sink => _noSynonymAnalyser.Analyse(document.AsSpan(), sink)),
            document =>
            {
                using var stream = _luceneAnalyzer.GetTokenStream("body", new System.IO.StringReader(document));
                return TextOutputPreflight.CaptureLucene(stream);
            },
            PreflightAllowList.Cjk);
        var withSynonyms = TextOutputPreflight.Compare(string.Concat("synonym/with/", MapTerms, "/", SynonymCount.ToString(System.Globalization.CultureInfo.InvariantCulture)), _documents,
            document => TextOutputPreflight.CaptureLean(sink => _synonymAnalyser.Analyse(document.AsSpan(), sink)),
            document =>
            {
                using var baseStream = _luceneAnalyzer.GetTokenStream("body", new System.IO.StringReader(document));
                using var synonymStream = new Lucene.Net.Analysis.Synonym.SynonymFilter(baseStream, _luceneSynonymMap, ignoreCase: false);
                return TextOutputPreflight.CaptureLucene(synonymStream);
            },
            PreflightAllowList.Cjk);
        ValidateSynonymsApplied(noSynonyms.Tokens, withSynonyms.Tokens);
    }

    /// <summary>Fails setup unless the synonym pipeline emits more tokens than the base pipeline.</summary>
    internal static void ValidateSynonymsApplied(int baseTokens, int synonymTokens)
    {
        if (synonymTokens <= baseTokens)
            throw new InvalidOperationException(
                $"The synonym pipeline emitted {synonymTokens} tokens against {baseTokens} without synonyms; the filter is not applied.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _luceneAnalyzer?.Dispose();
    }

    // --- LeanCorpus benchmarks ---

    private readonly ReadingTokenSink _readingSink = new();

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NoSynonyms() => AnalyseLean(_noSynonymAnalyser);

    [Benchmark(Description = "LeanCorpus no synonyms, A/A repeat")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NoSynonyms_Repeat() => AnalyseLean(_noSynonymAnalyser);

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_WithSynonyms() => AnalyseLean(_synonymAnalyser);

    private int AnalyseLean(Analyser analyser)
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            _readingSink.Reset();
            analyser.Analyse(doc.AsSpan(), _readingSink);
            total += _readingSink.Count;
        }
        return total;
    }

    // --- Lucene.NET parity benchmarks ---

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_NoSynonyms()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var reader = new System.IO.StringReader(doc);
            using var stream = _luceneAnalyzer.GetTokenStream("body", reader);
            total += LuceneTermReader.ReadAll(stream);
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_WithSynonyms()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var reader = new System.IO.StringReader(doc);
            using var baseStream = _luceneAnalyzer.GetTokenStream("body", reader);
            using var synonymStream = new Lucene.Net.Analysis.Synonym.SynonymFilter(
                baseStream, _luceneSynonymMap, ignoreCase: false);
            total += LuceneTermReader.ReadAll(synonymStream);
        }
        return total;
    }

    // --- Helper: build LeanCorpus synonym filter ---

    private static LeanSynonymGraphFilter BuildLeanSynonymFilter(string[] sources)
    {
        var map = new Rowles.LeanCorpus.Analysis.Filters.SynonymMap();
        for (int i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            var slug = source.Replace(' ', '_');
            map.Add(source, [$"{slug}_synonym_{i}", $"{slug}_alt"]);
        }
        return new LeanSynonymGraphFilter(map);
    }

    // --- Helper: build Lucene.NET FST-backed SynonymMap ---

    private static Lucene.Net.Analysis.Synonym.SynonymMap BuildLuceneSynonymMap(string[] sources)
    {
        // One Builder.Add per replacement, matching LeanCorpus's alternatives. Joining both
        // replacements into one output would make a single two-token phrase instead.
        var builder = new Lucene.Net.Analysis.Synonym.SynonymMap.Builder(dedup: true);
        for (int i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            var slug = source.Replace(' ', '_');
            foreach (var replacement in new[] { $"{slug}_synonym_{i}", $"{slug}_alt" })
            {
                builder.Add(
                    Lucene.Net.Analysis.Synonym.SynonymMap.Builder.Join(source.Split(' '), new CharsRef()),
                    Lucene.Net.Analysis.Synonym.SynonymMap.Builder.Join([replacement], new CharsRef()),
                    includeOrig: true);
            }
        }
        return builder.Build();
    }

    // --- Helper: extract frequent terms to use as synonym sources ---

    private string[] BuildSynonymSources(string[] documents)
    {
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        int sampleCount = Math.Min(documents.Length, 4_096);

        for (int i = 0; i < sampleCount; i++)
        {
            var tokens = new List<Analysis.Token>();
            _baseAnalyser.Analyse(documents[i].AsSpan(), new CapturingTokenSink(tokens));
            foreach (var token in tokens)
            {
                frequencies.TryGetValue(token.Text, out int current);
                frequencies[token.Text] = current + 1;
            }
        }

        var ordered = MapTerms == "sparse"
            ? frequencies.OrderBy(static entry => entry.Value).ThenBy(static entry => entry.Key, StringComparer.Ordinal)
            : frequencies.OrderByDescending(static entry => entry.Value).ThenBy(static entry => entry.Key, StringComparer.Ordinal);
        var sources = ordered
            .Where(static entry => entry.Key.All(char.IsAsciiLetterLower))
            .Take(SynonymCount)
            .Select(static entry => entry.Key)
            .ToArray();

        if (sources.Length != SynonymCount)
            throw new InvalidOperationException(
                $"Expected {SynonymCount} synonym sources but only found {sources.Length}.");

        return sources;
    }

    private sealed class CapturingTokenSink(List<Analysis.Token> tokens) : Analysis.ISpanTokenSink
    {
        public void Add(ReadOnlySpan<char> text, int startOffset, int endOffset,
            string type = Analysis.Token.DefaultType, int positionIncrement = 1, byte[]? payload = null)
            => tokens.Add(new Analysis.Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
    }
}
