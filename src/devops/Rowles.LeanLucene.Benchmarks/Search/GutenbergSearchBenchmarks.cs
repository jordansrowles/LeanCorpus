using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanLucene.Search.Searcher.IndexSearcher;
using LeanIndexWriter = Rowles.LeanLucene.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanLucene.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanLucene.Store.MMapDirectory;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTermQuery = Rowles.LeanLucene.Search.Queries.TermQuery;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures search throughput on real Project Gutenberg ebook text.
/// Two LeanLucene indexes are built once in setup (standard and English analysers),
/// plus a Lucene.NET index. The benchmark measures only the hot search path.
/// </summary>
/// <remarks>
/// Query terms are pre-processed through each analyser's pipeline so that the
/// query matches what is stored in the index (the English analyser stems index
/// tokens, so the query term must also be stemmed for a correct lookup).
/// </remarks>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[SimpleJob]
public class GutenbergSearchBenchmarks
{
    private const int TopN = 25;

    // Terms that appear across multiple books; root forms are preserved by Porter stemmer,
    // so results are directly comparable between analysers.
    [Params("love", "man", "night", "sea", "death")]
    public string SearchTerm { get; set; } = "love";

    // Pre-analysed query terms
    private string _standardQueryTerm = string.Empty;
    private string _englishQueryTerm  = string.Empty;
    private string _luceneQueryTerm   = string.Empty;

    private string _standardIndexPath = string.Empty;
    private string _englishIndexPath  = string.Empty;
    private string _luceneIndexPath   = string.Empty;

    private LeanMMapDirectory? _standardDirectory;
    private LeanMMapDirectory? _englishDirectory;
    private LeanIndexSearcher? _standardSearcher;
    private LeanIndexSearcher? _englishSearcher;

    private Lucene.Net.Store.FSDirectory? _luceneDirectory;
    private DirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;
    private StandardAnalyzer? _luceneAnalyzer;

    [GlobalSetup]
    public void Setup()
    {
        var paragraphs = GutenbergDataLoader.Load();

        _standardQueryTerm = AnalyseQueryTerm(new StandardAnalyser(), SearchTerm);
        _englishQueryTerm  = AnalyseQueryTerm(new EnglishAnalyser(),  SearchTerm);
        _luceneQueryTerm   = SearchTerm.ToLowerInvariant();

        _standardIndexPath = BuildLeanIndex(paragraphs, new StandardAnalyser(), "standard");
        _englishIndexPath  = BuildLeanIndex(paragraphs, new EnglishAnalyser(),  "english");
        _luceneIndexPath   = BuildLuceneIndex(paragraphs);

        _standardDirectory = new LeanMMapDirectory(_standardIndexPath);
        _englishDirectory  = new LeanMMapDirectory(_englishIndexPath);

        _standardSearcher = new LeanIndexSearcher(_standardDirectory);
        _englishSearcher  = new LeanIndexSearcher(_englishDirectory);

        _luceneDirectory  = Lucene.Net.Store.FSDirectory.Open(new DirectoryInfo(_luceneIndexPath));
        _luceneReader     = DirectoryReader.Open(_luceneDirectory);
        _luceneSearcher   = new LuceneIndexSearcher(_luceneReader);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _standardSearcher?.Dispose();
        _englishSearcher?.Dispose();
        _luceneReader?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();

        _standardDirectory = null;
        _englishDirectory  = null;
        _luceneDirectory   = null;
        _luceneReader      = null;
        _luceneSearcher    = null;

        DeleteDirectory(_standardIndexPath);
        DeleteDirectory(_englishIndexPath);
        DeleteDirectory(_luceneIndexPath);
    }

    /// <summary>
    /// Searches the LeanLucene standard-analyser index with a lowercased query term.
    /// </summary>
    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_Standard_Search()
    {
        var results = _standardSearcher!.Search(new LeanTermQuery("body", _standardQueryTerm), TopN);
        return results.TotalHits;
    }

    /// <summary>
    /// Searches the LeanLucene English-analyser index with a Porter-stemmed query term.
    /// </summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_English_Search()
    {
        var results = _englishSearcher!.Search(new LeanTermQuery("body", _englishQueryTerm), TopN);
        return results.TotalHits;
    }

    /// <summary>
    /// Searches the Lucene.NET standard-analyser index for comparison.
    /// </summary>
    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_Search()
    {
        var query = new LuceneTermQuery(new Term("body", _luceneQueryTerm));
        var results = _luceneSearcher!.Search(query, TopN);
        return results.TotalHits;
    }

    private static string AnalyseQueryTerm(IAnalyser analyser, string term)
    {
        var tokens = analyser.Analyse(term.AsSpan());
        return tokens.Count > 0 ? tokens[0].Text : term;
    }

    private static string BuildLeanIndex(BookParagraph[] paragraphs, IAnalyser analyser, string label)
    {
        var path = Path.Combine(Path.GetTempPath(), $"leanlucene-realdata-{label}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        var directory = new LeanMMapDirectory(path);
        using (var writer = new LeanIndexWriter(directory, new LeanIndexWriterConfig
        {
            DefaultAnalyser = analyser,
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
            DurableCommits = false
        }))
        {
            foreach (var para in paragraphs)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id",    para.Id));
                doc.Add(new LeanStringField("title", para.Title));
                doc.Add(new LeanTextField  ("body",  para.Body));
                writer.AddDocument(doc);
            }

            writer.Commit();
        }

        return path;
    }

    private string BuildLuceneIndex(BookParagraph[] paragraphs)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lucenenet-realdata-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        using var directory = Lucene.Net.Store.FSDirectory.Open(new DirectoryInfo(path));
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using (var writer = new Lucene.Net.Index.IndexWriter(
            directory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, _luceneAnalyzer)))
        {
            foreach (var para in paragraphs)
            {
                var doc = new Lucene.Net.Documents.Document
                {
                    new LuceneStringField("id",    para.Id,    Field.Store.NO),
                    new LuceneStringField("title", para.Title, Field.Store.NO),
                    new LuceneTextField  ("body",  para.Body,  Field.Store.NO)
                };
                writer.AddDocument(doc);
            }

            writer.Commit();
        }

        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
}
