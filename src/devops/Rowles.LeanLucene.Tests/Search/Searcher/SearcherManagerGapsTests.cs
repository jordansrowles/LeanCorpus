using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search.Searcher;

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
        try { Directory.Delete(_dir, true); } catch { }
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
}
