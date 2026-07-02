using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Rowles.LeanCorpus.Search.Searcher;
using IODirectory = System.IO.Directory;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneDocument = Lucene.Net.Documents.Document;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneTermQuery = Lucene.Net.Search.TermQuery;
using LuceneTerm = Lucene.Net.Index.Term;
using TermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Compares warm query-cache throughput against a cold (disabled) cache.
/// The cache is populated during BenchmarkDotNet's pilot phase so
/// actual measurements reflect the steady-state hot path.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class QueryCacheBenchmarks
{
    private const int TopN = 25;
    private const string FieldBody = "body";
    private const string QueryTerm1 = "government";
    private const string QueryTerm2 = "market";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _cachedSearcher;
    private LeanIndexSearcher? _uncachedSearcher;

    // Lucene.NET state
    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private LuceneDirectoryReader? _luceneReader;
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
        _cachedSearcher?.Dispose();
        _uncachedSearcher?.Dispose();
        DeleteDir(_leanIndexPath);

        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        DeleteDir(_luceneIndexPath);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NoCache()
        => _uncachedSearcher!.Search(new TermQuery(FieldBody, QueryTerm1), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_WithCache()
        => _cachedSearcher!.Search(new TermQuery(FieldBody, QueryTerm1), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_WithCache_BooleanQuery()
        => _cachedSearcher!.Search(BuildBooleanQuery(), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_NoCache_BooleanQuery()
        => _uncachedSearcher!.Search(BuildBooleanQuery(), TopN).TotalHits;

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_TermQuery()
    {
        var q = new LuceneTermQuery(new LuceneTerm(FieldBody, QueryTerm1));
        return _luceneSearcher!.Search(q, TopN).TotalHits;
    }

    private static Rowles.LeanCorpus.Search.Queries.BooleanQuery BuildBooleanQuery()
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        builder.Add(new TermQuery(FieldBody, QueryTerm1), Rowles.LeanCorpus.Search.Occur.Must);
        builder.Add(new TermQuery(FieldBody, QueryTerm2), Rowles.LeanCorpus.Search.Occur.Should);
        return builder.Build();
    }

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-cache-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);
        _luceneDirectory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        AddDocuments(documents, (id, body) =>
        {
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id", id, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField(FieldBody, body, Lucene.Net.Documents.Field.Store.NO));
            writer.AddDocument(doc);
        });
        writer.Commit();
        _luceneReader = LuceneDirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
    }

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-cache-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        AddDocuments(documents, (id, body) =>
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", id));
            doc.Add(new LeanTextField(FieldBody, body));
            writer.AddDocument(doc);
        });
        writer.Commit();

        _cachedSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { EnableQueryCache = true, QueryCacheMaxEntries = 1024 });

        _uncachedSearcher = new LeanIndexSearcher(
            _leanDirectory,
            new IndexSearcherConfig { EnableQueryCache = false });
    }

    private static void AddDocuments(
        string[] documents,
        Action<string, string> add)
    {
        for (int i = 0; i < documents.Length; i++)
            add(i.ToString(System.Globalization.CultureInfo.InvariantCulture), documents[i]);
    }

    private static void DeleteDir(string path)
    {
        if (!string.IsNullOrWhiteSpace(path) && IODirectory.Exists(path))
            IODirectory.Delete(path, recursive: true);
    }
}
