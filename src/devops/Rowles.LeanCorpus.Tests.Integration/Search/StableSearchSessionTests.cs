using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Ranking;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

public sealed class StableSearchSessionTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"lc_session_{Guid.NewGuid():N}");
    public StableSearchSessionTests() { Directory.CreateDirectory(_path); Seed(12); }
    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_path);

    [Theory]
    [MemberData(nameof(Sorts))]
    public void PagesEqualOneShotForEverySupportedSort(SortField[] sorts)
    {
        using var directory = new MMapDirectory(_path);
        using var searchers = new SearcherManager(directory, new SearcherManagerConfig { SearcherConfig = new IndexSearcherConfig { ParallelSearch = true } });
        using var sessions = new SearchSessionManager(searchers);
        using var session = sessions.OpenSession();
        var query = new TermQuery("body", "common");
        using var lease = searchers.AcquireLease();
        var expected = lease.Searcher.Search(query, 12, sorts).ScoreDocs.Select(static hit => hit.DocId).ToArray();
        Assert.Equal(expected, ReadAll(session, query, 3, sorts));
    }

    [Fact]
    public void SessionRemainsStableAcrossCommitDeleteAndRefresh()
    {
        using var directory = new MMapDirectory(_path);
        using var searchers = new SearcherManager(directory);
        using var sessions = new SearchSessionManager(searchers);
        using var session = sessions.OpenSession();
        var query = new TermQuery("body", "common");
        var first = session.Search(query, 4);

        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            writer.DeleteDocuments(new TermQuery("id", "doc-00"));
            Add(writer, 99); writer.Commit();
        }
        Assert.True(searchers.MaybeRefresh());
        var oldIds = first.Results.ScoreDocs.Select(static d => d.DocId)
            .Concat(ReadAll(session, query, 4, [SortField.Score], first.NextCursor)).ToArray();
        Assert.Equal(Enumerable.Range(0, 12), oldIds);
        using var fresh = sessions.OpenSession();
        Assert.Equal(12, ReadAll(fresh, query, 20, [SortField.Score]).Length);
    }

    [Fact]
    public void CursorIsBoundToQuerySortSessionAndRankingIdentity()
    {
        using var directory = new MMapDirectory(_path); using var searchers = new SearcherManager(directory); using var sessions = new SearchSessionManager(searchers);
        using var first = sessions.OpenSession(); using var second = sessions.OpenSession();
        var page = first.Search(new TermQuery("body", "common"), 2, sorts: [SortField.Score], rankingIdentity: "profile-a");
        AssertFailure(SearchSessionFailureReason.IncompatibleCursor, () => first.Search(new TermQuery("body", "other"), 2, page.NextCursor, [SortField.Score], "profile-a"));
        AssertFailure(SearchSessionFailureReason.IncompatibleCursor, () => first.Search(new TermQuery("body", "common"), 2, page.NextCursor, [SortField.DocId], "profile-a"));
        AssertFailure(SearchSessionFailureReason.IncompatibleCursor, () => first.Search(new TermQuery("body", "common"), 2, page.NextCursor, [SortField.Score], "profile-b"));
        AssertFailure(SearchSessionFailureReason.IncompatibleCursor, () => second.Search(new TermQuery("body", "common"), 2, page.NextCursor, [SortField.Score], "profile-a"));
    }

    [Fact]
    public void IntegrityProtectedCursorRejectsTampering()
    {
        using var directory = new MMapDirectory(_path); using var searchers = new SearcherManager(directory);
        using var sessions = new SearchSessionManager(searchers, new SearchSessionOptions { CursorIntegrityKey = Enumerable.Range(0, 32).Select(static i => (byte)i).ToArray() });
        using var session = sessions.OpenSession(); var query = new TermQuery("body", "common"); var page = session.Search(query, 2);
        string token = page.NextCursor!; char replacement = token[^1] == 'a' ? 'b' : 'a'; string altered = token[..^1] + replacement;
        AssertFailure(SearchSessionFailureReason.IntegrityFailure, () => session.Search(query, 2, altered));
        Assert.Equal(1, sessions.GetDiagnostics().IntegrityFailures);
    }

    [Fact]
    public void ExpiryAndResourceLimitsAreExplicit()
    {
        var time = new ManualTimeProvider(DateTimeOffset.UtcNow);
        using var directory = new MMapDirectory(_path); using var searchers = new SearcherManager(directory);
        using var sessions = new SearchSessionManager(searchers, new SearchSessionOptions { MaximumLifetime = TimeSpan.FromMinutes(1), MaximumConcurrentSessions = 1, TimeProvider = time });
        using var first = sessions.OpenSession();
        AssertFailure(SearchSessionFailureReason.ResourceLimit, () => sessions.OpenSession());
        time.Advance(TimeSpan.FromMinutes(2));
        AssertFailure(SearchSessionFailureReason.Expired, () => first.Search(new TermQuery("body", "common"), 2));
        Assert.Equal(1, sessions.GetDiagnostics().Expired);
    }

    [Fact]
    public void OldestSessionCanBeEvictedAndLeaseReleased()
    {
        using var directory = new MMapDirectory(_path); using var searchers = new SearcherManager(directory);
        using var sessions = new SearchSessionManager(searchers, new SearchSessionOptions { MaximumConcurrentSessions = 1, LimitPolicy = SearchSessionLimitPolicy.EvictOldest });
        using var first = sessions.OpenSession(); using var second = sessions.OpenSession();
        AssertFailure(SearchSessionFailureReason.Evicted, () => first.Search(new TermQuery("body", "common"), 1));
        Assert.Single(second.Search(new TermQuery("body", "common"), 1).Results.ScoreDocs);
        Assert.Equal(1, sessions.GetDiagnostics().Evicted);
    }

    [Fact]
    public void UnsupportedRankingPipelineAndMissingGenerationAreRejected()
    {
        using var directory = new MMapDirectory(_path); using var searchers = new SearcherManager(directory); using var sessions = new SearchSessionManager(searchers);
        AssertFailure(SearchSessionFailureReason.MissingGeneration, () => sessions.OpenSession(int.MaxValue));
        using var session = sessions.OpenSession();
        var profile = new RankingProfile("web", "1", new RankingPipeline([new ScoreFunctionStage("score", DoubleValuesSource.Scores, RankingScoreCombination.Add, 10)]));
        AssertFailure(SearchSessionFailureReason.UnsupportedPagination, () => session.Search(new RankingSearchRequest(new TermQuery("body", "common"), 2, profile)));
    }

    [Fact]
    public void DiagnosticsCountSharedGenerationBytesOnce()
    {
        using var directory = new MMapDirectory(_path); using var searchers = new SearcherManager(directory); using var sessions = new SearchSessionManager(searchers);
        using var first = sessions.OpenSession(); using var second = sessions.OpenSession();
        var diagnostics = sessions.GetDiagnostics();
        Assert.Equal(2, diagnostics.ActiveSessions); Assert.Single(diagnostics.RetainedGenerations);
        Assert.Equal(2, diagnostics.RetainedGenerations[0].SessionCount); Assert.True(diagnostics.RetainedGenerations[0].RetainedBytes > 0);
    }

    [Fact]
    public void RetainedByteLimitRejectsSnapshotBeforePublishingSession()
    {
        using var directory = new MMapDirectory(_path); using var searchers = new SearcherManager(directory);
        using var sessions = new SearchSessionManager(searchers, new SearchSessionOptions { MaximumRetainedBytes = 1 });
        AssertFailure(SearchSessionFailureReason.ResourceLimit, () => sessions.OpenSession());
        Assert.Equal(0, sessions.GetDiagnostics().ActiveSessions);
    }

    [Fact]
    public void MalformedCursorFuzzOnlyReturnsTypedFailures()
    {
        using var directory = new MMapDirectory(_path); using var searchers = new SearcherManager(directory);
        using var sessions = new SearchSessionManager(searchers, new SearchSessionOptions { CursorIntegrityKey = Enumerable.Repeat((byte)7, 32).ToArray() });
        using var session = sessions.OpenSession(); var query = new TermQuery("body", "common");
        var random = new Random(42);
        for (int i = 0; i < 250; i++)
        {
            string token = Convert.ToBase64String(Enumerable.Range(0, random.Next(1, 128)).Select(_ => (byte)random.Next(256)).ToArray());
            var exception = Record.Exception(() => session.Search(query, 2, token));
            Assert.IsType<SearchSessionException>(exception);
        }
    }

    [Fact]
    public async Task ConcurrentSearchAndCloseReleaseSessionSafely()
    {
        using var directory = new MMapDirectory(_path); using var searchers = new SearcherManager(directory); using var sessions = new SearchSessionManager(searchers);
        using var session = sessions.OpenSession(); var query = new TermQuery("body", "common");
        var searches = Enumerable.Range(0, 16).Select(index => Task.Run(() =>
        {
            _ = index;
            try { _ = session.Search(query, 2); }
            catch (SearchSessionException ex) when (ex.Reason == SearchSessionFailureReason.Closed) { }
        })).ToArray();
        sessions.Close(session.Id); await Task.WhenAll(searches);
        Assert.Equal(0, sessions.GetDiagnostics().ActiveSessions);
    }

    public static TheoryData<SortField[]> Sorts
    {
        get
        {
            var data = new TheoryData<SortField[]>();
            data.Add([SortField.Score]); data.Add([SortField.DocId]); data.Add([SortField.Numeric("rank")]);
            data.Add([SortField.Int64("ordinal", descending: true)]);
            data.Add([SortField.String("group"), SortField.Numeric("rank", descending: true)]);
            data.Add([SortField.SortedNumeric("multi", SortValueSelector.Max, descending: true)]);
            return data;
        }
    }

    private int[] ReadAll(SearchSession session, Query query, int size, SortField[] sorts, string? cursor = null)
    {
        var ids = new List<int>();
        do { var page = session.Search(query, size, cursor, sorts); ids.AddRange(page.Results.ScoreDocs.Select(static hit => hit.DocId)); cursor = page.NextCursor; } while (cursor is not null);
        return ids.ToArray();
    }
    private void Seed(int count)
    {
        using var directory = new MMapDirectory(_path); using var writer = new IndexWriter(directory, new IndexWriterConfig());
        for (int i = 0; i < count; i++) { Add(writer, i); if (i % 4 == 3) writer.Commit(); } writer.Commit();
    }
    private static void Add(IndexWriter writer, int value)
    {
        var document = new LeanDocument(); document.Add(new StringField("id", $"doc-{value:00}")); document.Add(new TextField("body", "common"));
        document.Add(new NumericField("rank", value % 4)); document.Add(new Int64Field("ordinal", value)); document.Add(new StringField("group", value % 2 == 0 ? "a" : "b"));
        document.Add(new NumericField("multi", value)); document.Add(new NumericField("multi", value + 100)); writer.AddDocument(document);
    }
    private static void AssertFailure(SearchSessionFailureReason reason, Action action) => Assert.Equal(reason, Assert.Throws<SearchSessionException>(action).Reason);

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    { private DateTimeOffset _now = now; public override DateTimeOffset GetUtcNow() => _now; internal void Advance(TimeSpan duration) => _now += duration; }
}
