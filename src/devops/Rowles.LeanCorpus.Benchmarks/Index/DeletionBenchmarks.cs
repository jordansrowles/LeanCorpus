using BenchmarkDotNet.Attributes;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Util;
using LeanMatchAllDocsQuery = Rowles.LeanCorpus.Search.Queries.MatchAllDocsQuery;
using LeanIndexSearcher = Rowles.LeanCorpus.Search.Searcher.IndexSearcher;
using LeanDocument = Rowles.LeanCorpus.Document.LeanDocument;
using LeanIndexWriter = Rowles.LeanCorpus.Index.Indexer.IndexWriter;
using LeanIndexWriterConfig = Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig;
using LeanMMapDirectory = Rowles.LeanCorpus.Store.MMapDirectory;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTermQuery = Rowles.LeanCorpus.Search.Queries.TermQuery;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;
using LuceneDirectoryReader = Lucene.Net.Index.DirectoryReader;
using LuceneIndexSearcher = Lucene.Net.Search.IndexSearcher;
using LuceneMatchAllDocsQuery = Lucene.Net.Search.MatchAllDocsQuery;
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
[WarmupCount(1)]
[IterationCount(3)]
[InvocationCount(1)]
public class DeletionQueueBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private int _deleteCount;
    private bool _leanDeletesQueued;
    private bool _luceneDeletesQueued;

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
        _leanIndexPath = BenchmarkHelpers.CreateTempDirectory("leancorpus-bench-del-queue");
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        _leanWriter = new LeanIndexWriter(_leanDirectory, CreateLeanWriterConfig());
        DeletionBenchmarkFixture.AddLeanDocuments(_leanWriter, _documents);
        _leanWriter.Commit();

        _luceneIndexPath = BenchmarkHelpers.CreateTempDirectory("lucenenet-bench-del-queue");
        _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        _luceneWriter = new Lucene.Net.Index.IndexWriter(_luceneDirectory,
            DeletionBenchmarkFixture.CreateLuceneWriterConfig(_luceneAnalyzer));
        DeletionBenchmarkFixture.AddLuceneDocuments(_luceneWriter, _documents);
        _luceneWriter.Commit();
        _leanDeletesQueued = false;
        _luceneDeletesQueued = false;
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _leanWriter?.Commit();
        _luceneWriter?.Commit();
        _leanWriter?.Dispose();
        _luceneWriter?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();

        try
        {
            DeletionBenchmarkFixture.VerifyLeanIds(
                _leanIndexPath,
                DeletionBenchmarkFixture.GetSurvivingIds(DocumentCount, _leanDeletesQueued ? _deleteCount : 0));
            DeletionBenchmarkFixture.VerifyLuceneIds(
                _luceneIndexPath,
                DeletionBenchmarkFixture.GetSurvivingIds(DocumentCount, _luceneDeletesQueued ? _deleteCount : 0));
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
            BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
        }
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanLucene_QueueDeletes()
    {
        for (int i = 0; i < _deleteCount; i++)
            _leanWriter!.DeleteDocuments(new LeanTermQuery("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        _leanDeletesQueued = true;
        return _deleteCount;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_QueueDeletes()
    {
        for (int i = 0; i < _deleteCount; i++)
            _luceneWriter!.DeleteDocuments(new Term("id", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));

        _luceneDeletesQueued = true;
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
[WarmupCount(1)]
[IterationCount(3)]
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
        DeletionBenchmarkFixture.AddLeanDocuments(_leanWriter, _documents);
        _leanWriter.Commit();
        DeletionBenchmarkFixture.QueueLeanDeletes(_leanWriter, _deleteCount);

        _luceneIndexPath = BenchmarkHelpers.CreateTempDirectory("lucenenet-bench-del-commit");
        _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        _luceneWriter = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            DeletionBenchmarkFixture.CreateLuceneWriterConfig(_luceneAnalyzer));
        DeletionBenchmarkFixture.AddLuceneDocuments(_luceneWriter, _documents);
        _luceneWriter.Commit();
        DeletionBenchmarkFixture.QueueLuceneDeletes(_luceneWriter, _deleteCount);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        _leanWriter?.Commit();
        _luceneWriter?.Commit();
        _leanWriter?.Dispose();
        _luceneWriter?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();

        try
        {
            DeletionBenchmarkFixture.VerifyLeanIds(
                _leanIndexPath,
                DeletionBenchmarkFixture.GetSurvivingIds(DocumentCount, _deleteCount));
            DeletionBenchmarkFixture.VerifyLuceneIds(
                _luceneIndexPath,
                DeletionBenchmarkFixture.GetSurvivingIds(DocumentCount, _deleteCount));
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
            BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
        }
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
}

/// <summary>Deterministic DataForge-backed fixtures and read-back checks for deletion benchmarks.</summary>
internal static class DeletionBenchmarkFixture
{
    internal static Lucene.Net.Index.IndexWriterConfig CreateLuceneWriterConfig(StandardAnalyzer analyzer)
        => new(LuceneVersion.LUCENE_48, analyzer)
        {
            MaxBufferedDocs = 10_000,
            RAMBufferSizeMB = 256,
            MergePolicy = Lucene.Net.Index.NoMergePolicy.NO_COMPOUND_FILES
        };

    internal static void AddLeanDocuments(LeanIndexWriter writer, IReadOnlyList<string> bodies)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            var document = new LeanDocument();
            document.Add(new LeanStringField("id", FormatId(i), stored: true));
            document.Add(new LeanTextField("body", bodies[i]));
            writer.AddDocument(document);
        }
    }

    internal static void AddLuceneDocuments(Lucene.Net.Index.IndexWriter writer, IReadOnlyList<string> bodies)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            var document = new Lucene.Net.Documents.Document
            {
                new LuceneStringField("id", FormatId(i), Field.Store.YES),
                new LuceneTextField("body", bodies[i], Field.Store.NO)
            };
            writer.AddDocument(document);
        }
    }

    internal static void QueueLeanDeletes(LeanIndexWriter writer, int deleteCount)
    {
        for (int i = 0; i < deleteCount; i++)
            writer.DeleteDocuments(new LeanTermQuery("id", FormatId(i)));
    }

    internal static void QueueLuceneDeletes(Lucene.Net.Index.IndexWriter writer, int deleteCount)
    {
        for (int i = 0; i < deleteCount; i++)
            writer.DeleteDocuments(new Term("id", FormatId(i)));
    }

    internal static string[] GetSurvivingIds(int documentCount, int deleteCount)
        => Enumerable.Range(deleteCount, documentCount - deleteCount)
            .Select(FormatId)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();

    internal static void VerifyLeanIds(string path, IReadOnlyList<string> expectedIds)
    {
        using var directory = new LeanMMapDirectory(path);
        using var searcher = new LeanIndexSearcher(directory);
        var result = searcher.Search(new LeanMatchAllDocsQuery(), expectedIds.Count);
        if (result.TotalHits != expectedIds.Count || result.ScoreDocs.Length != expectedIds.Count)
            throw new InvalidOperationException(
                $"LeanCorpus deletion verification expected {expectedIds.Count} live documents, got {result.TotalHits} total hits and {result.ScoreDocs.Length} returned.");

        string[] actualIds = result.ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"][0])
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        EnsureExactIds("LeanCorpus", expectedIds, actualIds);
    }

    internal static void VerifyLuceneIds(string path, IReadOnlyList<string> expectedIds)
    {
        using var directory = new LuceneMMapDirectory(new DirectoryInfo(path));
        using var reader = LuceneDirectoryReader.Open(directory);
        var searcher = new LuceneIndexSearcher(reader);
        var result = searcher.Search(new LuceneMatchAllDocsQuery(), expectedIds.Count);
        if (result.TotalHits != expectedIds.Count || result.ScoreDocs.Length != expectedIds.Count)
            throw new InvalidOperationException(
                $"Lucene.NET deletion verification expected {expectedIds.Count} live documents, got {result.TotalHits} total hits and {result.ScoreDocs.Length} returned.");

        string[] actualIds = result.ScoreDocs
            .Select(hit => reader.Document(hit.Doc).Get("id")
                ?? throw new InvalidOperationException("Lucene.NET deletion verification found a live document without an ID."))
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        EnsureExactIds("Lucene.NET", expectedIds, actualIds);
    }

    internal static string FormatId(int value)
        => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static void EnsureExactIds(string engine, IReadOnlyList<string> expectedIds, IReadOnlyList<string> actualIds)
    {
        if (!actualIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
            throw new InvalidOperationException($"{engine} deletion verification did not preserve the exact expected ID set.");
    }
}

