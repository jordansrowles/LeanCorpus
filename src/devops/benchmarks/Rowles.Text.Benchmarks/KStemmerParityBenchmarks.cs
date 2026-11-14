using BenchmarkDotNet.Attributes;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures KStemmer throughput via the <see cref="StemmerAnalyser"/> pipeline.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
public class KStemmerParityBenchmarks
{
    public static IEnumerable<int> DocCounts => TextBenchmarkData.GetDocCounts(TextBenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private IAnalyser _analyser = null!;

    [GlobalSetup]
    public void Setup()
    {
        _documents = TextBenchmarkData.BuildDocuments(DocumentCount);

        var lexiconPath = TextLexicons.Require("kstem-dict.txt");
        var lexicon = KStemLexicon.FromFile(lexiconPath);
        // Match Lucene's pipeline: Tokeniser → LowercaseFilter → KStemFilter (no stopword removal).
        _analyser = new Analyser(
            new Tokeniser(),
            new LowercaseFilter(),
            new StemTokenFilter(new KStemmer(lexicon)));

        // Build a Lucene.NET KStemmer pipeline: StandardTokenizer → LowerCaseFilter → KStemFilter
        _luceneAnalyser = new AnalyzerAnonymousClass(static (fieldName, reader) =>
        {
            var tokenizer = new Lucene.Net.Analysis.Standard.StandardTokenizer(
                Lucene.Net.Util.LuceneVersion.LUCENE_48, reader);
            var lowerCase = new Lucene.Net.Analysis.Core.LowerCaseFilter(
                Lucene.Net.Util.LuceneVersion.LUCENE_48, tokenizer);
            var kStem = new Lucene.Net.Analysis.En.KStemFilter(lowerCase);
            return new Lucene.Net.Analysis.TokenStreamComponents(tokenizer, kStem);
        });

        // KStem is declared not a pair until its 14.55% token difference is explained (Sprint 4 PR 2).
        // The named difference covers stemmed term text only; Japanese tokenisation differs as in every
        // other CJK case.
        TextOutputPreflight.Compare("kstem", _documents,
            document => TextOutputPreflight.CaptureLean(sink => _analyser.Analyse(document.AsSpan(), sink)),
            document =>
            {
                using var stream = _luceneAnalyser.GetTokenStream("body", new System.IO.StringReader(document));
                return TextOutputPreflight.CaptureLucene(stream);
            },
            PreflightAllowList.NamedWithCjk("KStem"));
        Console.WriteLine($"Benchmark lexicon kstem: path=lexicons/kstem-dict.txt, sha256={TextLexicons.Sha256(lexiconPath)}");
    }

    private readonly ReadingTokenSink _readingSink = new();

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_KStem_Analyse()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            _readingSink.Reset();
            _analyser.Analyse(doc.AsSpan(), _readingSink);
            total += _readingSink.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_KStem_Analyse()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var reader = new System.IO.StringReader(doc);
            using var stream = _luceneAnalyser.GetTokenStream("body", reader);
            total += LuceneTermReader.ReadAll(stream);
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_KStem_Analyse_CountOnly()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            var sink = new CountingTokenSink();
            _analyser.Analyse(doc.AsSpan(), sink);
            total += sink.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_KStem_Analyse_CountOnly()
    {
        int total = 0;
        foreach (var doc in _documents)
        {
            using var reader = new System.IO.StringReader(doc);
            using var stream = _luceneAnalyser.GetTokenStream("body", reader);
            stream.Reset();
            while (stream.IncrementToken())
                total++;
            stream.End();
        }
        return total;
    }

    // --- Lucene.NET state ---

    private Lucene.Net.Analysis.Analyzer _luceneAnalyser = null!;

    /// <summary>
    /// Minimal anonymous Analyzer subclass to avoid a separate file.
    /// </summary>
    private sealed class AnalyzerAnonymousClass : Lucene.Net.Analysis.Analyzer
    {
        private readonly Func<string, System.IO.TextReader, Lucene.Net.Analysis.TokenStreamComponents> _createComponents;

        public AnalyzerAnonymousClass(
            Func<string, System.IO.TextReader, Lucene.Net.Analysis.TokenStreamComponents> createComponents)
        {
            _createComponents = createComponents;
        }

        protected override Lucene.Net.Analysis.TokenStreamComponents CreateComponents(
            string fieldName, System.IO.TextReader reader)
        {
            return _createComponents(fieldName, reader);
        }
    }
}
