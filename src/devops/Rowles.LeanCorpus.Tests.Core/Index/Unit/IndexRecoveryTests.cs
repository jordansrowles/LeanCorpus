using System.Text.Json;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Unit tests for <see cref="IndexRecovery"/> decision logic. Commit files are hand-crafted
/// with a valid CRC trailer; no index is built, so segment references are intentionally missing
/// where the test only exercises commit selection, promotion, or rejection.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Index)]
public sealed class IndexRecoveryTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexRecoveryTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name) => Path.Combine(_fixture.Path, $"{name}_{Guid.NewGuid():N}");

    private static void WriteCommit(
        string directory,
        string fileName,
        int generation,
        string[] segments,
        long contentToken = 0)
    {
        var json = JsonSerializer.Serialize(
            new { Segments = segments, Generation = generation, ContentToken = contentToken });
        File.WriteAllText(Path.Combine(directory, fileName), CommitFileFormat.Wrap(json));
    }

    [Fact]
    public void RecoverLatestCommit_NonExistentDirectory_ReturnsNull()
    {
        var dir = SubDir("does-not-exist");

        var result = IndexRecovery.RecoverLatestCommit(dir, cleanupOrphans: false);

        Assert.Null(result);
    }

    [Fact]
    public void RecoverLatestCommit_AllCommitsInvalid_ThrowsInvalidData()
    {
        var dir = SubDir("all-invalid");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "segments_1"), "not a commit");

        var exception = Assert.Throws<InvalidDataException>(
            () => IndexRecovery.RecoverLatestCommit(dir, cleanupOrphans: false));

        Assert.Contains("corrupt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoverLatestCommit_CommitReferencesMissingSegment_ThrowsInvalidData()
    {
        var dir = SubDir("missing-segment");
        Directory.CreateDirectory(dir);
        WriteCommit(dir, "segments_1", 1, ["seg_missing"]);

        var exception = Assert.Throws<InvalidDataException>(
            () => IndexRecovery.RecoverLatestCommit(dir, cleanupOrphans: false));

        Assert.Contains("corrupt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RecoverLatestCommit_CommitGenerationMismatch_ThrowsInvalidData()
    {
        var dir = SubDir("generation-mismatch");
        Directory.CreateDirectory(dir);
        // File is named segments_2 but records Generation = 1.
        WriteCommit(dir, "segments_2", 1, ["seg_missing"]);

        var exception = Assert.Throws<InvalidDataException>(
            () => IndexRecovery.RecoverLatestCommit(dir, cleanupOrphans: false));

        Assert.Contains("corrupt", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PromotePendingCommits_OrphanedPending_IsPromotedToCommit()
    {
        var dir = SubDir("promote-pending");
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(
            new { Segments = new[] { "seg_missing" }, Generation = 1, ContentToken = 0L });
        File.WriteAllText(Path.Combine(dir, "segments_1.pending"), CommitFileFormat.Wrap(json));

        // Recovery still fails (the promoted commit references a missing segment), but the
        // orphaned pending file must have been promoted to a full commit first.
        Assert.Throws<InvalidDataException>(
            () => IndexRecovery.RecoverLatestCommit(dir, cleanupOrphans: true));

        Assert.False(File.Exists(Path.Combine(dir, "segments_1.pending")));
        Assert.True(File.Exists(Path.Combine(dir, "segments_1")));
    }

    [Fact]
    public void PromotePendingCommits_StalePending_WhenFinalExists_IsDeleted()
    {
        var dir = SubDir("stale-pending");
        Directory.CreateDirectory(dir);
        WriteCommit(dir, "segments_1", 1, ["seg_missing"]);
        File.WriteAllText(Path.Combine(dir, "segments_1.pending"), "stale pending");

        Assert.Throws<InvalidDataException>(
            () => IndexRecovery.RecoverLatestCommit(dir, cleanupOrphans: true));

        Assert.True(File.Exists(Path.Combine(dir, "segments_1")));
        Assert.False(File.Exists(Path.Combine(dir, "segments_1.pending")));
    }

    [Fact]
    public void RecoverLatestCommit_CleanupOrphansFalse_DoesNotMutateDirectory()
    {
        var dir = SubDir("no-mutate");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "segments_1"), "not a commit");
        File.WriteAllText(Path.Combine(dir, "segments_9.tmp"), "temp");
        File.WriteAllText(Path.Combine(dir, "segments_5.pending"), "pending");
        File.WriteAllText(Path.Combine(dir, "orphan_99.seg"), "fake");
        File.WriteAllText(Path.Combine(dir, "orphan_99.dic"), "fake");

        Assert.Throws<InvalidDataException>(
            () => IndexRecovery.RecoverLatestCommit(dir, cleanupOrphans: false));

        Assert.True(File.Exists(Path.Combine(dir, "segments_9.tmp")));
        Assert.True(File.Exists(Path.Combine(dir, "segments_5.pending")));
        Assert.True(File.Exists(Path.Combine(dir, "orphan_99.seg")));
        Assert.True(File.Exists(Path.Combine(dir, "orphan_99.dic")));
    }
}
