using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

[Trait("Category", "Index")]
[Trait("Category", "Backup")]
public sealed class IndexBackupTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexBackupTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "IndexBackup: Manifest Includes Commit And Segment Files")]
    public void IndexBackup_CreateManifest_IncludesCommitAndSegmentFiles()
    {
        var indexPath = CreateIndex("manifest_scope");

        var manifest = IndexBackup.CreateManifest(indexPath);

        Assert.Equal(1, manifest.CommitGeneration);
        Assert.Contains(manifest.Files, file => file.FileName == "segments_1" && file.IsCommitFile);
        Assert.Contains(manifest.Files, file => file.FileName.EndsWith(".seg", StringComparison.Ordinal) && file.IsRequired);
        Assert.Contains(manifest.Files, file => file.FileName.EndsWith(".dic", StringComparison.Ordinal) && file.IsRequired);
        Assert.Contains(manifest.Files, file => file.FileName.EndsWith(".pos", StringComparison.Ordinal) && file.IsRequired);
        Assert.Contains(manifest.Files, file => file.FileName.EndsWith(".fdt", StringComparison.Ordinal) && file.IsRequired);
        Assert.Contains(manifest.Files, file => file.FileName.EndsWith(".fdx", StringComparison.Ordinal) && file.IsRequired);
        Assert.Contains(manifest.Files, file => file.FileName.EndsWith(".stats.json", StringComparison.Ordinal) && file.Role == "segment-stats");
        Assert.Contains(manifest.Files, file => file.FileName == "stats_1.json" && file.Role == "commit-stats");
        Assert.Equal(manifest.Files.Select(file => file.FileName).OrderBy(file => file, StringComparer.Ordinal), manifest.Files.Select(file => file.FileName));
        Assert.All(manifest.Files, file =>
        {
            Assert.True(file.Length > 0);
            Assert.NotEqual(0u, file.Crc32);
        });
    }

    [Fact(DisplayName = "IndexBackup: Explicit Commit Generation Selects Old Commit")]
    public void IndexBackup_ExplicitCommitGeneration_SelectsOldCommit()
    {
        var indexPath = CreateIndexWithTwoCommits("explicit_generation");

        var manifest = IndexBackup.CreateManifest(indexPath, new IndexBackupOptions { CommitGeneration = 1 });

        Assert.Equal(1, manifest.CommitGeneration);
        Assert.Equal("segments_1", manifest.CommitFileName);
        Assert.DoesNotContain(manifest.Files, file => file.FileName == "segments_2");
    }

    [Fact(DisplayName = "IndexBackup: Restore Recreates Searchable Index")]
    public void IndexBackup_Restore_RecreatesSearchableIndex()
    {
        var indexPath = CreateIndex("restore_round_trip");
        var backupPath = Path.Combine(_fixture.Path, "restore_round_trip_backup");
        var restorePath = Path.Combine(_fixture.Path, "restore_round_trip_target");

        var backup = IndexBackup.Backup(indexPath, backupPath);
        var restore = IndexBackup.Restore(backupPath, restorePath);

        Assert.Equal(backup.Manifest.CommitGeneration, restore.Manifest.CommitGeneration);
        Assert.NotNull(restore.ValidationResult);
        Assert.True(restore.ValidationResult.IsHealthy);
        using var directory = new MMapDirectory(restorePath);
        using var searcher = new IndexSearcher(directory);
        Assert.Equal(2, searcher.Search(new TermQuery("body", "backup"), 10).TotalHits);
    }

    [Fact(DisplayName = "IndexBackup: Validate Backup Rejects Checksum Mismatch")]
    public void IndexBackup_ValidateBackup_RejectsChecksumMismatch()
    {
        var indexPath = CreateIndex("checksum_mismatch");
        var backupPath = Path.Combine(_fixture.Path, "checksum_mismatch_backup");
        var backup = IndexBackup.Backup(indexPath, backupPath);
        var fileToCorrupt = backup.Manifest.Files.First(file => !file.IsCommitFile);

        File.AppendAllText(Path.Combine(backupPath, fileToCorrupt.FileName), "corruption");

        Assert.Throws<InvalidDataException>(() => IndexBackup.ValidateBackup(backupPath));
    }

    [Fact(DisplayName = "IndexBackup: Restore Rejects Unsafe Manifest File Name")]
    public void IndexBackup_Restore_RejectsUnsafeManifestFileName()
    {
        var indexPath = CreateIndex("unsafe_manifest");
        var backupPath = Path.Combine(_fixture.Path, "unsafe_manifest_backup");
        var restorePath = Path.Combine(_fixture.Path, "unsafe_manifest_restore");
        var backup = IndexBackup.Backup(indexPath, backupPath);
        var firstFileName = backup.Manifest.Files[0].FileName;
        var manifestPath = Path.Combine(backupPath, IndexBackup.ManifestFileName);
        var json = File.ReadAllText(manifestPath)
            .Replace($"\"FileName\":\"{firstFileName}\"", $"\"FileName\":\"..\\\\{firstFileName}\"", StringComparison.Ordinal);
        File.WriteAllText(manifestPath, json);

        Assert.Throws<InvalidDataException>(() => IndexBackup.Restore(backupPath, restorePath));
    }

    [Fact(DisplayName = "IndexWriter: Backup Snapshot Uses Snapshot Commit")]
    public void IndexWriter_BackupSnapshot_UsesSnapshotCommit()
    {
        var indexPath = CreateIndexWithTwoCommits("snapshot_backup", out var snapshot, out var writer);
        var backupPath = Path.Combine(_fixture.Path, "snapshot_backup_output");

        try
        {
            var result = writer.BackupSnapshot(snapshot, backupPath);

            Assert.Equal(snapshot.CommitGeneration, result.Manifest.CommitGeneration);
            Assert.Equal("segments_1", result.Manifest.CommitFileName);
            Assert.DoesNotContain(result.Manifest.Files, file => file.FileName == "segments_2");
        }
        finally
        {
            writer.ReleaseSnapshot(snapshot);
            writer.Dispose();
        }
    }

    private string CreateIndex(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { DeletionPolicy = new KeepLastNCommitsPolicy(2) });
        writer.AddDocument(CreateDocument("first backup document", 10));
        writer.AddDocument(CreateDocument("second backup document", 20));
        writer.Commit();
        return path;
    }

    private string CreateIndexWithTwoCommits(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        using var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { DeletionPolicy = new KeepLastNCommitsPolicy(2) });
        writer.AddDocument(CreateDocument("first generation", 1));
        writer.Commit();
        writer.AddDocument(CreateDocument("second generation", 2));
        writer.Commit();
        return path;
    }

    private string CreateIndexWithTwoCommits(string name, out IndexSnapshot snapshot, out IndexWriter writer)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        var directory = new MMapDirectory(path);
        writer = new IndexWriter(directory, new IndexWriterConfig { DeletionPolicy = new KeepLatestCommitPolicy() });
        writer.AddDocument(CreateDocument("first snapshot generation", 1));
        writer.Commit();
        snapshot = writer.CreateSnapshot();
        writer.AddDocument(CreateDocument("second snapshot generation", 2));
        writer.Commit();
        return path;
    }

    private static LeanDocument CreateDocument(string body, double number)
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", body));
        document.Add(new StringField("category", "backup", stored: true));
        document.Add(new NumericField("number", number, stored: true));
        document.Add(new VectorField("embedding", new ReadOnlyMemory<float>([1f, 0f, 0f, 0f])));
        return document;
    }
}
