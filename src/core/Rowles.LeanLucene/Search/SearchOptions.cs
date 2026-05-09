namespace Rowles.LeanLucene.Search;

/// <summary>
/// Per-query options controlling resource usage and result delivery.
/// Pass to <c>IndexSearcher.Search(Query, int, SearchOptions)</c> to limit memory
/// and wall-clock time, or to <c>IndexSearcher.SearchStreaming</c> to receive
/// results in segment order rather than fully collected.
/// </summary>
/// <remarks>
/// Timeouts and cancellation are checked at segment boundaries. A query already
/// inside a hot inner loop will complete that segment before the deadline is
/// honoured. On early termination the returned <see cref="Scoring.TopDocs.IsPartial"/> is set.
/// </remarks>
public sealed class SearchOptions
{
    /// <summary>
    /// Approximate budget on the bytes the retained result accumulator may hold.
    /// Each retained candidate costs roughly 12 bytes (one <see cref="Scoring.ScoreDoc"/>).
    /// Regular top-N searches throw if the requested heap cannot fit within this budget.
    /// Streaming searches check the budget between segments and stop yielding when it is exhausted.
    /// </summary>
    public long MaxResultBytes { get; init; } = long.MaxValue;

    /// <summary>
    /// When true, callers are expected to invoke <c>IndexSearcher.SearchStreaming</c>
    /// instead of <c>Search</c>. The streaming path yields per-segment results in
    /// segment order without building a global top-N heap. Default: false.
    /// </summary>
    /// <remarks>
    /// Setting this on a regular <c>Search</c> call has no effect: the top-N path is
    /// always fully materialised. Use <c>SearchStreaming</c> to honour the flag.
    /// </remarks>
    public bool StreamResults { get; init; }

    /// <summary>
    /// Wall-clock deadline for the query. Checked between segments. When exceeded,
    /// the search stops and returns a partial result with
    /// <see cref="Scoring.TopDocs.IsPartial"/> set. Default: no limit.
    /// </summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Cancellation token honoured between segments. Default: <see cref="CancellationToken.None"/>.</summary>
    public CancellationToken CancellationToken { get; init; } = CancellationToken.None;

    /// <summary>Default options with no limits.</summary>
    public static SearchOptions Default { get; } = new();

    /// <summary>Creates options with a specific approximate result-byte budget.</summary>
    public static SearchOptions WithBudget(long maxBytes) => new() { MaxResultBytes = maxBytes };

    /// <summary>Creates options with a specific wall-clock deadline.</summary>
    public static SearchOptions WithTimeout(TimeSpan timeout) => new() { Timeout = timeout };

    /// <summary>Creates options with both a deadline and a result-byte budget.</summary>
    public static SearchOptions WithBudgetAndTimeout(long maxBytes, TimeSpan timeout)
        => new() { MaxResultBytes = maxBytes, Timeout = timeout };
}
