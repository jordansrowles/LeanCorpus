using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures BooleanQuery search performance across deterministic clause shapes.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class BooleanQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("Must2Common", "Must3Mixed", "Should2Common", "Should4Mixed", "MustNotCommon")]
    public string BooleanShape { get; set; } = "Must2Common";

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;
    private Rowles.LeanCorpus.Search.Query? _leanQuery;

    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private StandardAnalyzer? _luceneAnalyzer;
    private DirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;
    private Lucene.Net.Search.Query? _luceneQuery;

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        BuildLeanIndex(documents);
        BuildLuceneIndex(documents);
        _leanQuery = BuildLeanQuery(BooleanShape);
        _luceneQuery = BuildLuceneQuery(BooleanShape);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_luceneIndexPath) && IODirectory.Exists(_luceneIndexPath))
            IODirectory.Delete(_luceneIndexPath, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BooleanQuery()
    {
        return _leanSearcher!.Search(_leanQuery!, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_BooleanQuery()
    {
        return _luceneSearcher!.Search(_luceneQuery!, TopN).TotalHits;
    }

    // --- Index builders ---

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leancorpus-bench-bool-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);

        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using (var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 }))
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

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneIndexPath = Path.Combine(Path.GetTempPath(), $"lucenenet-bench-bool-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);

        _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

        using (var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, _luceneAnalyzer)))
        {
            for (int i = 0; i < documents.Length; i++)
            {
                var doc = new Lucene.Net.Documents.Document
                {
                    new LuceneStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture), Field.Store.NO),
                    new LuceneTextField("body", documents[i], Field.Store.NO)
                };
                writer.AddDocument(doc);
            }
            writer.Commit();
        }

        _luceneReader = DirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
    }

    private static Rowles.LeanCorpus.Search.Query BuildLeanQuery(string shape)
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        switch (shape)
        {
            case "Must2Common":
                AddLean(builder, "government", Rowles.LeanCorpus.Search.Occur.Must);
                AddLean(builder, "said", Rowles.LeanCorpus.Search.Occur.Must);
                break;
            case "Must3Mixed":
                AddLean(builder, "government", Rowles.LeanCorpus.Search.Occur.Must);
                AddLean(builder, "economic", Rowles.LeanCorpus.Search.Occur.Must);
                AddLean(builder, "people", Rowles.LeanCorpus.Search.Occur.Must);
                break;
            case "Should2Common":
                AddLean(builder, "market", Rowles.LeanCorpus.Search.Occur.Should);
                AddLean(builder, "people", Rowles.LeanCorpus.Search.Occur.Should);
                break;
            case "Should4Mixed":
                AddLean(builder, "government", Rowles.LeanCorpus.Search.Occur.Should);
                AddLean(builder, "people", Rowles.LeanCorpus.Search.Occur.Should);
                AddLean(builder, "market", Rowles.LeanCorpus.Search.Occur.Should);
                AddLean(builder, "president", Rowles.LeanCorpus.Search.Occur.Should);
                break;
            case "MustNotCommon":
                AddLean(builder, "government", Rowles.LeanCorpus.Search.Occur.Must);
                AddLean(builder, "market", Rowles.LeanCorpus.Search.Occur.MustNot);
                break;
            default:
                throw new InvalidOperationException($"Unknown boolean benchmark shape '{shape}'.");
        }

        return builder.Build();
    }

    private static Lucene.Net.Search.Query BuildLuceneQuery(string shape)
    {
        var query = new Lucene.Net.Search.BooleanQuery();
        switch (shape)
        {
            case "Must2Common":
                AddLucene(query, "government", Occur.MUST);
                AddLucene(query, "said", Occur.MUST);
                break;
            case "Must3Mixed":
                AddLucene(query, "government", Occur.MUST);
                AddLucene(query, "economic", Occur.MUST);
                AddLucene(query, "people", Occur.MUST);
                break;
            case "Should2Common":
                AddLucene(query, "market", Occur.SHOULD);
                AddLucene(query, "people", Occur.SHOULD);
                break;
            case "Should4Mixed":
                AddLucene(query, "government", Occur.SHOULD);
                AddLucene(query, "people", Occur.SHOULD);
                AddLucene(query, "market", Occur.SHOULD);
                AddLucene(query, "president", Occur.SHOULD);
                break;
            case "MustNotCommon":
                AddLucene(query, "government", Occur.MUST);
                AddLucene(query, "market", Occur.MUST_NOT);
                break;
            default:
                throw new InvalidOperationException($"Unknown boolean benchmark shape '{shape}'.");
        }

        return query;
    }

    private static void AddLean(
        Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder builder,
        string term,
        Rowles.LeanCorpus.Search.Occur occur)
    {
        builder.Add(new Rowles.LeanCorpus.Search.Queries.TermQuery("body", term), occur);
    }

    private static void AddLucene(
        Lucene.Net.Search.BooleanQuery query,
        string term,
        Occur occur)
    {
        query.Add(new Lucene.Net.Search.TermQuery(new Term("body", term)), occur);
    }

}
