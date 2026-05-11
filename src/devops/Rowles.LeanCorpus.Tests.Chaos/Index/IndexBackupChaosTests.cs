using System.Text.Json;
using Rowles.LeanCorpus.Index.Backup;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Serialization;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Index;

[Trait("Category", "Chaos")]
[Trait("Category", "Backup")]
public sealed class IndexBackupChaosTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public IndexBackupChaosTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void InterruptedBackup_PartialDirectoryWithoutOverwrite_Throws()
    {
        using var sourceDirectory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "backup_partial_reject");
        var backupPath = CreatePath("backup_partial_reject_target");
        var manifest = IndexBackup.CreateManifest(sourceDirectory.DirectoryPath);
        CopyOnlyFirstDataFile(sourceDirectory.DirectoryPath, backupPath, manifest);

        var exception = Assert.Throws<InvalidOperationException>(
            () => IndexBackup.Backup(sourceDirectory.DirectoryPath, backupPath));

        Assert.Contains("Backup directory", exception.Message, StringComparison.Ordinal);
        Assert.Contains("is not empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InterruptedBackup_PartialDirectoryWithOverwrite_RewritesBackup()
    {
        using var sourceDirectory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "backup_partial_overwrite");
        var backupPath = CreatePath("backup_partial_overwrite_target");
        var manifest = IndexBackup.CreateManifest(sourceDirectory.DirectoryPath);
        CopyOnlyFirstDataFile(sourceDirectory.DirectoryPath, backupPath, manifest);

        var result = IndexBackup.Backup(
            sourceDirectory.DirectoryPath,
            backupPath,
            new IndexBackupOptions { OverwriteBackupDirectory = true });

        var validatedManifest = IndexBackup.ValidateBackup(backupPath);
        Assert.Equal(result.Manifest.Files.Count, result.CopiedFiles.Count);
        Assert.Equal(result.Manifest.Files.Count, validatedManifest.Files.Count);
    }

    [Fact]
    public void InterruptedRestore_PartialTargetWithoutOverwrite_Throws()
    {
        using var sourceDirectory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "restore_partial_reject");
        var backupPath = CreatePath("restore_partial_reject_backup");
        var restorePath = CreatePath("restore_partial_reject_target");
        var backup = IndexBackup.Backup(sourceDirectory.DirectoryPath, backupPath);
        CopyFirstDataFile(backupPath, restorePath, backup.Manifest);

        var exception = Assert.Throws<InvalidOperationException>(
            () => IndexBackup.Restore(backupPath, restorePath));

        Assert.Contains("Restore target directory", exception.Message, StringComparison.Ordinal);
        Assert.Contains("is not empty", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void InterruptedRestore_PartialTargetWithOverwrite_RewritesAndSearches()
    {
        using var sourceDirectory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "restore_partial_overwrite");
        var backupPath = CreatePath("restore_partial_overwrite_backup");
        var restorePath = CreatePath("restore_partial_overwrite_target");
        var backup = IndexBackup.Backup(sourceDirectory.DirectoryPath, backupPath);
        CopyFirstDataFile(backupPath, restorePath, backup.Manifest);

        var result = IndexBackup.Restore(
            backupPath,
            restorePath,
            new IndexRestoreOptions { OverwriteTargetDirectory = true });

        Assert.NotNull(result.ValidationResult);
        Assert.True(result.ValidationResult.IsHealthy);
        AssertSearchable(restorePath);
    }

    [Fact]
    public void ManifestWithNoDataFiles_ValidateBackupAndRestore_ThrowMissingFile()
    {
        using var sourceDirectory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "backup_manifest_only");
        var backupPath = CreatePath("backup_manifest_only_source");
        var restorePath = CreatePath("backup_manifest_only_restore");
        var backup = IndexBackup.Backup(sourceDirectory.DirectoryPath, backupPath);
        var firstFileName = backup.Manifest.Files[0].FileName;
        foreach (var entry in backup.Manifest.Files)
            File.Delete(Path.Combine(backupPath, entry.FileName));

        var validateException = Assert.Throws<InvalidDataException>(
            () => IndexBackup.ValidateBackup(backupPath));
        var restoreException = Assert.Throws<InvalidDataException>(
            () => IndexBackup.Restore(backupPath, restorePath));

        Assert.Contains(firstFileName, validateException.Message, StringComparison.Ordinal);
        Assert.Contains("missing", validateException.Message, StringComparison.Ordinal);
        Assert.Contains(firstFileName, restoreException.Message, StringComparison.Ordinal);
        Assert.Contains("missing", restoreException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TruncatedFile_ValidateBackup_FailsLengthBeforeCrc()
    {
        using var sourceDirectory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "backup_truncated_file");
        var backupPath = CreatePath("backup_truncated_file_source");
        var backup = IndexBackup.Backup(sourceDirectory.DirectoryPath, backupPath);
        var entry = FirstDataFile(backup.Manifest);
        TruncateFile(Path.Combine(backupPath, entry.FileName), entry.Length - 1);

        var exception = Assert.Throws<InvalidDataException>(
            () => IndexBackup.ValidateBackup(backupPath));

        Assert.Contains(entry.FileName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("length", exception.Message, StringComparison.Ordinal);
        Assert.Contains("expected", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("CRC-32", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Restore_InvalidBackupWithOverwrite_DoesNotClearTarget()
    {
        using var sourceDirectory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "restore_invalid_preserve");
        var backupPath = CreatePath("restore_invalid_preserve_backup");
        var restorePath = CreatePath("restore_invalid_preserve_target");
        var backup = IndexBackup.Backup(sourceDirectory.DirectoryPath, backupPath);
        var entry = FirstDataFile(backup.Manifest);
        TruncateFile(Path.Combine(backupPath, entry.FileName), entry.Length - 1);
        Directory.CreateDirectory(restorePath);
        var sentinelPath = Path.Combine(restorePath, "sentinel.txt");
        File.WriteAllText(sentinelPath, "keep");

        Assert.Throws<InvalidDataException>(
            () => IndexBackup.Restore(
                backupPath,
                restorePath,
                new IndexRestoreOptions { OverwriteTargetDirectory = true }));

        Assert.True(File.Exists(sentinelPath));
        Assert.Equal("keep", File.ReadAllText(sentinelPath));
    }

    [Fact]
    public void Restore_ChecksumMismatch_DoesNotCreateTarget()
    {
        using var sourceDirectory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "restore_checksum_mismatch");
        var backupPath = CreatePath("restore_checksum_mismatch_backup");
        var restorePath = CreatePath("restore_checksum_mismatch_target");
        var backup = IndexBackup.Backup(sourceDirectory.DirectoryPath, backupPath);
        var entry = FirstDataFile(backup.Manifest);
        FlipByte(Path.Combine(backupPath, entry.FileName));

        var exception = Assert.Throws<InvalidDataException>(
            () => IndexBackup.Restore(backupPath, restorePath));

        Assert.Contains(entry.FileName, exception.Message, StringComparison.Ordinal);
        Assert.Contains("CRC-32", exception.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(restorePath));
    }

    private string CreatePath(string name)
        => Path.Combine(_fixture.Path, $"{name}_{Guid.NewGuid():N}");

    private static IndexBackupFileEntry FirstDataFile(IndexBackupManifest manifest)
        => manifest.Files
            .Where(static entry => !entry.IsCommitFile)
            .OrderBy(static entry => entry.FileName, StringComparer.Ordinal)
            .First();

    private static void CopyOnlyFirstDataFile(string sourceDirectory, string targetDirectory, IndexBackupManifest manifest)
    {
        Directory.CreateDirectory(targetDirectory);
        CopyFirstDataFile(sourceDirectory, targetDirectory, manifest);
        var manifestJson = JsonSerializer.Serialize(manifest, LeanCorpusJsonContext.Default.IndexBackupManifest);
        File.WriteAllText(Path.Combine(targetDirectory, IndexBackup.ManifestFileName), manifestJson);
    }

    private static void CopyFirstDataFile(string sourceDirectory, string targetDirectory, IndexBackupManifest manifest)
    {
        Directory.CreateDirectory(targetDirectory);
        var entry = FirstDataFile(manifest);
        File.Copy(Path.Combine(sourceDirectory, entry.FileName), Path.Combine(targetDirectory, entry.FileName));
    }

    private static void AssertSearchable(string indexPath)
    {
        using var directory = new MMapDirectory(indexPath);
        using var searcher = new IndexSearcher(directory);

        var results = searcher.Search(new TermQuery("body", "hello"), 10);

        Assert.True(results.TotalHits > 0);
    }

    private static void TruncateFile(string path, long length)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.SetLength(Math.Max(0, length));
    }

    private static void FlipByte(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var value = stream.ReadByte();
        Assert.NotEqual(-1, value);
        stream.Position = 0;
        stream.WriteByte((byte)(value ^ 0x5A));
    }
}
