using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Unit tests for <see cref="SearcherLease"/> and <see cref="RefreshFailedEventArgs"/>.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class SearcherLeaseTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _root;

    public SearcherLeaseTests(TestDirectoryFixture fixture) => _root = fixture.Path;

    private string SubDir(string name)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static MMapDirectory BuildIndex(string dir, params string[] bodies)
    {
        var mmap = new MMapDirectory(dir);
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

    // ── SearcherLease ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Acquire Lease: Returns Searcher scenario.
    /// </summary>
    [Fact(DisplayName = "Acquire Lease: Returns Searcher")]
    public void AcquireLease_ReturnsSearcher()
    {
        var dir = BuildIndex(SubDir(nameof(AcquireLease_ReturnsSearcher)), "hello world");
        using var mgr = new SearcherManager(dir);

        using var lease = mgr.AcquireLease();
        Assert.NotNull(lease.Searcher);
    }

    /// <summary>
    /// Verifies the Acquire Lease: Searcher Is Usable For Search scenario.
    /// </summary>
    [Fact(DisplayName = "Acquire Lease: Searcher Is Usable For Search")]
    public void AcquireLease_SearcherIsUsableForSearch()
    {
        var dir = BuildIndex(SubDir(nameof(AcquireLease_SearcherIsUsableForSearch)), "hello world");
        using var mgr = new SearcherManager(dir);

        using var lease = mgr.AcquireLease();
        var results = lease.Searcher.Search(new TermQuery("body", "hello"), 10);
        Assert.Equal(1, results.TotalHits);
    }

    /// <summary>
    /// Verifies the Acquire Lease: Dispose Releases Reference scenario.
    /// </summary>
    [Fact(DisplayName = "Acquire Lease: Dispose Releases Reference")]
    public void AcquireLease_Dispose_ReleasesReference()
    {
        var dir = BuildIndex(SubDir(nameof(AcquireLease_Dispose_ReleasesReference)), "test");
        using var mgr = new SearcherManager(dir);

        // Acquire and immediately dispose; no exception should surface.
        var lease = mgr.AcquireLease();
        lease.Dispose();
    }

    /// <summary>
    /// Verifies the Acquire Lease: Dispose Is Idempotent scenario.
    /// </summary>
    [Fact(DisplayName = "Acquire Lease: Dispose Is Idempotent")]
    public void AcquireLease_Dispose_IsIdempotent()
    {
        var dir = BuildIndex(SubDir(nameof(AcquireLease_Dispose_IsIdempotent)), "test");
        using var mgr = new SearcherManager(dir);

        var lease = mgr.AcquireLease();

        var exception = Record.Exception(() =>
        {
            lease.Dispose();
            lease.Dispose();
        });

        Assert.Null(exception);
    }

    /// <summary>
    /// Verifies the Acquire Lease: Multiple Concurrent Leases Are Supported scenario.
    /// </summary>
    [Fact(DisplayName = "Acquire Lease: Multiple Concurrent Leases Are Supported")]
    public void AcquireLease_MultipleConcurrentLeases_AreSupported()
    {
        var dir = BuildIndex(SubDir(nameof(AcquireLease_MultipleConcurrentLeases_AreSupported)), "test");
        using var mgr = new SearcherManager(dir);

        using var lease1 = mgr.AcquireLease();
        using var lease2 = mgr.AcquireLease();
        using var lease3 = mgr.AcquireLease();

        Assert.NotNull(lease1.Searcher);
        Assert.NotNull(lease2.Searcher);
        Assert.NotNull(lease3.Searcher);
    }

    // ── RefreshFailedEventArgs ────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Refresh Failed Event Args: Constructor Sets Error scenario.
    /// </summary>
    [Fact(DisplayName = "Refresh Failed Event Args: Constructor Sets Error")]
    public void RefreshFailedEventArgs_Constructor_SetsError()
    {
        var err = new InvalidOperationException("boom");
        var args = new RefreshFailedEventArgs(err, 1);

        Assert.Same(err, args.Error);
    }

    /// <summary>
    /// Verifies the Refresh Failed Event Args: Constructor Sets Consecutive Failures scenario.
    /// </summary>
    [Fact(DisplayName = "Refresh Failed Event Args: Constructor Sets Consecutive Failures")]
    public void RefreshFailedEventArgs_Constructor_SetsConsecutiveFailures()
    {
        var args = new RefreshFailedEventArgs(new Exception("x"), 7);
        Assert.Equal(7L, args.ConsecutiveFailures);
    }

    /// <summary>
    /// Verifies the Refresh Failed Event Args: At Is Near Current Utc scenario.
    /// </summary>
    [Fact(DisplayName = "Refresh Failed Event Args: At Is Near Current Utc")]
    public void RefreshFailedEventArgs_At_IsNearCurrentUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var args = new RefreshFailedEventArgs(new Exception("x"), 1);
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.InRange(args.At, before, after);
    }

    /// <summary>
    /// Verifies the Refresh Failed Event Args: Is Event Args scenario.
    /// </summary>
    [Fact(DisplayName = "Refresh Failed Event Args: Is Event Args")]
    public void RefreshFailedEventArgs_IsEventArgs()
    {
        var args = new RefreshFailedEventArgs(new Exception(), 0);
        Assert.IsAssignableFrom<EventArgs>(args);
    }
}