/// <summary>Measures replacing ten per cent of documents, committing and publishing the refreshed searcher.</summary>
[MemoryDiagnoser]
[HtmlExporter]
[JsonExporterAttribute.Full]
[MarkdownExporterAttribute.GitHub]
[RPlotExporter]
[WarmupCount(1)]
[IterationCount(3)]
[InvocationCount(1)]
public class DeletionReplacementRefreshBenchmarks
{
    public static IEnumerable<int> DocCounts => BenchmarkData.GetDocCounts(1_000, BenchmarkData.DefaultDocCount);

    [ParamsSource(nameof(DocCounts))]
    public int DocumentCount { get; set; }

    private string[] _documents = [];
    private string[] _expectedIds = [];
    private int _replacementCount;
    private string _path = string.Empty;
    private LeanMMapDirectory? _directory;
    private LeanIndexWriter? _writer;
    private Rowles.LeanCorpus.Search.Searcher.SearcherManager? _searcherManager;
    private bool _replacementRefreshCompleted;

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _replacementCount = Math.Max(1, DocumentCount / 10);
        _expectedIds = Enumerable.Range(0, DocumentCount)
            .Select(DeletionBenchmarkFixture.FormatId)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _path = BenchmarkHelpers.CreateTempDirectory("leancorpus-bench-delete-replace-refresh");
        _directory = new LeanMMapDirectory(_path);
        _writer = new LeanIndexWriter(_directory, new LeanIndexWriterConfig
        {
            MaxBufferedDocs = 10_000,
            RamBufferSizeMB = 256,
            MergePolicy = Rowles.LeanCorpus.Index.Indexer.NoMergePolicy.Instance
        });

