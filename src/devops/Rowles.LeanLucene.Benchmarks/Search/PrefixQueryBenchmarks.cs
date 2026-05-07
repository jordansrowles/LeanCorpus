using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanLucene.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanLucene.Store.MMapDirectory;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanLucene.Benchmarks;

/// <summary>
/// Measures PrefixQuery performance across LeanLucene and Lucene.NET.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class PrefixQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("gov", "pres", "mark")]
    public string QueryPrefix { get; set; } = "gov";

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

    private RAMDirectory? _luceneDirectory;
    private StandardAnalyzer? _luceneAnalyzer;
    private DirectoryReader? _luceneReader;
    private LuceneIndexSearcher? _luceneSearcher;

    [GlobalSetup]
    public void Setup()
    {
        var documents = BenchmarkData.BuildDocuments(DocumentCount);
        BuildLeanIndex(documents);
        BuildLuceneIndex(documents);
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
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_PrefixQuery()
    {
        var query = new Rowles.LeanLucene.Search.Queries.PrefixQuery("body", QueryPrefix);
        return _leanSearcher!.Search(query, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_PrefixQuery()
    {
        var query = new Lucene.Net.Search.PrefixQuery(new Term("body", QueryPrefix));
        return _luceneSearcher!.Search(query, TopN).TotalHits;
    }

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-prefix-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);

        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using (var writer = new Rowles.LeanLucene.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanLucene.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 }))
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
        _luceneDirectory = new RAMDirectory();
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

}
