using System.Text.Json;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Unit tests for partial-failure rollback during <see cref="IndexBackup.Restore"/>.
/// Backups are hand-crafted and restored with <see cref="IndexRestoreOptions.ValidateAfterRestore"/> disabled,
/// so no real index structure is required.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class IndexBackupPartialRestoreTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexBackupPartialRestoreTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name) => Path.Combine(_fixture.Path, $"{name}_{Guid.NewGuid():N}");

    private static IndexBackupManifest CreateManifest(params (string FileName, byte[] Content)[] files)
    {
        var entries = new List<IndexBackupFileEntry>(files.Length);
        foreach (var (name, content) in files)
        {
            entries.Add(new IndexBackupFileEntry
            {
                FileName = name,
                Length = content.Length,
                Crc32 = Crc32.Compute(content),
                Role = "sidecar",
                IsRequired = false,
                IsCommitFile = false,
                PresentInBackup = true
            });
        }

        return new IndexBackupManifest
        {
            FormatVersion = IndexBackup.CurrentManifestFormatVersion,
            Kind = IndexBackupKind.Full,
            ChainDepth = 1,
            CommitGeneration = 1,
            ContentToken = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CommitFileName = "segments_1",
            Files = entries
        };
    }

    private static void WriteBackup(
        string directory,
        IndexBackupManifest manifest,
        params (string FileName, byte[] Content)[] files)
    {
        Directory.CreateDirectory(directory);
        foreach (var (name, content) in files)
            File.WriteAllBytes(Path.Combine(directory, name), content);

        var json = JsonSerializer.Serialize(manifest, LeanCorpusJsonContext.Default.IndexBackupManifest);
        File.WriteAllText(Path.Combine(directory, IndexBackup.ManifestFileName), json);
    }

    [Fact]
    public void Restore_MidCopyChecksumFailure_CleansUpStagingAndLeavesTargetUntouched()
    {
        var root = SubDir("midcopy-failure");
        var backupDir = Path.Combine(root, "backup");
        var manifest = CreateManifest(
            ("data1.bin", new byte[] { 1, 2, 3, 4 }),
            ("data2.bin", new byte[] { 5, 6, 7, 8 }));
        WriteBackup(backupDir, manifest,
            ("data1.bin", new byte[] { 1, 2, 3, 4 }),
            ("data2.bin", new byte[] { 5, 6, 7, 8 }));

        // Corrupt the second file in place (same length) so the checksum failure
        // occurs after data1.bin has already been copied into the staging directory.
        FlipFirstByte(Path.Combine(backupDir, "data2.bin"));

        var targetDir = Path.Combine(root, "target");

        Assert.Throws<InvalidDataException>(
            () => IndexBackup.Restore(backupDir, targetDir, new IndexRestoreOptions { ValidateAfterRestore = false }));

        Assert.False(Directory.Exists(targetDir));
        Assert.Empty(Directory.GetDirectories(root, "*.restore.*.tmp"));
    }

    [Fact]
    public void Restore_CommitStatsSkipped_WhenRestoreCommitStatsFalse()
    {
        var root = SubDir("commit-stats-skipped");
        var backupDir = Path.Combine(root, "backup");
        var targetDir = Path.Combine(root, "target");
        var data = new byte[] { 1, 2, 3, 4 };
        var stats = new byte[] { 7, 8, 9 };
        var manifest = new IndexBackupManifest
        {
            FormatVersion = IndexBackup.CurrentManifestFormatVersion,
            Kind = IndexBackupKind.Full,
            ChainDepth = 1,
            CommitGeneration = 1,
            ContentToken = 0,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            CommitFileName = "segments_1",
            Files =
            [
                new IndexBackupFileEntry
                {
                    FileName = "data.bin",
                    Length = data.Length,
                    Crc32 = Crc32.Compute(data),
                    Role = "sidecar",
                    IsRequired = true,
                    IsCommitFile = false,
                    PresentInBackup = true
                },
                new IndexBackupFileEntry
                {
                    FileName = "stats_1.json",
                    Length = stats.Length,
                    Crc32 = Crc32.Compute(stats),
                    Role = "commit-stats",
                    IsRequired = false,
                    IsCommitFile = false,
                    PresentInBackup = true
                }
            ]
        };
        WriteBackup(backupDir, manifest,
            ("data.bin", data),
            ("stats_1.json", stats));

        IndexBackup.Restore(backupDir, targetDir, new IndexRestoreOptions
        {
            ValidateAfterRestore = false,
            RestoreCommitStats = false
        });

        Assert.True(File.Exists(Path.Combine(targetDir, "data.bin")));
        Assert.False(File.Exists(Path.Combine(targetDir, "stats_1.json")));
    }

    private static void FlipFirstByte(string path)
    {
        using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var value = stream.ReadByte();
        stream.Position = 0;
        stream.WriteByte((byte)(value ^ 0xFF));
    }
}
