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
// Bounds, not a fixed count: a class-level IterationCount overrides the command line, so the
// reviewed qualification runs could not force the 30 samples their gate needs (ADR047).
[MinIterationCount(3)]
[MaxIterationCount(15)]
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
    private readonly DeletionSurvivorProof _proof = new(typeof(DeletionQueueBenchmarks), "deletion-queue-survivors",
        nameof(LeanCorpus_QueueDeletes), nameof(LuceneNet_QueueDeletes));

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
        DeletionBenchmarkFixture.RecordDurability("deletion-queue");
    }

    [GlobalCleanup]
    public void Cleanup() => _proof.Record(DocumentCount, _deleteCount);

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
        try
        {
            // Queued deletes are not published: the committed index still shows every original.
            DeletionBenchmarkFixture.VerifyLeanIds(_leanIndexPath, DeletionBenchmarkFixture.GetSurvivingIds(DocumentCount, 0));
            DeletionBenchmarkFixture.VerifyLuceneIds(_luceneIndexPath, DeletionBenchmarkFixture.GetSurvivingIds(DocumentCount, 0));
        }
        catch (Exception exception)
        {
            _proof.Fail(exception, DocumentCount, _deleteCount);
            DisposeWriters();
            BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
            BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
            throw;
        }

        _leanWriter?.Commit();
        _luceneWriter?.Commit();
        DisposeWriters();

        try
        {
            DeletionBenchmarkFixture.VerifyLeanIds(
                _leanIndexPath,
                DeletionBenchmarkFixture.GetSurvivingIds(DocumentCount, _leanDeletesQueued ? _deleteCount : 0));
            DeletionBenchmarkFixture.VerifyLuceneIds(
                _luceneIndexPath,
                DeletionBenchmarkFixture.GetSurvivingIds(DocumentCount, _luceneDeletesQueued ? _deleteCount : 0));
            _proof.Verified(_leanDeletesQueued, _luceneDeletesQueued);
        }
        catch (Exception exception)
        {
            _proof.Fail(exception, DocumentCount, _deleteCount);
            throw;
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
            BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
        }
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_QueueDeletes()
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

    private void DisposeWriters()
    {
        _leanWriter?.Dispose();
        _leanWriter = null;
        _luceneWriter?.Dispose();
        _luceneWriter = null;
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();
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
// Bounds, not a fixed count: a class-level IterationCount overrides the command line, so the
// reviewed qualification runs could not force the 30 samples their gate needs (ADR047).
[MinIterationCount(3)]
[MaxIterationCount(15)]
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

    private bool _leanCommitMeasured;
    private bool _luceneCommitMeasured;
    private LeanMMapDirectory? _leanPreDeleteDirectory;
    private LeanIndexSearcher? _leanPreDeleteSearcher;
    private LuceneMMapDirectory? _lucenePreDeleteDirectory;
    private Lucene.Net.Index.DirectoryReader? _lucenePreDeleteReader;
    private readonly DeletionSurvivorProof _proof = new(typeof(DeletionCommitBenchmarks), "deletion-commit-survivors",
        nameof(LeanCorpus_CommitDeletes), nameof(LuceneNet_CommitDeletes));

    [GlobalSetup]
    public void Setup()
    {
        _documents = BenchmarkData.BuildDocuments(DocumentCount);
        _deleteCount = Math.Max(1, DocumentCount / 10);
        DeletionBenchmarkFixture.RecordDurability("deletion-commit");
    }

    [GlobalCleanup]
    public void Cleanup() => _proof.Record(DocumentCount, _deleteCount);

    [IterationSetup]
    public void IterationSetup()
    {
        _leanCommitMeasured = false;
        _luceneCommitMeasured = false;
        _leanIndexPath = BenchmarkHelpers.CreateTempDirectory("leancorpus-bench-del-commit");
        _leanDirectory = new LeanMMapDirectory(_leanIndexPath);
        _leanWriter = new LeanIndexWriter(_leanDirectory, DeletionQueueBenchmarks.CreateLeanWriterConfig());
        DeletionBenchmarkFixture.AddLeanDocuments(_leanWriter, _documents);
        _leanWriter.Commit();
        _leanPreDeleteDirectory = new LeanMMapDirectory(_leanIndexPath);
        _leanPreDeleteSearcher = new LeanIndexSearcher(_leanPreDeleteDirectory);
        DeletionBenchmarkFixture.QueueLeanDeletes(_leanWriter, _deleteCount);

        _luceneIndexPath = BenchmarkHelpers.CreateTempDirectory("lucenenet-bench-del-commit");
        _luceneDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
        _luceneAnalyzer = new StandardAnalyzer(LuceneVersion.LUCENE_48);
        _luceneWriter = new Lucene.Net.Index.IndexWriter(
            _luceneDirectory,
            DeletionBenchmarkFixture.CreateLuceneWriterConfig(_luceneAnalyzer));
        DeletionBenchmarkFixture.AddLuceneDocuments(_luceneWriter, _documents);
        _luceneWriter.Commit();
        _lucenePreDeleteDirectory = new LuceneMMapDirectory(new DirectoryInfo(_luceneIndexPath));
        _lucenePreDeleteReader = Lucene.Net.Index.DirectoryReader.Open(_lucenePreDeleteDirectory);
        DeletionBenchmarkFixture.QueueLuceneDeletes(_luceneWriter, _deleteCount);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        try
        {
            // Verify at the timed commit: writers stay open and no second commit has run yet.
            DeletionBenchmarkFixture.VerifyLeanIds(_leanIndexPath,
                DeletionBenchmarkFixture.GetSurvivingIds(DocumentCount, _leanCommitMeasured ? _deleteCount : 0));
            DeletionBenchmarkFixture.VerifyLuceneIds(_luceneIndexPath,
                DeletionBenchmarkFixture.GetSurvivingIds(DocumentCount, _luceneCommitMeasured ? _deleteCount : 0));
            DeletionBenchmarkFixture.VerifyPointInTime(DocumentCount, _leanPreDeleteSearcher!, _lucenePreDeleteReader!);
        }
        catch (Exception exception)
        {
            _proof.Fail(exception, DocumentCount, _deleteCount);
            ReleaseAll();
            throw;
        }

        _leanPreDeleteSearcher?.Dispose();
        _leanPreDeleteDirectory?.Dispose();
        _lucenePreDeleteReader?.Dispose();
        _lucenePreDeleteDirectory?.Dispose();
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
            _proof.Verified(_leanCommitMeasured, _luceneCommitMeasured);
        }
        catch (Exception exception)
        {
            _proof.Fail(exception, DocumentCount, _deleteCount);
            throw;
        }
        finally
        {
            BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
            BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
        }
    }

    [Benchmark(Baseline = true)]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LeanCorpus_CommitDeletes()
    {
        _leanWriter!.Commit();
        _leanCommitMeasured = true;
        return _deleteCount;
    }

    [Benchmark]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public int LuceneNet_CommitDeletes()
    {
        _luceneWriter!.Commit();
        _luceneCommitMeasured = true;
        return _deleteCount;
    }

    private void ReleaseAll()
    {
        _leanPreDeleteSearcher?.Dispose();
        _leanPreDeleteDirectory?.Dispose();
        _lucenePreDeleteReader?.Dispose();
        _lucenePreDeleteDirectory?.Dispose();
        _leanWriter?.Dispose();
        _luceneWriter?.Dispose();
        _luceneAnalyzer?.Dispose();
        _luceneDirectory?.Dispose();
        BenchmarkHelpers.DeleteDirectory(_leanIndexPath);
        BenchmarkHelpers.DeleteDirectory(_luceneIndexPath);
    }
}