        for (int i = 0; i < _documents.Length; i++)
            _writer.AddDocument(CreateDocument(i, "0", _documents[i]));
        _writer.Commit();
        _searcherManager = new Rowles.LeanCorpus.Search.Searcher.SearcherManager(
            _directory,
            new Rowles.LeanCorpus.Search.Searcher.SearcherManagerConfig { RefreshInterval = TimeSpan.FromDays(1) });
        _replacementRefreshCompleted = false;
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        try
        {
            VerifyPublishedDocuments(_replacementRefreshCompleted ? "1" : "0");
        }
        finally
        {
            _searcherManager?.Dispose();
            _writer?.Dispose();
            _directory?.Dispose();
            BenchmarkHelpers.DeleteDirectory(_path);
        }
    }

    [Benchmark(Description = "Replace ten per cent, commit and refresh")]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_ReplaceAndRefresh()
    {
        for (int i = 0; i < _replacementCount; i++)
            _writer!.UpdateDocument("id", DeletionBenchmarkFixture.FormatId(i), CreateDocument(i, "1", _documents[i]));

        _writer!.Commit();
        if (!_searcherManager!.MaybeRefresh())
            throw new InvalidOperationException("Deletion replacement preflight expected the committed replacement to refresh.");

        _replacementRefreshCompleted = true;
        return _replacementCount;
    }

    private void VerifyPublishedDocuments(string replacedGeneration)
    {
        using var lease = _searcherManager!.AcquireLease();
        var result = lease.Searcher.Search(new LeanMatchAllDocsQuery(), DocumentCount);
        if (result.TotalHits != DocumentCount || result.ScoreDocs.Length != DocumentCount)
            throw new InvalidOperationException(
                $"Replacement/refresh verification expected {DocumentCount} live documents, got {result.TotalHits} total hits and {result.ScoreDocs.Length} returned.");

        var actual = result.ScoreDocs
            .Select(hit =>
            {
                var fields = lease.Searcher.GetStoredFields(hit.DocId);
                return (Id: fields["id"][0], Generation: fields["generation"][0]);
            })
            .OrderBy(static item => item.Id, StringComparer.Ordinal)
            .ToArray();

        if (actual.Length != DocumentCount)
            throw new InvalidOperationException($"Replacement/refresh verification expected {DocumentCount} IDs, got {actual.Length}.");

        if (!actual.Select(static item => item.Id).SequenceEqual(_expectedIds, StringComparer.Ordinal))
            throw new InvalidOperationException("Replacement/refresh verification did not preserve the exact logical ID set.");

        foreach (var item in actual)
        {
            int id = int.Parse(item.Id, System.Globalization.CultureInfo.InvariantCulture);
            string expectedGeneration = id < _replacementCount ? replacedGeneration : "0";
            if (!item.Generation.Equals(expectedGeneration, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Replacement/refresh verification expected ID {item.Id} at generation {expectedGeneration}, got generation {item.Generation}.");
            }
        }
    }

    private static LeanDocument CreateDocument(int id, string generation, string body)
    {
        var document = new LeanDocument();
        document.Add(new LeanStringField("id", DeletionBenchmarkFixture.FormatId(id), stored: true));
        document.Add(new LeanStringField("generation", generation, stored: true));
        document.Add(new LeanTextField("body", body));
        return document;
    }
}
