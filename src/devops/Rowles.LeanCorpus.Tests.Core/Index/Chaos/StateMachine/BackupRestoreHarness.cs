using System.Globalization;
using Rowles.LeanCorpus.Tests.Core.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Core.Index.Chaos.StateMachine;

internal sealed class BackupRestoreHarness : IDisposable
{
    private readonly StateMachineTestDirectory _testDirectory = new();
    private readonly string _sourcePath;
    private readonly string _backupRoot;
    private readonly string _restoreRoot;
    private readonly MMapDirectory _writerDirectory;
    private readonly IndexWriter _writer;
    private bool _disposed;

    public BackupRestoreHarness()
    {
        _sourcePath = _testDirectory.CreateChildPath("source");
        _backupRoot = _testDirectory.CreateChildPath("backups");
        _restoreRoot = _testDirectory.CreateChildPath("restores");
        _writerDirectory = new MMapDirectory(_sourcePath);
        _writer = new IndexWriter(_writerDirectory, CreateWriterConfig());
        _writer.Commit();
    }

    public void Add(ModelDocument document) => _writer.AddDocument(document.ToLeanDocument());

    public void AddBatch(IReadOnlyList<ModelDocument> documents) =>
        _writer.AddDocuments(documents.Select(static document => document.ToLeanDocument()).ToArray());

    public void Delete(string id) => _writer.DeleteDocuments(new TermQuery("id", id));

    public void Update(ModelDocument replacement) =>
        _writer.UpdateDocument("id", replacement.Id, replacement.ToLeanDocument());

    public void Commit() => _writer.Commit();

    public IndexBackupResult CreateFullBackup(int backupId, int generation)
    {
        string backupPath = GetBackupPath(backupId);
        return IndexBackup.Backup(_sourcePath, backupPath, new IndexBackupOptions
        {
            CommitGeneration = generation,
            IncludeCommitStats = true
        });
    }

    public IndexBackupResult CreateIncrementalBackup(int backupId, int parentId, int generation)
    {
        string backupPath = GetBackupPath(backupId);
        return IndexBackup.Backup(_sourcePath, backupPath, new IndexBackupOptions
        {
            CommitGeneration = generation,
            IncludeCommitStats = true,
            PreviousBackupDirectoryPath = GetBackupPath(parentId)
        });
    }

    public IndexBackupManifest ValidateBackup(BackupArtifactModel artifact)
    {
        var paths = artifact.ChainIds.Select(GetBackupPath).ToArray();
        return paths.Length == 1
            ? IndexBackup.ValidateBackup(paths[0])
            : IndexBackup.ValidateBackup(paths);
    }

    public void ValidateStandaloneIncremental(BackupArtifactModel artifact)
    {
        Assert.True(artifact.IsIncremental);
        string backupPath = GetBackupPath(artifact.Id);
        var manifest = IndexBackup.ReadManifest(backupPath);
        Assert.Equal(IndexBackupKind.Incremental, manifest.Kind);
        if (artifact.RequiresParent)
            Assert.Contains(manifest.Files, static file => !file.PresentInBackup);
        Assert.Throws<InvalidDataException>(() => IndexBackup.ValidateBackup(backupPath));
    }

    public IndexRestoreResult Restore(BackupArtifactModel artifact, int restoreId)
    {
        string targetPath = GetRestorePath(restoreId);
        var paths = artifact.ChainIds.Select(GetBackupPath).ToArray();
        return paths.Length == 1
            ? IndexBackup.Restore(paths[0], targetPath)
            : IndexBackup.Restore(paths, targetPath);
    }

    public void CorruptAndAssertRestoreFails(BackupArtifactModel artifact, int restoreId, bool removeFile)
    {
        string? filePath = FindCorruptibleFile(artifact);
        Assert.NotNull(filePath);
        if (removeFile)
            File.Delete(filePath!);
        else
            File.AppendAllText(filePath!, "corruption");

        string targetPath = GetRestorePath(restoreId);
        Directory.CreateDirectory(targetPath);
        string sentinelPath = Path.Combine(targetPath, "existing-target.txt");
        File.WriteAllText(sentinelPath, "preserve this target");

        var paths = artifact.ChainIds.Select(GetBackupPath).ToArray();
        Assert.Throws<InvalidDataException>(() =>
        {
            if (paths.Length == 1)
                IndexBackup.Restore(paths[0], targetPath, new IndexRestoreOptions { OverwriteTargetDirectory = true });
            else
                IndexBackup.Restore(paths, targetPath, new IndexRestoreOptions { OverwriteTargetDirectory = true });
        });

        Assert.True(File.Exists(sentinelPath));
        Assert.Equal("preserve this target", File.ReadAllText(sentinelPath));
        Assert.Empty(Directory.GetFiles(targetPath, "segments_*"));
        Assert.Empty(Directory.GetDirectories(_restoreRoot,
            Path.GetFileName(targetPath) + ".restore.*.tmp"));
    }

    public void AssertSourceSearch(SearchSpec search, IReadOnlyDictionary<string, ModelDocument> expectedDocuments) =>
        AssertSearchAtPath(_sourcePath, search, expectedDocuments);

    public void AssertRestored(IndexRestoreResult result, CommitSnapshot expected)
    {
        Assert.Equal(expected.Generation, result.Manifest.CommitGeneration);
        Assert.Equal(expected.ContentToken, result.Manifest.ContentToken);
        Assert.NotNull(result.ValidationResult);
        Assert.True(result.ValidationResult!.IsHealthy);

        foreach (var search in SearchSpec.Cases)
            AssertSearchAtPath(result.TargetDirectoryPath, search, expected.Documents);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _writer.Dispose();
        _writerDirectory.Dispose();
        _testDirectory.Dispose();
    }

    private string GetBackupPath(int backupId) =>
        Path.Combine(_backupRoot, $"backup-{backupId.ToString(CultureInfo.InvariantCulture)}");

    private string GetRestorePath(int restoreId) =>
        Path.Combine(_restoreRoot, $"restore-{restoreId.ToString(CultureInfo.InvariantCulture)}");

    private string? FindCorruptibleFile(BackupArtifactModel artifact)
    {
        foreach (int backupId in artifact.ChainIds.Reverse())
        {
            string backupPath = GetBackupPath(backupId);
            var manifest = IndexBackup.ReadManifest(backupPath);
            var entry = manifest.Files.FirstOrDefault(file =>
                file.PresentInBackup && !file.IsCommitFile && file.Length > 0);
            if (entry is not null)
                return Path.Combine(backupPath, entry.FileName);
        }

        return null;
    }

    private static void AssertSearchAtPath(
        string indexPath,
        SearchSpec search,
        IReadOnlyDictionary<string, ModelDocument> expectedDocuments)
    {
        using var directory = new MMapDirectory(indexPath);
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
