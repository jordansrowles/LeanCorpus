using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Search.Searcher;

/// <summary>
/// Gap-coverage tests for <see cref="SearcherManager"/> targeting branches not
/// exercised by the existing SearcherLeaseTests: UsingSearcher exception safety,
/// MaybeRefreshAsync cancellation, and disposed-manager guards.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class SearcherManagerGapsTests : IDisposable
{
    private readonly string _dir;

    public SearcherManagerGapsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_smgr_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    private MMapDirectory BuildIndex(params string[] bodies)
    {
        var mmap = new MMapDirectory(_dir);
        using var writer = new IndexWriter(mmap, new IndexWriterConfig());
        foreach (var body in bodies)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", body));
            writer.AddDocument(doc);
        }
        writer.Commit();
        return mmap;
    }

    // ── UsingSearcher exception safety ────────────────────────────────────────

    [Fact(DisplayName = "SearcherManager: UsingSearcher Releases On Exception")]
    public void UsingSearcher_ReleasesOnException()
    {
        using var dir = BuildIndex("hello world");
        using var mgr = new SearcherManager(dir);

        Assert.Throws<InvalidOperationException>(() =>
            mgr.UsingSearcher<int>(_ => throw new InvalidOperationException("test")));

        // Manager still usable after the exception.
        using var lease = mgr.AcquireLease();
        Assert.NotNull(lease.Searcher);
    }

    // ── MaybeRefreshAsync cancellation ────────────────────────────────────────

    [Fact(DisplayName = "SearcherManager: MaybeRefreshAsync With Pre-Cancelled Token Throws")]
    public async Task MaybeRefreshAsync_PreCancelledToken_ThrowsOperationCancelled()
    {
        using var dir = BuildIndex("doc one");
        using var mgr = new SearcherManager(dir);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => mgr.MaybeRefreshAsync(cts.Token));
    }

    // ── Disposed manager guards ───────────────────────────────────────────────

    [Fact(DisplayName = "SearcherManager: AcquireLease After Dispose Throws ObjectDisposed")]
    public void AcquireLease_AfterDispose_Throws()
    {
        var dir = BuildIndex("doc");
        var mgr = new SearcherManager(dir);
        mgr.Dispose();
        Assert.Throws<ObjectDisposedException>(() => mgr.AcquireLease());
    }

    [Fact(DisplayName = "SearcherManager: MaybeRefresh After Dispose Throws ObjectDisposed")]
    public void MaybeRefresh_AfterDispose_Throws()
    {
        var dir = BuildIndex("doc");
        var mgr = new SearcherManager(dir);
        mgr.Dispose();
        Assert.Throws<ObjectDisposedException>(() => mgr.MaybeRefresh());
    }

    // ── Acquire / Release ─────────────────────────────────────────────────────

    [Fact(DisplayName = "SearcherManager: Acquire And Release Works")]
    public void Acquire_And_Release_Works()
    {
        using var dir = BuildIndex("doc");
        using var mgr = new SearcherManager(dir);
        var searcher = mgr.Acquire();
        Assert.NotNull(searcher);
        mgr.Release(searcher);
    }

    [Fact(DisplayName = "SearcherManager: Release Unknown Searcher Is No-Op")]
    public void Release_UnknownSearcher_IsNoOp()
    {
        using var dir = BuildIndex("doc");
        using var mgr = new SearcherManager(dir);

        // A searcher not managed by this manager must not throw.
        var unknown = new IndexSearcher(dir);
        mgr.Release(unknown);
    }

    // ── LastRefreshError / LastRefreshErrorAt / ConsecutiveRefreshFailures ────

    [Fact(DisplayName = "SearcherManager: LastRefreshError is initially null")]
    public void LastRefreshError_InitiallyNull()
    {
        using var dir = BuildIndex("smgr_err_init");
        using var mgr = new SearcherManager(dir);

        Assert.Null(mgr.LastRefreshError);
    }

    [Fact(DisplayName = "SearcherManager: LastRefreshErrorAt is initially null")]
    public void LastRefreshErrorAt_InitiallyNull()
    {
        using var dir = BuildIndex("smgr_errtime_init");
        using var mgr = new SearcherManager(dir);

        Assert.Null(mgr.LastRefreshErrorAt);
    }

    [Fact(DisplayName = "SearcherManager: ConsecutiveRefreshFailures is initially zero")]
    public void ConsecutiveRefreshFailures_InitiallyZero()
    {
        using var dir = BuildIndex("smgr_crf_init");
        using var mgr = new SearcherManager(dir);

        Assert.Equal(0, mgr.ConsecutiveRefreshFailures);
    }

    // ── Refresh failure recording ─────────────────────────────────────────────

    [Fact(DisplayName = "SearcherManager: LastRefreshError is non-null after corrupt commit")]
    public void LastRefreshError_AfterCorruptCommit_IsNonNull()
    {
        using var dir = BuildIndex("smgr_err_corrupt");
        using var mgr = new SearcherManager(dir);

        CorruptAllCommitFiles(dir.DirectoryPath);
        mgr.MaybeRefresh();

        Assert.NotNull(mgr.LastRefreshError);
    }

    [Fact(DisplayName = "SearcherManager: LastRefreshErrorAt is non-null after corrupt commit")]
    public void LastRefreshErrorAt_AfterCorruptCommit_IsNonNull()
    {
        using var dir = BuildIndex("smgr_errtime_corrupt");
        using var mgr = new SearcherManager(dir);

        CorruptAllCommitFiles(dir.DirectoryPath);
        mgr.MaybeRefresh();

        Assert.NotNull(mgr.LastRefreshErrorAt);
    }

    [Fact(DisplayName = "SearcherManager: ConsecutiveRefreshFailures increments after corrupt commit")]
    public void ConsecutiveRefreshFailures_AfterCorruptCommit_IsPositive()
    {
        using var dir = BuildIndex("smgr_crf_corrupt");
        using var mgr = new SearcherManager(dir);

        CorruptAllCommitFiles(dir.DirectoryPath);
        mgr.MaybeRefresh();

        Assert.True(mgr.ConsecutiveRefreshFailures > 0);
    }

    [Fact(DisplayName = "SearcherManager: RefreshFailed event fires when commit file is corrupt")]
    public void RefreshFailed_EventFires_WhenCommitCorrupted()
    {
        using var dir = BuildIndex("smgr_event_corrupt");
        using var mgr = new SearcherManager(dir);
        var fired = false;
        mgr.RefreshFailed += (_, _) => fired = true;

        CorruptAllCommitFiles(dir.DirectoryPath);
        mgr.MaybeRefresh();

        Assert.True(fired);
    }

    [Fact(DisplayName = "SearcherManager: Subscriber exception in RefreshFailed does not propagate")]
    public void RefreshFailed_SubscriberThrows_NoExceptionPropagates()
    {
        using var dir = BuildIndex("smgr_sub_throw");
        using var mgr = new SearcherManager(dir);
        mgr.RefreshFailed += (_, _) => throw new InvalidOperationException("subscriber boom");

        CorruptAllCommitFiles(dir.DirectoryPath);

        // Must not throw even though subscriber throws.
        var ex = Record.Exception(() => mgr.MaybeRefresh());
        Assert.Null(ex);
    }

    // ── Recovery after corruption ─────────────────────────────────────────────

    [Fact(DisplayName = "SearcherManager: ConsecutiveRefreshFailures resets after successful refresh")]
    public void ConsecutiveRefreshFailures_ResetsAfterRecovery()
    {
        using var dir = BuildIndex("smgr_crf_reset");
        using var mgr = new SearcherManager(dir);

        // Capture the original commit bytes so we can restore them.
        var commitPath  = Directory.GetFiles(dir.DirectoryPath, "segments_*")
            .Single(f => !f.EndsWith(".tmp", StringComparison.Ordinal));
        var original    = File.ReadAllBytes(commitPath);

        CorruptAllCommitFiles(dir.DirectoryPath);
        mgr.MaybeRefresh(); // produces a failure → counter goes to 1
        Assert.True(mgr.ConsecutiveRefreshFailures > 0);

        // Restore the original commit and write a second commit so
        // TryRefreshCore sees a newer generation and succeeds.
        File.WriteAllBytes(commitPath, original);
        AddCommit(dir);

        mgr.MaybeRefresh(); // succeeds → counter resets to 0
        Assert.Equal(0, mgr.ConsecutiveRefreshFailures);
    }
    // ── Exception handling: transient caught, fatal propagated ─────────────────

    [Fact(DisplayName = "SearcherManager: Non-transient exception propagates through MaybeRefresh")]
    public void MaybeRefresh_NonTransientException_Propagates()
    {
        using var dir = BuildIndex("smgr_fatal_prop");
        using var mgr = new SearcherManager(dir);

        // Null out _directory via reflection so TryRefreshCore throws NullReferenceException,
        // which is not in the transient-exception when filter and must propagate.
        var directoryField = typeof(SearcherManager).GetField("_directory",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.NotNull(directoryField);
        directoryField.SetValue(mgr, null);

        Assert.Throws<NullReferenceException>(() => mgr.MaybeRefresh());
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

    private void AddCommit(MMapDirectory dir)
    {
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "refresh recovery"));
        writer.AddDocument(doc);
        writer.Commit();
    }
}
