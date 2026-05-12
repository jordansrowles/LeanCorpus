using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.Search;

/// <summary>
/// Chaos tests for <see cref="SearcherManager"/> that deliberately corrupt commit files
/// to exercise the refresh-failure recording paths.
/// </summary>
[Trait("Category", "Chaos")]
[Trait("Category", "Search")]
public sealed class SearcherManagerChaosTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public SearcherManagerChaosTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "SearcherManager Chaos: corrupt commit file causes RecordRefreshFailure")]
    public void SearcherManager_CorruptCommitFile_RecordsRefreshFailure()
    {
        using var dir = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "smgr_chaos_record");
        using var mgr = new SearcherManager(dir);

        CorruptAllCommitFiles(dir.DirectoryPath);
        mgr.MaybeRefresh();

        Assert.NotNull(mgr.LastRefreshError);
        Assert.NotNull(mgr.LastRefreshErrorAt);
        Assert.True(mgr.ConsecutiveRefreshFailures > 0);
    }

    [Fact(DisplayName = "SearcherManager Chaos: corrupt commit file fires RefreshFailed event")]
    public void SearcherManager_CorruptCommitFile_RefreshFailedEventFires()
    {
        using var dir = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "smgr_chaos_event");
        using var mgr = new SearcherManager(dir);
        var firedCount = 0;
        Exception? capturedEx = null;
        mgr.RefreshFailed += (_, args) =>
        {
            Interlocked.Increment(ref firedCount);
            capturedEx = args.Error;
        };

        CorruptAllCommitFiles(dir.DirectoryPath);
        mgr.MaybeRefresh();

        Assert.True(firedCount >= 1, $"Expected RefreshFailed to fire at least once, got {firedCount}");
        Assert.NotNull(capturedEx);
    }

    [Fact(DisplayName = "SearcherManager Chaos: subscriber exception does not propagate from MaybeRefresh")]
    public void SearcherManager_CorruptCommitFile_SubscriberThrows_NoExceptionPropagates()
    {
        using var dir = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "smgr_chaos_sub_throw");
        using var mgr = new SearcherManager(dir);
        mgr.RefreshFailed += (_, _) => throw new InvalidOperationException("chaos subscriber");

        CorruptAllCommitFiles(dir.DirectoryPath);
        var ex = Record.Exception(() => mgr.MaybeRefresh());

        Assert.Null(ex);
    }

    [Fact(DisplayName = "SearcherManager Chaos: consecutive failure counter increments on repeated corrupt refreshes")]
    public void SearcherManager_CorruptCommitFile_CountsConsecutiveFailures()
    {
        using var dir = ChaosIndexFactory.CreateSimpleIndex(_fixture.Path, "smgr_chaos_count");
        using var mgr = new SearcherManager(dir);

        CorruptAllCommitFiles(dir.DirectoryPath);
        mgr.MaybeRefresh();
        long after1 = mgr.ConsecutiveRefreshFailures;
        mgr.MaybeRefresh();
        long after2 = mgr.ConsecutiveRefreshFailures;

        Assert.True(after1 > 0);
        Assert.True(after2 > after1, $"Expected counter to grow: after1={after1}, after2={after2}");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void CorruptAllCommitFiles(string directoryPath)
    {
        foreach (var file in Directory.GetFiles(directoryPath, "segments_*"))
        {
            if (file.EndsWith(".tmp", StringComparison.Ordinal))
                continue;
            File.WriteAllBytes(file, [0xDE, 0xAD, 0xBE, 0xEF]);
        }
    }
}
