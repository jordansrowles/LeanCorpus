using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Util;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneMMapDirectory = Lucene.Net.Store.MMapDirectory;
using LuceneStringField = Lucene.Net.Documents.StringField;
using LuceneTextField = Lucene.Net.Documents.TextField;

namespace Rowles.LeanCorpus.Benchmarks;

/// <summary>
/// Measures the cost of queueing delete terms without applying them to segment files.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
[InvocationCount(1)]
public class DeletionQueueBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private int _deleteCount;

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexWriter? _leanWriter;

    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private StandardAnalyzer? _luceneAnalyzer;
    private Lucene.Net.Index.IndexWriter? _luceneWriter;

    [GlobalSetup]
    public void Setup()
    {
        _deleteCount = Math.Max(1, DocumentCount / 10);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _leanIndexPath = BenchmarkHelpers.CreateTempDirectory("leancorpus-bench-del-queue");
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        _leanWriter = new LeanIndexWriter(_leanDirectory, CreateLeanWriterConfig());

        _luceneIndexPath = BenchmarkHelpers.CreateTempDirectory("lucenenet-bench-del-queue");
        _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        _luceneWriter = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, _luceneAnalyzer));
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _leanWriter?.Dispose();
        _luceneWriter?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();

        BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
        BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_QueueDeletes()
    {
        for (int i = 0; i < _deleteCount; i++)
            _leanWriter!.DeleteDocuments(new LeanTermQuery("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        return _deleteCount;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_QueueDeletes()
    {
        for (int i = 0; i < _deleteCount; i++)
            _luceneWriter!.DeleteDocuments(new Term("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        return _deleteCount;
    }

    internal static LeanIndexWriterConfig CreateLeanWriterConfig() => new()
    {
        MaxBufferedDocs = 10_000,
        RamBufferSizeMB = 256,
        MergeThreshold = int.MaxValue,
    };


}

/// <summary>
/// Measures the cost of applying already-queued deletes during commit.
/// </summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[SimpleJob(warmupCount: 1, iterationCount: 3)]
[InvocationCount(1)]
public class DeletionCommitBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private int _deleteCount;

    private string _leanIndexPath = string.Empty;
    private LeanMMapDirectory? _leanDirectory;
    private LeanIndexWriter? _leanWriter;

    private string _luceneIndexPath = string.Empty;
    private LuceneMMapDirectory? _luceneDirectory;
    private StandardAnalyzer? _luceneAnalyzer;
    private Lucene.Net.Index.IndexWriter? _luceneWriter;

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _deleteCount = Math.Max(1, DocumentCount / 10);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _leanIndexPath = BenchmarkHelpers.CreateTempDirectory("leancorpus-bench-del-commit");
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        _leanWriter = new LeanIndexWriter(_leanDirectory, DeletionQueueBenchmarks.CreateLeanWriterConfig());
        IndexLeanDocuments(_leanWriter, _documents);
        _leanWriter.Commit();
        QueueLeanDeletes(_leanWriter);

        _luceneIndexPath = BenchmarkHelpers.CreateTempDirectory("lucenenet-bench-del-commit");
        _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        _luceneWriter = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            new Lucene.Net.Index.IndexWriterConfig(LuceneVersion.LUCENE_48, _luceneAnalyzer));
        IndexLuceneDocuments(_luceneWriter, _documents);
        _luceneWriter.Commit();
        QueueLuceneDeletes(_luceneWriter);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _leanWriter?.Dispose();
        _luceneWriter?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();

        BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
        BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_CommitDeletes()
    {
        _leanWriter!.Commit();
        return _deleteCount;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_CommitDeletes()
    {
        _luceneWriter!.Commit();
        return _deleteCount;
    }

    private void QueueLeanDeletes(LeanIndexWriter writer)
    {
        for (int i = 0; i < _deleteCount; i++)
            writer.DeleteDocuments(new LeanTermQuery("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private void QueueLuceneDeletes(Lucene.Net.Index.IndexWriter writer)
    {
        for (int i = 0; i < _deleteCount; i++)
            writer.DeleteDocuments(new Term("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static void IndexLeanDocuments(LeanIndexWriter writer, IReadOnlyList<string> documents)
    {
        for (int i = 0; i < documents.Count; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new LeanStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            doc.Add(new LeanTextField("body", documents[i]));
            writer.AddDocument(doc);
        }
    }

    private static void IndexLuceneDocuments(Lucene.Net.Index.IndexWriter writer, IReadOnlyList<string> documents)
    {
        for (int i = 0; i < documents.Count; i++)
        {
            var doc = new Lucene.Net.Documents.Document
            {
                new LuceneStringField("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture), Field.Store.NO),
                new LuceneTextField("body", documents[i], Field.Store.NO)
            };
            writer.AddDocument(doc);
        }
    }
}
