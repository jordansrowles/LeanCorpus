using BenchmarkDotNet.Attributes;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using LeanSynonymGraphFilter = Rowles.LeanCorpus.Analysis.Filters.SynonymGraphFilter;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares synonym expansion throughput between <see cref="LeanSynonymGraphFilter"/>
/// and Lucene.NET <c>SynonymFilter</c> at different synonym map sizes.
/// Both maps contain the same source→replacement mappings extracted from the corpus.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class SynonymBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>Number of synonym mappings in the synonym map.</summary>
    [Params(10, 50, 200)]
    public int SynonymCount { get; set; }

    private string[] _documents = [];

    // LeanCorpus state
    private StandardAnalyser _baseAnalyser = null!;
    private LeanSynonymGraphFilter _synonymFilter = null!;

    // Lucene.NET state
    private Lucene.Net.Analysis.Standard.StandardAnalyzer _luceneAnalyzer = null!;
    private Lucene.Net.Analysis.Synonym.SynonymMap _luceneSynonymMap = null!;

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _baseAnalyser = new StandardAnalyser();

        var sources = BuildSynonymSources(_documents);
        _synonymFilter = BuildLeanSynonymFilter(sources);
        _luceneSynonymMap = BuildLuceneSynonymMap(sources);
        _luceneAnalyzer = new Lucene.Net.Analysis.Standard.StandardAnalyzer(LuceneVersion.LUCENE_48);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _luceneAnalyzer?.Dispose();
    }

    // --- LeanCorpus benchmarks ---

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NoSynonyms()
    {
        int total = 0;
        foreach (var doc in _documents)
            total += _baseAnalyser.Analyse(doc.AsSpan()).Count;
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_WithSynonyms()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            var tokens = _baseAnalyser.Analyse(doc.AsSpan());
            _synonymFilter.Apply(tokens);
            total += tokens.Count;
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
            stream.Reset();
            while (stream.IncrementToken())
                total++;
            stream.End();
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
            synonymStream.Reset();
            while (synonymStream.IncrementToken())
                total++;
            synonymStream.End();
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
        var builder = new Lucene.Net.Analysis.Synonym.SynonymMap.Builder(dedup: true);
        var inputChars = new CharsRef();
        var outputChars = new CharsRef();
        for (int i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            var slug = source.Replace(' ', '_');
            builder.Add(
                Lucene.Net.Analysis.Synonym.SynonymMap.Builder.Join(source.Split(' '), inputChars),
                Lucene.Net.Analysis.Synonym.SynonymMap.Builder.Join(
                    [$"{slug}_synonym_{i}", $"{slug}_alt"], outputChars),
                includeOrig: true);
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
            var tokens = _baseAnalyser.Analyse(documents[i].AsSpan());
            foreach (var token in tokens)
            {
                frequencies.TryGetValue(token.Text, out int current);
                frequencies[token.Text] = current + 1;
            }
        }

        var sources = frequencies
            .OrderByDescending(static entry => entry.Value)
            .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
            .Take(SynonymCount)
            .Select(static entry => entry.Key)
            .ToArray();

        if (sources.Length != SynonymCount)
            throw new InvalidOperationException(
                $"Expected {SynonymCount} synonym sources but only found {sources.Length}.");

        return sources;
    }
}
