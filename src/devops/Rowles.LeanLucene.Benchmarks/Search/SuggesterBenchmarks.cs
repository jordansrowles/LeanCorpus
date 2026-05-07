using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search.Spell;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Rowles.LeanLucene.Search.Suggestions;
using Rowles.LeanLucene.Store;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanLucene.Search.Searcher.IndexSearcher;
using LeanIndexWriter = Rowles.LeanLucene.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanLucene.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanLucene.Store.MMapDirectory;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Compares DidYouMean suggestion speed against Lucene.NET SpellChecker.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class SuggesterBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private (string Original, string Misspelled)[] _misspelledTerms = [];

    // LeanLucene state
    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;
    private SpellIndex? _leanSpellIndex;

    // Lucene.NET state
    private RAMDirectory? _luceneSourceDirectory;
    private RAMDirectory? _luceneSpellDirectory;
    private SpellChecker? _luceneSpellChecker;

    [GlobalSetup]
    public void Setup()
    {
        _misspelledTerms = BenchmarkData.BuildMisspelledTerms();
        var documents = BenchmarkData.BuildDocuments(DocumentCount);

        BuildLeanLuceneIndex(documents);
        _leanSpellIndex = SpellIndex.Build(_leanSearcher!, "body");
        BuildLuceneNetSpellChecker(documents);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && System.IO.Directory.Exists(_leanIndexPath))
            System.IO.Directory.Delete(_leanIndexPath, recursive: true);

        _luceneSpellChecker?.Dispose();
        _luceneSourceDirectory?.Dispose();
        _luceneSpellDirectory?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_DidYouMean()
    {
        int total = 0;
        foreach (var (_, misspelled) in _misspelledTerms)
        {
            var suggestions = DidYouMeanSuggester.Suggest(_leanSearcher!, "body", misspelled, maxEdits: 2, topN: 5);
            total += suggestions.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_SpellIndex()
    {
        int total = 0;
        foreach (var (_, misspelled) in _misspelledTerms)
        {
            var suggestions = _leanSpellIndex!.Suggest(misspelled, maxEdits: 2, topN: 5);
            total += suggestions.Count;
        }
        return total;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_SpellChecker()
    {
        int total = 0;
        foreach (var (_, misspelled) in _misspelledTerms)
        {
            var suggestions = _luceneSpellChecker!.SuggestSimilar(misspelled, 5);
            total += suggestions.Length;
        }
        return total;
    }

    private void BuildLeanLuceneIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-suggest-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(_leanIndexPath);

        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using (var writer = new LeanIndexWriter(_leanDirectory, new LeanIndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256
        }))
        {
            for (int i = 0; i < documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", documents[i]));
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }

    private void BuildLuceneNetSpellChecker(string[] documents)
    {
        _luceneSourceDirectory = new RAMDirectory();
        using var analyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

        using (var writer = new Lucene.Net.Index.IndexWriter(
            _luceneSourceDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer)))
        {
            for (int i = 0; i < documents.Length; i++)
            {
                var doc = new Lucene.Net.Documents.Document
                {
                    new LuceneTextField("body", documents[i], Field.Store.NO)
                };
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _luceneSpellDirectory = new RAMDirectory();
        _luceneSpellChecker = new SpellChecker(_luceneSpellDirectory);

        using var reader = DirectoryReader.Open(_luceneSourceDirectory);
        _luceneSpellChecker.IndexDictionary(
            new LuceneDictionary(reader, "body"),
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer),
            fullMerge: true);
    }
}
