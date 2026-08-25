using Rowles.LeanCorpus.Tests.Core.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed class IndexReaderLifecycleHarness : IDisposable
{
    private readonly StateMachineTestDirectory _testDirectory = new();
    private readonly string _indexPath;
    private readonly MMapDirectory _writerDirectory;
    private readonly IndexWriter _writer;
    private readonly MMapDirectory _managerDirectory;
    private readonly SearcherManager _manager;
    private readonly Dictionary<int, SearcherLease> _leases = [];
    private bool _disposed;

    public IndexReaderLifecycleHarness()
    {
        _indexPath = _testDirectory.CreateChildPath("index");
        _writerDirectory = new MMapDirectory(_indexPath);
        _writer = new IndexWriter(_writerDirectory, CreateWriterConfig());
        _writer.Commit();

        _managerDirectory = new MMapDirectory(_indexPath);
        _manager = new SearcherManager(_managerDirectory, new SearcherManagerConfig
        {
            RefreshInterval = TimeSpan.FromHours(1)
        });
        PrimeCurrentReader();
    }

    public void Add(ModelDocument document) => _writer.AddDocument(document.ToLeanDocument());

    public void AddBatch(IReadOnlyList<ModelDocument> documents) =>
        _writer.AddDocuments(documents.Select(static document => document.ToLeanDocument()).ToArray());

    public void Delete(string id) => _writer.DeleteDocuments(new TermQuery("id", id));

    public void Update(ModelDocument replacement) =>
        _writer.UpdateDocument("id", replacement.Id, replacement.ToLeanDocument());

    public void Commit() => _writer.Commit();

    public void Refresh()
    {
        _manager.MaybeRefresh();
        PrimeCurrentReader();
    }

    public void Acquire(int leaseId)
    {
        Assert.False(_leases.ContainsKey(leaseId), $"Lease {leaseId} is already active.");
        var lease = _manager.AcquireLease();
        _leases.Add(leaseId, lease);
    }

    public void Release(int leaseId)
    {
        Assert.True(_leases.Remove(leaseId, out var lease), $"Lease {leaseId} was not active.");
        lease.Dispose();
    }

    public void AssertLease(int leaseId, ReaderLeaseModel expected)
    {
        Assert.True(_leases.TryGetValue(leaseId, out var lease), $"Lease {leaseId} was not active.");
        Assert.Equal(expected.Generation, lease.CommitGeneration);

        string[] expectedIds = expected.Documents.Values
            .Where(static document => true)
            .Select(static document => document.Id)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        AssertSearch(lease.Searcher, new MatchAllDocsQuery(), expectedIds);
    }

    public void AssertSearch(int leaseId, SearchSpec search, IReadOnlyDictionary<string, ModelDocument> expectedDocuments)
    {
        Assert.True(_leases.TryGetValue(leaseId, out var lease), $"Lease {leaseId} was not active.");
        string[] expectedIds = expectedDocuments.Values
            .Where(search.Matches)
            .Select(static document => document.Id)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        AssertSearch(lease.Searcher, search.ToQuery(), expectedIds);
    }

    public void AssertCurrentGeneration(int expectedGeneration)
    {
        using var lease = _manager.AcquireLease();
        Assert.Equal(expectedGeneration, lease.CommitGeneration);
        _ = lease.Searcher.Search(new MatchAllDocsQuery(), 1);
    }

    public void AssertDiagnostics(IndexReaderLifecycleModel expected)
    {
        var diagnostics = _manager.GetDiagnostics();
        // ReaderManagerDiagnostics subtracts the manager's ownership reference
        // from every reader, including retired readers. That means each retired
        // reader group is reported one lease short while it is still retained.
        int expectedActiveLeases = expected.Leases
            .Values
            .GroupBy(static lease => lease.ReaderVersion)
            .Sum(group => group.Key == expected.ManagerReaderVersion
                ? group.Count()
                : Math.Max(0, group.Count() - 1));
        Assert.Equal(expectedActiveLeases, diagnostics.ActiveLeases);
        Assert.True(diagnostics.ActiveReaders >= 1);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var lease in _leases.Values)
            lease.Dispose();
        _leases.Clear();
        _manager.Dispose();
        _managerDirectory.Dispose();
        _writer.Dispose();
        _writerDirectory.Dispose();
        _testDirectory.Dispose();
    }

    private static void AssertSearch(IndexSearcher searcher, Query query, IReadOnlyList<string> expectedIds)
    {
        var results = searcher.Search(query, Math.Max(1, expectedIds.Count + 1));
        Assert.Equal(expectedIds.Count, results.TotalHits);

        string[] actualIds = results.ScoreDocs
            .Select(scoreDocument => ReadStoredValue(searcher.GetStoredFields(scoreDocument.DocId), "id"))
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedIds, actualIds);
    }

    private void PrimeCurrentReader()
    {
        using var lease = _manager.AcquireLease();
        _ = lease.Searcher.Search(new MatchAllDocsQuery(), 1);
    }

    private static string ReadStoredValue(
        IReadOnlyDictionary<string, IReadOnlyList<string>> stored,
        string field)
    {
        Assert.True(stored.TryGetValue(field, out var values), $"Stored field '{field}' was not found.");
        Assert.NotNull(values);
        Assert.NotEmpty(values!);
        return values![0];
    }

    private static IndexWriterConfig CreateWriterConfig() => new()
    {
        MaxBufferedDocs = 3,
        MergePolicy = NoMergePolicy.Instance
    };
}
