using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Rowles.LeanLucene.Index;
using LeanDocument = Rowles.LeanLucene.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanLucene.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanLucene.Store.MMapDirectory;
using LeanStringField = Rowles.LeanLucene.Document.Fields.StringField;
using LeanTermQuery = Rowles.LeanLucene.Search.Queries.TermQuery;
using LeanTextField = Rowles.LeanLucene.Document.Fields.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;

namespace Rowles.LeanLucene.Benchmarks;

[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[KeepBenchmarkFiles]
[SimpleJob]
public class TermQueryBenchmarks
{
    private const int TopN = 25;

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params("said", "government", "people")]
    public string QueryTerm { get; set; } = "said";

    private string[] _documents = [];
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
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        BuildLeanLuceneIndex();
        BuildLuceneNetIndex();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && System.IO.Directory.Exists(_leanIndexPath))
            System.IO.Directory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_TermQuery()
    {
        var topDocs = _leanSearcher!.Search(new LeanTermQuery("body", QueryTerm), TopN);
        return topDocs.TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_TermQuery()
    {
        var query = new LuceneTermQuery(new Term("body", QueryTerm));
        var topDocs = _luceneSearcher!.Search(query, TopN);
        return topDocs.TotalHits;
    }

    private void BuildLeanLuceneIndex()
    {
        _leanIndexPath = Path.Combine(Path.GetTempPath(), $"leanlucene-bench-search-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(_leanIndexPath);

        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using (var writer = new Rowles.LeanLucene.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanLucene.Index.Indexer.IndexWriterConfig
            {
                MaxBufferedDocs = 10_000,
                RamBufferSizeMB = 256
            }))
        {
            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new LeanDocument();
                doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                doc.Add(new LeanTextField("body", _documents[i]));
                writer.AddDocument(doc);
            }

            writer.Commit();
        }

        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }

    private void BuildLuceneNetIndex()
    {
        _luceneDirectory = new RAMDirectory();
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);

        using (var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, _luceneAnalyzer)))
        {
            for (int i = 0; i < _documents.Length; i++)
            {
                var doc = new Lucene.Net.Documents.Document
                {
                    new LuceneStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture), Field.Store.NO),
                    new LuceneTextField("body", _documents[i], Field.Store.NO)
                };
                writer.AddDocument(doc);
            }

            writer.Commit();
        }

        _luceneReader = DirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
    }

}
