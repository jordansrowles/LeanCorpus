using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Core;
using Lucene.Net.Analysis.NGram;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="EdgeNGramTokeniser"/> and <see cref="NGramTokeniser"/> throughput against Lucene.NET equivalents.
/// Only the zero-allocation span-sink and streaming enumerator paths are benchmarked. The edge pair is
/// Lucene.NET <see cref="WhitespaceTokenizer"/> plus <see cref="EdgeNGramTokenFilter"/>, which grams each
/// whitespace word as LeanCorpus does; setup asserts equal grams on the first 500 documents. Lucene.NET
/// keeps the whole word's offsets on each gram, so the edge comparison is of gram terms. Directional
/// head-to-head rows.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class NGramTokeniserBenchmarks
{
    public static IEnumerable<int> DocCounts => TextBenchmarkData.GetDocCounts(TextBenchmarkData.DefaultDocCount);

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
        _documents = TextBenchmarkData.BuildDocuments(DocumentCount);
        _edgeTokeniser = new EdgeNGramTokeniser(_min, _max);
        _ngramTokeniser = new NGramTokeniser(_min, _max);
        _ngramTokeniserWs = new NGramTokeniser(_min, _max, splitOnWhitespace: true);

        TextOutputPreflight.Compare(string.Concat("ngram/", GramRange), _documents,
            document => TextOutputPreflight.CaptureLean(sink => _ngramTokeniser.Tokenise(document.AsSpan(), sink)),
            document =>
            {
                using var tokenizer = new NGramTokenizer(LuceneVersion.LUCENE_48, new StringReader(document), _min, _max);
                return TextOutputPreflight.CaptureLucene(tokenizer);
            },
            PreflightAllowList.None);

        ValidateEdgeGrams();
    }

    private TokenStream CreateLuceneEdge(string document)
        => new EdgeNGramTokenFilter(LuceneVersion.LUCENE_48,
            new WhitespaceTokenizer(LuceneVersion.LUCENE_48, new StringReader(document)), _min, _max);

    /// <summary>Fails setup unless both engines emit the same edge grams, in order, per document.</summary>
    private void ValidateEdgeGrams()
    {
        int count = Math.Min(TextOutputPreflight.DocumentCount, _documents.Length);
        long grams = 0;
        for (int i = 0; i < count; i++)
        {
            var lean = TextOutputPreflight.CaptureLean(sink => _edgeTokeniser.Tokenise(_documents[i].AsSpan(), sink)).Select(static t => t.Term).ToArray();
            string[] lucene;
            using (var stream = CreateLuceneEdge(_documents[i]))
                lucene = TextOutputPreflight.CaptureLucene(stream).Select(static t => t.Term).ToArray();
            if (!lean.SequenceEqual(lucene, StringComparer.Ordinal))
                throw new InvalidOperationException(string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"Edge n-gram preflight {GramRange}: document {i} has {lean.Length} LeanCorpus grams and {lucene.Length} Lucene.NET grams, or a different gram sequence."));
            grams += lean.Length;
        }

        Console.WriteLine(string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"Benchmark preflight text edge-ngram/{GramRange}: documents={count}, identical-grams={count}, grams={grams}"));
    }

    private readonly ReadingTokenSink _readingSink = new();

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NGramTokeniser_Reading()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            _readingSink.Reset();
            _ngramTokeniser.Tokenise(doc.AsSpan(), _readingSink);
            total += _readingSink.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_NGramTokenizer_Reading()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var tokenizer = new NGramTokenizer(LuceneVersion.LUCENE_48, new StringReader(doc), _min, _max);
            total += LuceneTermReader.ReadAll(tokenizer);
        }
        return total;
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_EdgeNGramTokeniser_SpanSink_CountOnly()
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
    public int LeanCorpus_NGramTokeniser_SpanSink_CountOnly()
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
    public int LeanCorpus_NGramTokeniser_WordSplit_SpanSink_CountOnly()
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
    public int LuceneNet_EdgeNGramFilter_CountOnly()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var stream = CreateLuceneEdge(doc);
            stream.Reset();
            while (stream.IncrementToken())
                total++;
            stream.End();
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_EdgeNGramTokeniser_Reading()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            _readingSink.Reset();
            _edgeTokeniser.Tokenise(doc.AsSpan(), _readingSink);
            total += _readingSink.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_EdgeNGramFilter_Reading()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var stream = CreateLuceneEdge(doc);
            total += LuceneTermReader.ReadAll(stream);
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_NGramTokenizer_CountOnly()
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
            string type = Rowles.LeanCorpus.Analysis.Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            Count++;
        }
    }
}
