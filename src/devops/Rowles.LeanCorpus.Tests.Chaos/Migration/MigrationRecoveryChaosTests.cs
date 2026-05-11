using Rowles.LeanCorpus.Index.Migration;
using Rowles.LeanCorpus.Index.Compatibility;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Migration;

[Trait("Category", "Chaos")]
[Trait("Category", "Migration")]
public sealed class MigrationRecoveryChaosTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public MigrationRecoveryChaosTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Fact]
    public void RollBack_PreparedMarker_DeletesStagingAndClearsMarker()
    {
        var sourcePath = System.IO.Path.Combine(_fixture.Path, "rollback_source");
        var stagingPath = System.IO.Path.Combine(_fixture.Path, "rollback_staging");
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(stagingPath);
        File.WriteAllText(System.IO.Path.Combine(stagingPath, "leftover.tmp"), "pending migration data");
        IndexMigrationRecovery.WriteMarker(sourcePath, CreateMarker(IndexMigrationState.Prepared, sourcePath, stagingPath), durable: false);

        IndexMigrationRecovery.RollBack(sourcePath);

        Assert.False(Directory.Exists(stagingPath));
        Assert.False(File.Exists(System.IO.Path.Combine(sourcePath, IndexMigrationRecovery.MarkerFileName)));
        Assert.Equal(IndexMigrationState.None, IndexMigrationRecovery.GetState(sourcePath).State);
    }

    [Fact]
    public void GetState_CorruptMarker_ThrowsInvalidDataException()
    {
        var sourcePath = System.IO.Path.Combine(_fixture.Path, "corrupt_marker");
        Directory.CreateDirectory(sourcePath);
        File.WriteAllText(System.IO.Path.Combine(sourcePath, IndexMigrationRecovery.MarkerFileName), "{ not-json");

        Assert.Throws<InvalidDataException>(() => IndexMigrationRecovery.GetState(sourcePath));
    }

    [Fact]
    public void RollBack_InProgressMarkerWithStaging_DeletesStagingAndKeepsSourceReadable()
    {
        using var directory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "rollback_in_progress_source");
        var stagingPath = System.IO.Path.Combine(_fixture.Path, "rollback_in_progress_staging");
        CopyDirectoryFiles(directory.DirectoryPath, stagingPath);
        IndexMigrationRecovery.WriteMarker(directory.DirectoryPath, CreateMarker(IndexMigrationState.InProgress, directory.DirectoryPath, stagingPath), durable: false);

        IndexMigrationRecovery.RollBack(directory.DirectoryPath);

        Assert.False(Directory.Exists(stagingPath));
        Assert.False(File.Exists(System.IO.Path.Combine(directory.DirectoryPath, IndexMigrationRecovery.MarkerFileName)));
        Assert.Equal(IndexMigrationState.None, IndexMigrationRecovery.GetState(directory.DirectoryPath).State);
        AssertSourceReadable(directory.DirectoryPath);
    }

    [Fact]
    public void Abandon_FailedMarkerWithStaging_RemovesMarkerAndPreservesStaging()
    {
        using var directory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "abandon_failed_source");
        var stagingPath = System.IO.Path.Combine(_fixture.Path, "abandon_failed_staging");
        Directory.CreateDirectory(stagingPath);
        var sentinelPath = System.IO.Path.Combine(stagingPath, "leftover.tmp");
        File.WriteAllText(sentinelPath, "pending migration data");
        IndexMigrationRecovery.WriteMarker(directory.DirectoryPath, CreateMarker(IndexMigrationState.Failed, directory.DirectoryPath, stagingPath), durable: false);

        IndexMigrationRecovery.Abandon(directory.DirectoryPath);

        Assert.True(File.Exists(sentinelPath));
        Assert.False(File.Exists(System.IO.Path.Combine(directory.DirectoryPath, IndexMigrationRecovery.MarkerFileName)));
        Assert.Equal(IndexMigrationState.None, IndexMigrationRecovery.GetState(directory.DirectoryPath).State);
        AssertSourceReadable(directory.DirectoryPath);
    }

    [Fact]
    public void RollBack_ReadyToPublishMarker_ThrowsAndPreservesStaging()
    {
        using var directory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "rollback_ready_source");
        var stagingPath = System.IO.Path.Combine(_fixture.Path, "rollback_ready_staging");
        Directory.CreateDirectory(stagingPath);
        var sentinelPath = System.IO.Path.Combine(stagingPath, "ready.tmp");
        File.WriteAllText(sentinelPath, "ready migration data");
        IndexMigrationRecovery.WriteMarker(directory.DirectoryPath, CreateMarker(IndexMigrationState.ReadyToPublish, directory.DirectoryPath, stagingPath), durable: false);

        Assert.Throws<InvalidOperationException>(() => IndexMigrationRecovery.RollBack(directory.DirectoryPath));

        Assert.True(File.Exists(System.IO.Path.Combine(directory.DirectoryPath, IndexMigrationRecovery.MarkerFileName)));
        Assert.True(File.Exists(sentinelPath));
        Assert.NotEqual(IndexCompatibilityStatus.Compatible, IndexCompatibility.Check(directory).Status);
    }

    [Fact]
    public void Abandon_StaleMarkerWithMissingStaging_RemovesMarkerAndKeepsSourceReadable()
    {
        using var directory = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "abandon_stale_source");
        var stagingPath = System.IO.Path.Combine(_fixture.Path, "abandon_stale_missing");
        IndexMigrationRecovery.WriteMarker(directory.DirectoryPath, CreateMarker(IndexMigrationState.Failed, directory.DirectoryPath, stagingPath), durable: false);

        IndexMigrationRecovery.Abandon(directory.DirectoryPath);

        Assert.False(File.Exists(System.IO.Path.Combine(directory.DirectoryPath, IndexMigrationRecovery.MarkerFileName)));
        Assert.Equal(IndexMigrationState.None, IndexMigrationRecovery.GetState(directory.DirectoryPath).State);
        AssertSourceReadable(directory.DirectoryPath);
    }

    private static void CopyDirectoryFiles(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        foreach (var file in Directory.EnumerateFiles(sourcePath))
            File.Copy(file, System.IO.Path.Combine(targetPath, System.IO.Path.GetFileName(file)), overwrite: false);
    }

    private static void AssertSourceReadable(string sourcePath)
    {
        using var directory = new MMapDirectory(sourcePath);
        Assert.Equal(IndexCompatibilityStatus.Compatible, IndexCompatibility.Check(directory).Status);
        using var searcher = new IndexSearcher(directory);
        var results = searcher.Search(new TermQuery("body", "hello"), 10);
        Assert.True(results.TotalHits > 0);
    }

    private static IndexMigrationMarker CreateMarker(IndexMigrationState state, string sourcePath, string stagingPath)
    {
        var now = DateTimeOffset.UtcNow;
        return new IndexMigrationMarker
        {
            State = state,
            SourceDirectory = sourcePath,
            StagingDirectory = stagingPath,
            SourceCommitGeneration = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            PlannedActions = []
        };
    }
}
