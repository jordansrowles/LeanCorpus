using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>Controls the bounded lifetime and resource use of search sessions.</summary>
public sealed class SearchSessionOptions
{
    public TimeSpan MaximumLifetime { get; set; } = TimeSpan.FromMinutes(15);
    public int MaximumConcurrentSessions { get; set; } = 256;
    public int MaximumRetainedGenerations { get; set; } = 8;
    public long MaximumRetainedBytes { get; set; } = 4L * 1024 * 1024 * 1024;
    public int MaximumCursorBytes { get; set; } = 4096;
    public SearchSessionLimitPolicy LimitPolicy { get; set; } = SearchSessionLimitPolicy.RejectNew;
    public byte[]? CursorIntegrityKey { get; set; }
    public TimeProvider TimeProvider { get; set; } = TimeProvider.System;
}

public enum SearchSessionLimitPolicy { RejectNew, EvictOldest }
public enum SearchSessionFailureReason { Closed, Expired, Evicted, MissingGeneration, ResourceLimit, InvalidCursor, IntegrityFailure, IncompatibleCursor, UnsupportedPagination }

/// <summary>A typed search-session or cursor failure.</summary>
public sealed class SearchSessionException : InvalidOperationException
{
    public SearchSessionException(SearchSessionFailureReason reason, string message) : base(message) => Reason = reason;
    public SearchSessionFailureReason Reason { get; }
}

/// <summary>One page from a retained search snapshot.</summary>
public sealed record SearchSessionPage(TopDocs Results, string? NextCursor);

/// <summary>Current bounded-session resource and lifecycle diagnostics.</summary>
public sealed record SearchSessionDiagnostics(int ActiveSessions, TimeSpan OldestSessionAge,
    IReadOnlyList<RetainedSearchGeneration> RetainedGenerations, long Opened, long Closed,
    long Expired, long Evicted, long Rejected, long InvalidCursors, long IntegrityFailures);

public sealed record RetainedSearchGeneration(int CommitGeneration, int SessionCount, long RetainedBytes,
    IReadOnlyList<string> RetainedFiles, IReadOnlyList<string> FilesPreventingDeletion);

/// <summary>An opaque handle to one retained, read-only searcher snapshot.</summary>
public sealed class SearchSession : IDisposable
{
    private SearchSessionManager? _owner;
    internal SearchSession(SearchSessionManager owner, string id) { _owner = owner; Id = id; }
    public string Id { get; }

    public SearchSessionPage Search(Query query, int pageSize, string? cursor = null,
        IReadOnlyList<SortField>? sorts = null, string? rankingIdentity = null)
        => (_owner ?? throw new ObjectDisposedException(nameof(SearchSession))).Search(Id, query, pageSize, cursor, sorts, rankingIdentity);

    public SearchSessionPage Search(Ranking.RankingSearchRequest request, string? cursor = null,
        IReadOnlyList<SortField>? sorts = null)
        => (_owner ?? throw new ObjectDisposedException(nameof(SearchSession))).Search(Id, request, cursor, sorts);

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        owner?.Close(Id);
    }
}
