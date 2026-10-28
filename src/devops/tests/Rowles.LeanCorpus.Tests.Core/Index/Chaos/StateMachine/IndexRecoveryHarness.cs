using Rowles.LeanCorpus.Tests.Core.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed class IndexRecoveryHarness : IDisposable
{
    private readonly StateMachineTestDirectory _testDirectory = new();
    private readonly string _indexPath;
    private MMapDirectory? _writerDirectory;
    private IndexWriter? _writer;
    private bool _disposed;

    public IndexRecoveryHarness()
    {
        _indexPath = _testDirectory.CreateChildPath("index");
        OpenWriter();
        _writer!.Commit();
    }

    public void Add(ModelDocument document) => Writer.AddDocument(document.ToLeanDocument());

    public void AddBatch(IReadOnlyList<ModelDocument> documents) =>
        Writer.AddDocuments(documents.Select(static document => document.ToLeanDocument()).ToArray());

    public void Delete(string id) => Writer.DeleteDocuments(new TermQuery("id", id));

    public void Update(ModelDocument replacement) =>
        Writer.UpdateDocument("id", replacement.Id, replacement.ToLeanDocument());

    public void Commit() => Writer.Commit();

    public void ReopenWriter()
    {
        CloseWriter();
        OpenWriter();
    }

    public IndexRecovery.RecoveryResult InspectRecovery() =>
        IndexRecovery.RecoverLatestCommit(_indexPath, cleanupOrphans: false)
        ?? throw new InvalidDataException("The state-machine index unexpectedly has no commit.");

    public void PrepareCommitAndReopen()
    {
        Writer.PrepareCommit();
        CloseWriter();
        OpenWriter();
    }

    public IndexRecovery.RecoveryResult CorruptLatestCommitAndReopen()
    {
        CloseWriter();
        var latest = InspectRecovery();
        File.WriteAllText(latest.CommitFilePath, "{corrupt commit");
        var recovery = IndexRecovery.RecoverLatestCommit(_indexPath, cleanupOrphans: true)
            ?? throw new InvalidDataException("Recovery unexpectedly returned no fallback commit.");
        OpenWriter();
        return recovery;
    }

    public IndexRecovery.RecoveryResult DeleteLatestCommitAndReopen()
    {
        CloseWriter();
        var latest = InspectRecovery();
        File.Delete(latest.CommitFilePath);
        var recovery = IndexRecovery.RecoverLatestCommit(_indexPath, cleanupOrphans: true)
            ?? throw new InvalidDataException("Recovery unexpectedly returned no fallback commit.");
        OpenWriter();
        return recovery;
    }

    public void WriteTemporaryFilesAndReopen()
    {
        CloseWriter();
        File.WriteAllText(Path.Combine(_indexPath, "segments_999.tmp"), "partial commit");
        File.WriteAllText(Path.Combine(_indexPath, "data.tmp"), "unrelated temporary data");
        OpenWriter();

        Assert.False(File.Exists(Path.Combine(_indexPath, "segments_999.tmp")));
        Assert.True(File.Exists(Path.Combine(_indexPath, "data.tmp")));
    }

    public void WriteOrphanFilesAndReopen()
    {
        CloseWriter();
        const string orphanId = "orphan_999";
        File.WriteAllText(Path.Combine(_indexPath, orphanId + ".seg"), "orphan");
        File.WriteAllText(Path.Combine(_indexPath, orphanId + ".dic"), "orphan");
        File.WriteAllText(Path.Combine(_indexPath, orphanId + ".pos"), "orphan");
        OpenWriter();

        Assert.False(File.Exists(Path.Combine(_indexPath, orphanId + ".seg")));
        Assert.False(File.Exists(Path.Combine(_indexPath, orphanId + ".dic")));
        Assert.False(File.Exists(Path.Combine(_indexPath, orphanId + ".pos")));
    }

    public void AssertSearch(SearchSpec search, IReadOnlyDictionary<string, ModelDocument> expectedDocuments)
    {
        using var directory = new MMapDirectory(_indexPath);
        using var searcher = new IndexSearcher(directory);
        string[] expectedIds = expectedDocuments.Values
            .Where(search.Matches)
            .Select(static document => document.Id)
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        var results = searcher.Search(search.ToQuery(), Math.Max(1, expectedIds.Length + 1));
        Assert.Equal(expectedIds.Length, results.TotalHits);
        string[] actualIds = results.ScoreDocs
            .Select(scoreDocument => ReadStoredValue(searcher.GetStoredFields(scoreDocument.DocId), "id"))
            .OrderBy(static id => id, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedIds, actualIds);
    }

    public void AssertCommitted(IReadOnlyDictionary<string, ModelDocument> expected) =>
        AssertSearch(new SearchSpec(SearchKind.MatchAll), expected);

    public void AssertAllCommitsInvalid()
    {
        foreach (var path in Directory.GetFiles(_indexPath, "segments_*")
                     .Where(static path =>
                     {
                         string name = Path.GetFileName(path);
                         return !name.EndsWith(".pending", StringComparison.Ordinal)
                             && !name.EndsWith(".tmp", StringComparison.Ordinal);
                     }))
        {
            File.WriteAllText(path, "corrupt commit");
        }

        Assert.Throws<InvalidDataException>(() =>
            IndexRecovery.RecoverLatestCommit(_indexPath, cleanupOrphans: false));
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        CloseWriter();
        _testDirectory.Dispose();
    }

    private IndexWriter Writer => _writer ?? throw new ObjectDisposedException(nameof(IndexRecoveryHarness));

    private void OpenWriter()
    {
        _writerDirectory = new MMapDirectory(_indexPath);
        _writer = new IndexWriter(_writerDirectory, CreateWriterConfig());
    }

    private void CloseWriter()
    {
        _writer?.Dispose();
        _writer = null;
        _writerDirectory?.Dispose();
        _writerDirectory = null;
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
        DurableCommits = true,
        DeletionPolicy = new KeepLastNCommitsPolicy(64),
        MergePolicy = NoMergePolicy.Instance
    };
}
