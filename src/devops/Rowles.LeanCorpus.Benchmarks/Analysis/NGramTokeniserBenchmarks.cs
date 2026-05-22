using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.NGram;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="EdgeNGramTokeniser"/> and <see cref="NGramTokeniser"/> throughput against Lucene.NET equivalents.
/// Only the zero-allocation span-sink and streaming enumerator paths are benchmarked.
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
    private NGramTokeniser _ngramTokeniserWs = null!;
    private readonly CountingSpanTokenSink _spanSink = new();
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
        _ngramTokeniserWs = new NGramTokeniser(_min, _max, splitOnWhitespace: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_EdgeNGramTokeniser_SpanSink()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            _spanSink.Reset();
            _edgeTokeniser.Tokenise(doc.AsSpan(), _spanSink);
            total += _spanSink.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NGramTokeniser_SpanSink()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            _spanSink.Reset();
            _ngramTokeniser.Tokenise(doc.AsSpan(), _spanSink);
            total += _spanSink.Count;
        }
        return total;
    }

    /// <summary>NGram tokeniser with per-word whitespace splitting, span sink path.</summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NGramTokeniser_WordSplit_SpanSink()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            _spanSink.Reset();
            _ngramTokeniserWs.Tokenise(doc.AsSpan(), _spanSink);
            total += _spanSink.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_EdgeNGramTokeniser_Streaming()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            foreach (var token in _edgeTokeniser.EnumerateTokens(doc.AsSpan()))
                total++;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NGramTokeniser_Streaming()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            foreach (var token in _ngramTokeniser.EnumerateTokens(doc.AsSpan()))
                total++;
        }
        return total;
    }

    /// <summary>NGram tokeniser with per-word whitespace splitting, streaming enumeration.</summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NGramTokeniser_WordSplit_Streaming()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            foreach (var token in _ngramTokeniserWs.EnumerateTokens(doc.AsSpan()))
                total++;
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

    private sealed class CountingSpanTokenSink : ISpanTokenSink
    {
        public int Count { get; private set; }

        public void Reset() => Count = 0;

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            Count++;
        }
    }
}
