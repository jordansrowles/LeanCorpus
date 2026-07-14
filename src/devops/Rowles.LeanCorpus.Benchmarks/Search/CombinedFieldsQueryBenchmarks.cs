using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
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
/// Measures <see cref="CombinedFieldsQuery"/> multi-field BM25F throughput
/// against an equivalent <see cref="BooleanQuery"/> across title and body fields.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob]
public class CombinedFieldsQueryBenchmarks
{
    private const int TopN = 25;
    private const string QueryTerm1 = "government";
    private const string QueryTerm2 = "market";
    private const string FieldTitle = "title";
    private const string FieldBody = "body";

    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    [Params(1, 2)]
    public int MinimumShouldMatch { get; set; } = 1;

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexSearcher? _leanSearcher;

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
        _leanSearcher?.Dispose();
        if (!string.IsNullOrWhiteSpace(_leanIndexPath) && IODirectory.Exists(_leanIndexPath))
            IODirectory.Delete(_leanIndexPath, recursive: true);

        _luceneReader?.Dispose();
        _luceneDirectory?.Dispose();
        if (!string.IsNullOrWhiteSpace(_luceneIndexPath) && IODirectory.Exists(_luceneIndexPath))
            IODirectory.Delete(_luceneIndexPath, recursive: true);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_CombinedFieldsQuery()
    {
        var q = new CombinedFieldsQuery(
            [FieldTitle, FieldBody],
            [QueryTerm1, QueryTerm2],
            minimumShouldMatch: MinimumShouldMatch,
            fieldWeights: new Dictionary<string, float> { [FieldTitle] = 2.0f, [FieldBody] = 1.0f });
        return _leanSearcher!.Search(q, TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_BooleanQuery_MultiField()
    {
        var builder = new Rowles.LeanCorpus.Search.Queries.BooleanQuery.Builder();
        builder.Add(new TermQuery(FieldTitle, QueryTerm1), Rowles.LeanCorpus.Search.Occur.Should);
        builder.Add(new TermQuery(FieldTitle, QueryTerm2), Rowles.LeanCorpus.Search.Occur.Should);
        builder.Add(new TermQuery(FieldBody, QueryTerm1), Rowles.LeanCorpus.Search.Occur.Should);
        builder.Add(new TermQuery(FieldBody, QueryTerm2), Rowles.LeanCorpus.Search.Occur.Should);
        return _leanSearcher!.Search(builder.Build(), TopN).TotalHits;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_BooleanQuery_MultiField()
    {
        var bq = new Lucene.Net.Search.BooleanQuery();
        bq.Add(new LuceneTermQuery(new LuceneTerm(FieldTitle, QueryTerm1)), Occur.SHOULD);
        bq.Add(new LuceneTermQuery(new LuceneTerm(FieldTitle, QueryTerm2)), Occur.SHOULD);
        bq.Add(new LuceneTermQuery(new LuceneTerm(FieldBody, QueryTerm1)), Occur.SHOULD);
        bq.Add(new LuceneTermQuery(new LuceneTerm(FieldBody, QueryTerm2)), Occur.SHOULD);
        return _luceneSearcher!.Search(bq, TopN).TotalHits;
    }

    private void BuildLuceneIndex(string[] documents)
    {
        _luceneIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"lucenenet-bench-combined-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_luceneIndexPath);
        _luceneDirectory = new LuceneMMapDirectory(new System.IO.DirectoryInfo(_luceneIndexPath));
        var analyser = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        using var writer = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, analyser));
        AddDocuments(documents, (id, title, body) =>
        {
            var doc = new LuceneDocument();
            doc.Add(new LuceneStringField("id", id, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField(FieldTitle, title, Lucene.Net.Documents.Field.Store.NO));
            doc.Add(new LuceneTextField(FieldBody, body, Lucene.Net.Documents.Field.Store.NO));
            writer.AddDocument(doc);
        });
        writer.Commit();
        _luceneReader = LuceneDirectoryReader.Open(_luceneDirectory);
        _luceneSearcher = new LuceneIndexSearcher(_luceneReader);
    }

    private void BuildLeanIndex(string[] documents)
    {
        _leanIndexPath = Path.Combine(BenchmarkHelpers.TempRoot, $"leancorpus-bench-combined-{Guid.NewGuid():N}");
        IODirectory.CreateDirectory(_leanIndexPath);
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        using var writer = new Rowles.LeanCorpus.Index.Indexer.IndexWriter(
            _leanDirectory,
            new Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig { MaxBufferedDocs = 10_000, RamBufferSizeMB = 256 });
        AddDocuments(documents, (id, title, body) =>
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", id));
            doc.Add(new LeanTextField(FieldTitle, title));
            doc.Add(new LeanTextField(FieldBody, body));
            writer.AddDocument(doc);
        });
        writer.Commit();
        _leanSearcher = new LeanIndexSearcher(_leanDirectory);
    }

    /// <summary>
    /// Iterates <paramref name="documents"/>, extracts a title from the first
    /// sentence, then calls <paramref name="add"/> for each document.
    /// </summary>
    private static void AddDocuments(
        string[] documents,
        Action<string, string, string> add)
    {
        for (int i = 0; i < documents.Length; i++)
        {
            var body = documents[i];
            var dot = body.IndexOf('.', StringComparison.Ordinal);
            var title = dot > 0 && dot <= 120 ? body[..dot] : body[..Math.Min(80, body.Length)];
            add(i.ToString(System.Globalization.CultureInfo.InvariantCulture), title, body);
        }
    }
}