/// <summary>
/// Records the semantic proof for a deletion benchmark process (ADR047): every verified iteration
/// checked the exact sorted survivor IDs of both indexes after the timed operation. The proof names
/// only the method whose engine this process measured.
/// </summary>
internal sealed class DeletionSurvivorProof(Type benchmarkType, string proofId, string leanMethod, string luceneMethod)
{
    private int _leanVerifications;
    private int _luceneVerifications;
    private bool _recorded;

    public void Verified(bool leanMeasured, bool luceneMeasured)
    {
        if (leanMeasured)
            _leanVerifications++;
        if (luceneMeasured)
            _luceneVerifications++;
    }

    public void Fail(Exception exception, int documentCount, int deleteCount)
    {
        Write(leanMethod, documentCount, deleteCount, Math.Max(1, _leanVerifications), [string.Concat("SurvivorSetMismatch: ", exception.GetType().Name)]);
        Write(luceneMethod, documentCount, deleteCount, Math.Max(1, _luceneVerifications), [string.Concat("SurvivorSetMismatch: ", exception.GetType().Name)]);
        _recorded = true;
    }

    public void Record(int documentCount, int deleteCount)
    {
        if (_recorded)
            return;
        if (_leanVerifications > 0)
            Write(leanMethod, documentCount, deleteCount, _leanVerifications, []);
        if (_luceneVerifications > 0)
            Write(luceneMethod, documentCount, deleteCount, _luceneVerifications, []);
        _recorded = true;
    }

    private void Write(string method, int documentCount, int deleteCount, int verifications, IReadOnlyList<string> reasons)
        => BenchmarkSemanticProofs.Record(
            benchmarkType,
            [method],
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["DocumentCount"] = documentCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            proofId,
            "deletion-survivor-set-v1",
            BenchmarkSemanticProofs.SearchDatasetContentSha256(documentCount),
            passed: reasons.Count == 0,
            reasons,
            BenchmarkSemanticProofs.Digest(string.Join("\n", DeletionBenchmarkFixture.GetSurvivingIds(documentCount, deleteCount))),
            verifications);
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

    /// <summary>Fails unless readers opened before the deletes still see every original document.</summary>
    internal static void VerifyPointInTime(int documentCount, LeanIndexSearcher leanSearcher, Lucene.Net.Index.IndexReader luceneReader)
    {
        int leanLive = leanSearcher.Search(new LeanMatchAllDocsQuery(), 1).TotalHits;
        if (leanLive != documentCount || luceneReader.NumDocs != documentCount)
            throw new InvalidOperationException(
                $"Pre-delete readers must still see {documentCount} documents: LeanCorpus {leanLive}, Lucene.NET {luceneReader.NumDocs}.");
    }

    /// <summary>Records the durable-commit settings of both writers.</summary>
    internal static void RecordDurability(string fixture)
    {
        BenchmarkFixtureEvidence.RecordProfile(fixture, BenchmarkFixtureEvidence.LeanCorpusEngine, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["durableCommits"] = DeletionQueueBenchmarks.CreateLeanWriterConfig().DurableCommits.ToString(),
            ["mergeThreshold"] = "int.MaxValue"
        });
        BenchmarkFixtureEvidence.RecordProfile(fixture, BenchmarkFixtureEvidence.LuceneNetEngine, new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["durableCommits"] = "IndexWriter.Commit synchronises the commit files",
            ["mergePolicy"] = "NoMergePolicy.NO_COMPOUND_FILES"
        });
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
        var result = lease.Searcher.Search(new LeanMatchAllDocsQuery(), DocumentCount, CancellationToken.None);
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
