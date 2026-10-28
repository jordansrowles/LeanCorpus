using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using LeanStringField = Rowles.LeanCorpus.Document.Fields.StringField;
using LeanTextField = Rowles.LeanCorpus.Document.Fields.TextField;

namespace Rowles.LeanCorpus.Benchmark.Workloads;

internal sealed record WorkloadDocument(string Id, string Body);

internal interface IWorkloadEngine : IDisposable
{
    void Add(WorkloadDocument document);
    void Commit();
    void Refresh();
    string? Find(string id);
    string[] ReadIds(int maximum);
    int SegmentCount { get; }
}

internal abstract class SnapshotWorkloadEngine : IWorkloadEngine
{
    private readonly ReaderWriterLockSlim readers = new();
    public abstract void Add(WorkloadDocument document);
    public abstract void Commit();
    protected abstract void RefreshSnapshot();
    protected abstract string? FindSnapshot(string id);
    protected abstract string[] ReadSnapshotIds(int maximum);
    protected abstract void Close();
    public abstract int SegmentCount { get; }

    public void Refresh()
    {
        readers.EnterWriteLock();
        try { RefreshSnapshot(); }
        finally { readers.ExitWriteLock(); }
    }

    public string? Find(string id)
    {
        readers.EnterReadLock();
        try { return FindSnapshot(id); }
        finally { readers.ExitReadLock(); }
    }

    public string[] ReadIds(int maximum)
    {
        readers.EnterReadLock();
        try { return ReadSnapshotIds(maximum); }
        finally { readers.ExitReadLock(); }
    }

    public void Dispose()
    {
        readers.EnterWriteLock();
        try { Close(); }
        finally { readers.ExitWriteLock(); readers.Dispose(); }
    }
}

internal sealed class LeanWorkloadEngine : SnapshotWorkloadEngine
{
    private readonly string path;
    private readonly MMapDirectory directory;
    private readonly IndexWriter writer;
    private MMapDirectory? readDirectory;
    private IndexSearcher? searcher;

    public LeanWorkloadEngine(WorkloadRequest request)
    {
        path = request.IndexPath;
        directory = new MMapDirectory(path);
        try
        {
            writer = new IndexWriter(directory, new IndexWriterConfig
            {
                DefaultAnalyser = new WhitespaceAnalyser(),
                DurableCommits = true, RamBufferSizeMB = 16, MaxBufferedDocs = 1024,
                IndexingConcurrency = request.Concurrency,
                MergeThreshold = int.MaxValue, MergePolicy = NoMergePolicy.Instance
            });
        }
        catch { directory.Dispose(); throw; }
    }

    public override void Add(WorkloadDocument document)
    {
        var value = new LeanDocument();
        value.Add(new LeanStringField("id", document.Id, stored: true));
        value.Add(new LeanTextField("body", document.Body, stored: true));
        writer.AddDocument(value);
    }

    public override void Commit() => writer.Commit();
    protected override void RefreshSnapshot()
    {
        searcher?.Dispose();
        readDirectory?.Dispose();
        searcher = null;
        readDirectory = new MMapDirectory(path);
        searcher = new IndexSearcher(readDirectory, new IndexSearcherConfig { EnableQueryCache = false, MaxConcurrency = 1 });
    }
    protected override string? FindSnapshot(string id)
    {
        var hits = searcher!.Search(new TermQuery("id", id), 2, CancellationToken.None);
        return hits.TotalHits == 1 ? StoredId(hits.ScoreDocs[0].DocId) : null;
    }
    protected override string[] ReadSnapshotIds(int maximum)
        => searcher!.Search(new MatchAllDocsQuery(), maximum + 1, CancellationToken.None).ScoreDocs.Select(hit => StoredId(hit.DocId)).ToArray();
    private string StoredId(int documentId)
    {
        var fields = searcher!.GetStoredFields(documentId);
        return fields.TryGetValue("id", out var values) && values.Count == 1
            ? values[0] : throw new InvalidDataException("A stored logical ID is missing or ambiguous.");
    }
    public override int SegmentCount => Directory.GetFiles(path, "seg_*.seg").Length;
    protected override void Close()
    {
        try { searcher?.Dispose(); }
        finally
        {
            try { readDirectory?.Dispose(); }
            finally { try { writer.Dispose(); } finally { directory.Dispose(); } }
        }
    }
}

internal sealed class LuceneWorkloadEngine : SnapshotWorkloadEngine
{
    private readonly Lucene.Net.Store.MMapDirectory directory;
    private readonly Lucene.Net.Analysis.Core.WhitespaceAnalyzer analyser = new(Lucene.Net.Util.LuceneVersion.LUCENE_48);
    private readonly Lucene.Net.Index.IndexWriter writer;
    private Lucene.Net.Index.DirectoryReader? reader;
    private Lucene.Net.Search.IndexSearcher? searcher;

    public LuceneWorkloadEngine(WorkloadRequest request)
    {
        directory = new Lucene.Net.Store.MMapDirectory(new DirectoryInfo(request.IndexPath));
        try
        {
            writer = new Lucene.Net.Index.IndexWriter(directory,
                new Lucene.Net.Index.IndexWriterConfig(Lucene.Net.Util.LuceneVersion.LUCENE_48, analyser)
                {
                    RAMBufferSizeMB = 16,
                    MergePolicy = Lucene.Net.Index.NoMergePolicy.NO_COMPOUND_FILES
                });
        }
        catch { analyser.Dispose(); directory.Dispose(); throw; }
    }
    public override void Add(WorkloadDocument document)
    {
        var value = new Lucene.Net.Documents.Document
        {
            new Lucene.Net.Documents.StringField("id", document.Id, Lucene.Net.Documents.Field.Store.YES),
            new Lucene.Net.Documents.TextField("body", document.Body, Lucene.Net.Documents.Field.Store.YES)
        };
        writer.AddDocument(value);
    }
    public override void Commit() => writer.Commit();
    protected override void RefreshSnapshot()
    {
        reader?.Dispose();
        reader = Lucene.Net.Index.DirectoryReader.Open(directory);
        searcher = new Lucene.Net.Search.IndexSearcher(reader);
    }
    protected override string? FindSnapshot(string id)
    {
        var hits = searcher!.Search(new Lucene.Net.Search.TermQuery(new Lucene.Net.Index.Term("id", id)), 2);
        return hits.TotalHits == 1 ? searcher.Doc(hits.ScoreDocs[0].Doc).Get("id") : null;
    }
    protected override string[] ReadSnapshotIds(int maximum)
        => searcher!.Search(new Lucene.Net.Search.MatchAllDocsQuery(), maximum + 1).ScoreDocs
            .Select(hit => searcher.Doc(hit.Doc).Get("id") ?? throw new InvalidDataException("Missing Lucene logical ID.")).ToArray();
    public override int SegmentCount => reader?.Leaves.Count ?? 0;
    protected override void Close()
    {
        try { reader?.Dispose(); }
        finally
        {
            try { writer.Dispose(); }
            finally { analyser.Dispose(); directory.Dispose(); }
        }
    }
}
