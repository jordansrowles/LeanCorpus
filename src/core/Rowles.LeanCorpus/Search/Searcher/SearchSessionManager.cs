using System.Security.Cryptography;
using System.Text;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Ranking;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>Owns bounded point-in-time search sessions over <see cref="SearcherManager"/> leases.</summary>
public sealed class SearchSessionManager : IDisposable
{
    private readonly SearcherManager _searchers;
    private readonly SearchSessionOptions _options;
    private readonly SearchCursorCodec _codec;
    private readonly string _indexIdentity;
    private readonly Dictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SearchSessionFailureReason> _tombstones = new(StringComparer.Ordinal);
    private readonly Queue<string> _tombstoneOrder = new();
    private readonly Lock _lock = new();
    private readonly Timer _timer;
    private bool _disposed;
    private long _opened, _closed, _expired, _evicted, _rejected, _invalidCursors, _integrityFailures;

    public SearchSessionManager(SearcherManager searchers, SearchSessionOptions? options = null)
    {
        _searchers = searchers ?? throw new ArgumentNullException(nameof(searchers));
        _options = CopyOptions(options ?? new SearchSessionOptions());
        ValidateOptions(_options);
        _codec = new SearchCursorCodec(_options.MaximumCursorBytes, _options.CursorIntegrityKey);
        string canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_searchers.DirectoryPath));
        _indexIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var interval = TimeSpan.FromSeconds(Math.Clamp(_options.MaximumLifetime.TotalSeconds / 4, 1, 30));
        _timer = new Timer(static state => ((SearchSessionManager)state!).ExpireSessions(), this, interval, interval);
    }

    public SearchSession OpenSession(int? commitGeneration = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SearcherLease lease;
        if (commitGeneration is { } generation)
        {
            if (!_searchers.TryAcquireLease(generation, out lease))
                throw new SearchSessionException(SearchSessionFailureReason.MissingGeneration, $"Committed generation {generation} is not retained by the searcher manager.");
        }
        else lease = _searchers.AcquireLease();

        string id = CreateId();
        var now = _options.TimeProvider.GetUtcNow();
        var state = new SessionState(id, lease, now, now + _options.MaximumLifetime);
        lock (_lock)
        {
            ThrowIfDisposed(); ExpireSessionsLocked(now);
            if (!MakeRoomLocked(state))
            {
                lease.Dispose(); Interlocked.Increment(ref _rejected);
                throw new SearchSessionException(SearchSessionFailureReason.ResourceLimit, "Opening the search session would exceed the configured session retention limits.");
            }
            _sessions.Add(id, state); Interlocked.Increment(ref _opened);
        }
        return new SearchSession(this, id);
    }

    internal SearchSessionPage Search(string id, RankingSearchRequest request, string? cursor, IReadOnlyList<SortField>? sorts)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Rules is not null || request.Profile.Pipeline.Stages.Count != 0 || request.Profile.FieldWeights.Count != 0 ||
            request.Profile.FieldSimilarities.Count != 0 || request.Profile.DefaultSimilarity is not null)
            throw new SearchSessionException(SearchSessionFailureReason.UnsupportedPagination, "This ranking request changes result ordering and does not provide stable continuation state.");
        return Search(id, request.Query, request.TopN, cursor, sorts, request.Profile.Fingerprint);
    }

    internal SearchSessionPage Search(string id, Query query, int pageSize, string? cursor,
        IReadOnlyList<SortField>? sorts, string? rankingIdentity)
    {
        ArgumentNullException.ThrowIfNull(query); ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        if (query is RrfQuery)
            throw new SearchSessionException(SearchSessionFailureReason.UnsupportedPagination,
                "Fusion queries do not provide stable cursor continuation state.");
        var effectiveSorts = sorts is null || sorts.Count == 0 ? new[] { SortField.Score } : sorts.ToArray();
        ValidateSorts(effectiveSorts);
        using var operation = AcquireOperation(id);
        string queryIdentity = QueryCache.CreateQueryFingerprint(query);
        string sortIdentity = CreateSortIdentity(effectiveSorts);
        string effectiveRankingIdentity = rankingIdentity ?? string.Empty;
        ScoreDoc? after = null;
        if (cursor is not null)
        {
            SearchCursorData decoded;
            try { decoded = _codec.Decode(cursor); }
            catch (SearchSessionException ex)
            {
                if (ex.Reason == SearchSessionFailureReason.IntegrityFailure) Interlocked.Increment(ref _integrityFailures);
                else Interlocked.Increment(ref _invalidCursors);
                throw;
            }
            if (decoded.SessionId != id || decoded.IndexIdentity != _indexIdentity || decoded.Generation != operation.State.Generation ||
                decoded.QueryIdentity != queryIdentity || decoded.SortIdentity != sortIdentity || decoded.RankingIdentity != effectiveRankingIdentity)
                throw new SearchSessionException(SearchSessionFailureReason.IncompatibleCursor, "Cursor does not belong to this session, query, sort, generation, index, or ranking identity.");
            var actual = operation.State.Searcher.CaptureCursorSortValues(decoded.After, effectiveSorts);
            if (!actual.SequenceEqual(decoded.SortValues))
                throw new SearchSessionException(SearchSessionFailureReason.IncompatibleCursor, "Cursor sort values do not match the retained snapshot.");
            after = decoded.After;
        }

        TopDocs results = after is { } boundary
            ? operation.State.Searcher.SearchAfter(boundary, query, pageSize, effectiveSorts)
            : operation.State.Searcher.Search(query, pageSize, effectiveSorts);
        string? next = null;
        if (results.ScoreDocs.Length == pageSize)
        {
            var last = results.ScoreDocs[^1];
            var values = operation.State.Searcher.CaptureCursorSortValues(last, effectiveSorts);
            if (values.Any(static value => value.Type is SortFieldType.Score or SortFieldType.Numeric && !double.IsFinite(value.Numeric)))
                throw new SearchSessionException(SearchSessionFailureReason.UnsupportedPagination, "Non-finite score and numeric sort boundaries cannot be paginated.");
            next = _codec.Encode(new SearchCursorData(id, _indexIdentity, operation.State.Generation,
                queryIdentity, sortIdentity, effectiveRankingIdentity, last, values));
        }
        return new SearchSessionPage(results, next);
    }

    public SearchSessionDiagnostics GetDiagnostics()
    {
        lock (_lock)
        {
            var now = _options.TimeProvider.GetUtcNow(); ExpireSessionsLocked(now);
            var generations = _sessions.Values.GroupBy(static state => state.Searcher)
                .Select(group => new RetainedSearchGeneration(group.First().Generation, group.Count(),
                    group.Key.SnapshotRetainedBytes, group.Key.SnapshotRetainedFiles, group.Key.SnapshotPendingDeletionFiles))
                .OrderBy(static generation => generation.CommitGeneration).ToArray();
            var oldest = _sessions.Count == 0 ? TimeSpan.Zero : now - _sessions.Values.Min(static state => state.CreatedAt);
            return new SearchSessionDiagnostics(_sessions.Count, oldest, generations, Interlocked.Read(ref _opened),
                Interlocked.Read(ref _closed), Interlocked.Read(ref _expired), Interlocked.Read(ref _evicted),
                Interlocked.Read(ref _rejected), Interlocked.Read(ref _invalidCursors), Interlocked.Read(ref _integrityFailures));
        }
    }

    internal void Close(string id)
    {
        lock (_lock)
        {
            if (!_sessions.Remove(id, out var state)) return;
            RetireLocked(state, SearchSessionFailureReason.Closed); Interlocked.Increment(ref _closed);
        }
    }

    public void Dispose()
    {
        SessionState[] states;
        lock (_lock)
        {
            if (_disposed) return; _disposed = true; _timer.Dispose();
            states = _sessions.Values.ToArray(); _sessions.Clear();
            foreach (var state in states) RetireLocked(state, SearchSessionFailureReason.Closed);
        }
    }

    private OperationLease AcquireOperation(string id)
    {
        lock (_lock)
        {
            ThrowIfDisposed(); var now = _options.TimeProvider.GetUtcNow(); ExpireSessionsLocked(now);
            if (!_sessions.TryGetValue(id, out var state))
            {
                var reason = _tombstones.TryGetValue(id, out var stored) ? stored : SearchSessionFailureReason.Closed;
                throw new SearchSessionException(reason, $"Search session is {reason.ToString().ToLowerInvariant()}.");
            }
            state.ActiveOperations++; return new OperationLease(this, state);
        }
    }

    private void ReleaseOperation(SessionState state)
    {
        lock (_lock) { state.ActiveOperations--; if (state.Retired && state.ActiveOperations == 0) state.Lease.Dispose(); }
    }

    private bool MakeRoomLocked(SessionState candidate)
    {
        while (ExceedsLimitsLocked(candidate))
        {
            if (_options.LimitPolicy == SearchSessionLimitPolicy.RejectNew || _sessions.Count == 0) return false;
            var oldest = _sessions.Values.MinBy(static state => state.CreatedAt)!;
            _sessions.Remove(oldest.Id); RetireLocked(oldest, SearchSessionFailureReason.Evicted); Interlocked.Increment(ref _evicted);
        }
        return true;
    }

    private bool ExceedsLimitsLocked(SessionState candidate)
    {
        if (_sessions.Count + 1 > _options.MaximumConcurrentSessions) return true;
        var unique = _sessions.Values.Select(static s => s.Searcher).Distinct().ToList();
        if (!unique.Contains(candidate.Searcher)) unique.Add(candidate.Searcher);
        return unique.Count > _options.MaximumRetainedGenerations || unique.Sum(static s => s.SnapshotRetainedBytes) > _options.MaximumRetainedBytes;
    }

    private void ExpireSessions() { try { lock (_lock) { if (!_disposed) ExpireSessionsLocked(_options.TimeProvider.GetUtcNow()); } } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "search session expiry"); } }
    private void ExpireSessionsLocked(DateTimeOffset now)
    {
        foreach (var state in _sessions.Values.Where(state => now >= state.ExpiresAt).ToArray())
        { _sessions.Remove(state.Id); RetireLocked(state, SearchSessionFailureReason.Expired); Interlocked.Increment(ref _expired); }
    }
    private void RetireLocked(SessionState state, SearchSessionFailureReason reason)
    {
        state.Retired = true; AddTombstoneLocked(state.Id, reason); if (state.ActiveOperations == 0) state.Lease.Dispose();
    }
    private void AddTombstoneLocked(string id, SearchSessionFailureReason reason)
    {
        _tombstones[id] = reason; _tombstoneOrder.Enqueue(id);
        int maximum = Math.Max(64, _options.MaximumConcurrentSessions * 2);
        while (_tombstoneOrder.Count > maximum) _tombstones.Remove(_tombstoneOrder.Dequeue());
    }
    private static string CreateId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    private static string CreateSortIdentity(IReadOnlyList<SortField> sorts) => RankingProfile.FingerprintOf(string.Join("\n", sorts.Select(static sort => $"{sort.Type}:{sort.FieldName}:{sort.Descending}:{sort.Selector}")));
    private static void ValidateSorts(IReadOnlyList<SortField> sorts)
    { if (sorts.Count is < 1 or > 32) throw new ArgumentException("Cursor pagination supports between one and 32 sort fields.", nameof(sorts)); foreach (var sort in sorts) if (sort.Type is SortFieldType.Numeric or SortFieldType.Int64 or SortFieldType.String && string.IsNullOrWhiteSpace(sort.FieldName)) throw new ArgumentException("Field sorts require a field name.", nameof(sorts)); }
    private static void ValidateOptions(SearchSessionOptions options)
    {
        if (options.MaximumLifetime <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options.MaximumLifetime));
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumConcurrentSessions, 1); ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumRetainedGenerations, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumRetainedBytes, 1); ArgumentOutOfRangeException.ThrowIfLessThan(options.MaximumCursorBytes, 256);
        ArgumentNullException.ThrowIfNull(options.TimeProvider); if (options.CursorIntegrityKey is { Length: < 16 }) throw new ArgumentException("Cursor integrity keys must contain at least 16 bytes.", nameof(options.CursorIntegrityKey));
    }
    private static SearchSessionOptions CopyOptions(SearchSessionOptions options) => new()
    {
        MaximumLifetime = options.MaximumLifetime,
        MaximumConcurrentSessions = options.MaximumConcurrentSessions,
        MaximumRetainedGenerations = options.MaximumRetainedGenerations,
        MaximumRetainedBytes = options.MaximumRetainedBytes,
        MaximumCursorBytes = options.MaximumCursorBytes,
        LimitPolicy = options.LimitPolicy,
        CursorIntegrityKey = options.CursorIntegrityKey?.ToArray(),
        TimeProvider = options.TimeProvider
    };
    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class SessionState(string id, SearcherLease lease, DateTimeOffset createdAt, DateTimeOffset expiresAt)
    {
        internal string Id { get; } = id; internal SearcherLease Lease { get; } = lease; internal IndexSearcher Searcher => Lease.Searcher;
        internal int Generation => Lease.CommitGeneration; internal DateTimeOffset CreatedAt { get; } = createdAt; internal DateTimeOffset ExpiresAt { get; } = expiresAt;
        internal int ActiveOperations; internal bool Retired;
    }
    private sealed class OperationLease(SearchSessionManager owner, SessionState state) : IDisposable
    { private SearchSessionManager? _owner = owner; internal SessionState State { get; } = state; public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseOperation(State); }
}
