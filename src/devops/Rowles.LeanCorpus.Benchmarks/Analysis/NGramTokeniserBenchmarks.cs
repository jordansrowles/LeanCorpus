using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.NGram;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="EdgeNGramTokeniser"/> and <see cref="NGramTokeniser"/> throughput against Lucene.NET equivalents.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class NGramTokeniserBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    /// <summary>n-gram size range expressed as "min-max".</summary>
    [Params("2-3", "3-5")]
    public string GramRange { get; set; } = "2-3";

    private string[] _documents = [];
    private EdgeNGramTokeniser _edgeTokeniser = null!;
    private NGramTokeniser _ngramTokeniser = null!;
    private readonly List<Token> _edgeTokens = [];
    private readonly List<Token> _ngramTokens = [];
    private int _min;
    private int _max;

    [GlobalSetup]
    public void Setup()
    {
        var parts = GramRange.Split('-');
        _min = int.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture);
        _max = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture);
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _edgeTokeniser = new EdgeNGramTokeniser(_min, _max);
        _ngramTokeniser = new NGramTokeniser(_min, _max);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_EdgeNGramTokeniser()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            _edgeTokeniser.Tokenise(doc.AsSpan(), _edgeTokens);
            total += _edgeTokens.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NGramTokeniser()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            _ngramTokeniser.Tokenise(doc.AsSpan(), _ngramTokens);
            total += _ngramTokens.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_EdgeNGramTokenizer()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var tokenizer = new EdgeNGramTokenizer(
                LuceneVersion.LUCENE_48, new StringReader(doc), _min, _max);
            tokenizer.Reset();
            while (tokenizer.IncrementToken())
                total++;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_NGramTokenizer()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var tokenizer = new NGramTokenizer(
                LuceneVersion.LUCENE_48, new StringReader(doc), _min, _max);
            tokenizer.Reset();
            while (tokenizer.IncrementToken())
                total++;
        }
        return total;
    }
}
