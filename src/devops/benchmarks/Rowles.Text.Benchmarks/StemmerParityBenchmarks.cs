using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.En;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares <see cref="StemmedAnalyser"/> throughput against Lucene.NET's <c>EnglishAnalyzer</c>
/// chain with <c>PorterStemFilter</c> replaced by <c>SnowballFilter(EnglishStemmer)</c>, so both
/// sides run Porter2. Since #122 <see cref="StemmedAnalyser"/> uses Porter2; the stock
/// <c>EnglishAnalyzer</c> uses the original Porter algorithm and would not be a like-for-like pair.
/// Lucene.NET's bundled Snowball English is older than 3.2.0, so setup accepts a named term-only
/// difference and reports the divergent tokens (<c>namedTermDifferences</c>). Token structure is
/// proven equal, stemming semantics are not: treat the rows as directional, not qualified parity.
/// Reading rows copy every term; <c>_CountOnly</c> rows allocate a counting sink per document and
/// only count tokens. Setup proves equal output on the first 500 documents. Directional
/// head-to-head rows.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class StemmerParityBenchmarks
{
    public static IEnumerable<int> DocCounts => TextBenchmarkData.GetDocCounts(TextBenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private StemmedAnalyser _leanAnalyser = null!;
    private Lucene.Net.Analysis.Analyzer _luceneAnalyser = null!;
    private readonly ReadingTokenSink _readingSink = new();

    [GlobalSetup]
    public void Setup()
    {
        _documents = TextBenchmarkData.BuildDocuments(DocumentCount);
        _leanAnalyser = new StemmedAnalyser();
        _luceneAnalyser = new SnowballEnglishAnalyzer();
        TextOutputPreflight.Compare("stemmer/english", _documents,
            document => TextOutputPreflight.CaptureLean(sink => _leanAnalyser.Analyse(document.AsSpan(), sink)),
            document =>
            {
                using var stream = _luceneAnalyser.GetTokenStream("body", document);
                return TextOutputPreflight.CaptureLucene(stream);
            },
            // Lucene.NET bundles an older Snowball English. Snowball 3.2.0 differs on some words, for
            // example international -> internat (Lucene.NET: intern), confirmed against Snowball's C
            // stemwords and word list. Term text only; positions and offsets must still match.
            PreflightAllowList.NamedWithCjk("snowball-english-3.2.0-vs-lucene-net"));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _luceneAnalyser.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_StemmedAnalyser()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            _readingSink.Reset();
            _leanAnalyser.Analyse(doc.AsSpan(), _readingSink);
            total += _readingSink.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_EnglishAnalyzer()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var ts = _luceneAnalyser.GetTokenStream("body", doc);
            total += LuceneTermReader.ReadAll(ts);
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_StemmedAnalyser_CountOnly()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            var sink = new CountingTokenSink();
            _leanAnalyser.Analyse(doc.AsSpan(), sink);
            total += sink.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_EnglishAnalyzer_CountOnly()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var ts = _luceneAnalyser.GetTokenStream("body", doc);
            ts.Reset();
            while (ts.IncrementToken())
                total++;
            ts.End();
        }
        return total;
    }

    /// <summary>
    /// Lucene.NET <c>EnglishAnalyzer</c>'s chain (standard tokeniser, standard and possessive filters,
    /// lowercasing, English stop words) ending in the Snowball English (Porter2) stemmer.
    /// </summary>
    private sealed class SnowballEnglishAnalyzer : Lucene.Net.Analysis.Analyzer
    {
        protected override Lucene.Net.Analysis.TokenStreamComponents CreateComponents(string fieldName, System.IO.TextReader reader)
        {
            var source = new Lucene.Net.Analysis.Standard.StandardTokenizer(LuceneVersion.LUCENE_48, reader);
            Lucene.Net.Analysis.TokenStream result = new Lucene.Net.Analysis.Standard.StandardFilter(LuceneVersion.LUCENE_48, source);
            result = new EnglishPossessiveFilter(LuceneVersion.LUCENE_48, result);
            result = new Lucene.Net.Analysis.Core.LowerCaseFilter(LuceneVersion.LUCENE_48, result);
            result = new Lucene.Net.Analysis.Core.StopFilter(LuceneVersion.LUCENE_48, result, EnglishAnalyzer.DefaultStopSet);
            result = new Lucene.Net.Analysis.Snowball.SnowballFilter(result, new Lucene.Net.Tartarus.Snowball.Ext.EnglishStemmer());
            return new Lucene.Net.Analysis.TokenStreamComponents(source, result);
        }
    }
}
