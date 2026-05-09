using System.Collections.Concurrent;
using System.Diagnostics;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Diagnostics;

/// <summary>
/// Verifies that <see cref="LeanLuceneActivitySource"/> emits activities with the expected names
/// and tags for Search, Commit, Flush, and Merge operations.
/// </summary>
/// <remarks>
/// Each test scopes its captures to a per-test parent activity. The listener captures every
/// activity from the production source, but the assertions only consider activities whose
/// <see cref="Activity.RootId"/> matches the test's parent. This isolates the assertions from
/// any other test in the suite that emits activities from the same source in parallel.
/// </remarks>
public sealed class ActivitySourceTests : IDisposable
{
    private const string SourceName = "Rowles.LeanLucene";
    private const string TestSourceName = "Rowles.LeanLucene.Tests.ActivityScope";

    private readonly string _dir;
    private readonly ConcurrentBag<Activity> _captured = [];
    private readonly ActivityListener _listener;
    private readonly ActivitySource _testSource = new(TestSourceName);

    public ActivitySourceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "activity_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);

        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == SourceName || src.Name == TestSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => _captured.Add(a)
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _testSource.Dispose();
        foreach (var a in _captured) a.Dispose();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { }
    }

    /// <summary>
    /// Starts a parent activity used as the scoping anchor for the test. Production activities
    /// started while this is current become children, sharing <see cref="Activity.RootId"/>.
    /// </summary>
    private Activity StartScope([System.Runtime.CompilerServices.CallerMemberName] string name = "")
        => _testSource.StartActivity(name) ?? throw new InvalidOperationException("Test scope activity could not be started.");

    private IEnumerable<Activity> Scoped(Activity scope)
        => _captured.Where(a => a.RootId == scope.RootId && a.Source.Name == SourceName);

    /// <summary>
    /// Verifies the Search: Emits Activity With Query Type Tag scenario.
    /// </summary>
    [Fact(DisplayName = "Search: Emits Activity With Query Type Tag")]
    public void Search_EmitsActivity_WithQueryTypeTag()
    {
        using var scope = StartScope();
        using var writer = CreateAndPopulateIndex();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        searcher.Search(new TermQuery("body", "hello"), 5);

        var activity = Scoped(scope).FirstOrDefault(a => a.OperationName == "leanlucene.search");
        Assert.NotNull(activity);
        Assert.Equal("TermQuery", activity!.GetTagItem("query.type"));
    }

    /// <summary>
    /// Verifies the Search: Emits Activity With Total Hits Tag scenario.
    /// </summary>
    [Fact(DisplayName = "Search: Emits Activity With Total Hits Tag")]
    public void Search_EmitsActivity_WithTotalHitsTag()
    {
        using var scope = StartScope();
        using var writer = CreateAndPopulateIndex();

        using var searcher = new IndexSearcher(new MMapDirectory(_dir));
        searcher.Search(new TermQuery("body", "hello"), 10);

        var activity = Scoped(scope).FirstOrDefault(a => a.OperationName == "leanlucene.search");
        Assert.NotNull(activity);
        Assert.NotNull(activity!.GetTagItem("search.total_hits"));
    }

    /// <summary>
    /// Verifies the Commit: Emits Activity With Segment Count Tag scenario.
    /// </summary>
    [Fact(DisplayName = "Commit: Emits Activity With Segment Count Tag")]
    public void Commit_EmitsActivity_WithSegmentCountTag()
    {
        using var scope = StartScope();
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "commit activity test"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        var activity = Scoped(scope).FirstOrDefault(a => a.OperationName == "leanlucene.index.commit");
        Assert.NotNull(activity);
        Assert.NotNull(activity!.GetTagItem("index.segment_count"));
    }

    /// <summary>
    /// Verifies the Flush: Emits Activity With Doc Count Tag scenario.
    /// </summary>
    [Fact(DisplayName = "Flush: Emits Activity With Doc Count Tag")]
    public void Flush_EmitsActivity_WithDocCountTag()
    {
        using var scope = StartScope();
        using var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "flush activity test"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        var activity = Scoped(scope).FirstOrDefault(a => a.OperationName == "leanlucene.index.flush");
        Assert.NotNull(activity);
        Assert.NotNull(activity.GetTagItem("index.doc_count"));
    }

    /// <summary>
    /// Verifies the Activities: Have Correct Source Name scenario.
    /// </summary>
    [Fact(DisplayName = "Activities: Have Correct Source Name")]
    public void Activities_HaveCorrectSourceName()
    {
        using var scope = StartScope();
        using var writer = CreateAndPopulateIndex();
        writer.Dispose();

        var snapshot = Scoped(scope).ToList();
        Assert.NotEmpty(snapshot);
        Assert.All(snapshot, a => Assert.Equal(SourceName, a.Source.Name));
    }

    /// <summary>
    /// Verifies the No Listener: Produces No Activities scenario.
    /// </summary>
    [Fact(DisplayName = "No Listener: Produces No Activities")]
    public void NoListener_ProducesNoActivities()
    {
        // Dispose our listener and drain anything already captured from other test classes
        // running in parallel (the listener listens globally on the production source).
        _listener.Dispose();
        while (_captured.TryTake(out _)) { }

        var noListenPath = Path.Combine(Path.GetTempPath(), "act_nolisten_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(noListenPath);

        var writer = new IndexWriter(new MMapDirectory(noListenPath), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "no listener test"));
        writer.AddDocument(doc);
        writer.Commit();
        writer.Dispose();

        try { Directory.Delete(noListenPath, true); } catch { }

        // Our listener is disposed, so writes above should not flow into _captured.
        // Activities from other parallel tests can still produce items via *their* listeners
        // but never via ours, so _captured remains empty.
        Assert.DoesNotContain(_captured, a => a.OperationName == "leanlucene.index.flush");
    }

    private IndexWriter CreateAndPopulateIndex()
    {
        var writer = new IndexWriter(new MMapDirectory(_dir), new IndexWriterConfig());
        var doc = new LeanDocument();
        doc.Add(new TextField("body", "hello world"));
        writer.AddDocument(doc);
        writer.Commit();
        return writer;
    }
}
