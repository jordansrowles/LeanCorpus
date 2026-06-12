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
[SimpleJob]
public class KStemmerParityBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private IAnalyser _analyser = null!;

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);

        var lexiconPath = FindKStemLexiconPath();
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
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_KStem_Analyse()
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
    public int LuceneNet_KStem_Analyse()
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

    private static string FindKStemLexiconPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "lexicons", "kstem-dict.txt")))
            dir = dir.Parent;
        return dir is not null
            ? Path.Combine(dir.FullName, "lexicons", "kstem-dict.txt")
            : throw new InvalidOperationException("Could not find lexicons/kstem-dict.txt.");
    }
}
