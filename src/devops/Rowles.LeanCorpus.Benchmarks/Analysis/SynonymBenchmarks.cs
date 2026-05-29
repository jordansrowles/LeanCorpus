using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures indexing overhead of <see cref="SynonymGraphFilter"/> at different synonym map sizes.
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
    private StandardAnalyser _baseAnalyser = null!;
    private SynonymGraphFilter _synonymFilter = null!;

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _baseAnalyser = new StandardAnalyser();
        _synonymFilter = BuildSynonymFilter(SynonymCount, _documents, _baseAnalyser);
    }

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

    private static SynonymGraphFilter BuildSynonymFilter(int count, string[] documents, StandardAnalyser analyser)
    {
        var map = new SynonymMap();
        var sources = BuildSynonymSources(documents, analyser, count);
        for (int i = 0; i < sources.Length; i++)
        {
            var source = sources[i];
            var slug = source.Replace(' ', '_');
            map.Add(source, [$"{slug}_synonym_{i}", $"{slug}_alt"]);
        }
        return new SynonymGraphFilter(map);
    }

    private static string[] BuildSynonymSources(string[] documents, StandardAnalyser analyser, int count)
    {
        var frequencies = new Dictionary<string, int>(StringComparer.Ordinal);
        int sampleCount = Math.Min(documents.Length, 4_096);

        for (int i = 0; i < sampleCount; i++)
        {
            var tokens = analyser.Analyse(documents[i].AsSpan());
            foreach (var token in tokens)
            {
                frequencies.TryGetValue(token.Text, out int current);
                frequencies[token.Text] = current + 1;
            }
        }

        var sources = frequencies
            .OrderByDescending(static entry => entry.Value)
            .ThenBy(static entry => entry.Key, StringComparer.Ordinal)
            .Take(count)
            .Select(static entry => entry.Key)
            .ToArray();

        if (sources.Length != count)
            throw new InvalidOperationException($"Expected {count} synonym sources but only found {sources.Length}.");

        return sources;
    }
}
