namespace Rowles.LeanLucene.Diagnostics;

/// <summary>
/// A single search event captured by <see cref="SearchAnalytics"/>.
/// </summary>
public sealed record SearchEvent
{
    /// <summary>Gets the UTC timestamp when the search was executed.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Gets the short type name of the query (e.g., <c>TermQuery</c>).</summary>
    public string QueryType { get; init; } = string.Empty;

    /// <summary>Gets the string representation of the query.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Gets the elapsed search time in milliseconds, rounded to two decimal places.</summary>
    public double ElapsedMs { get; init; }

    /// <summary>Gets the total number of documents that matched the query.</summary>
    public int TotalHits { get; init; }

    /// <summary>Gets a value indicating whether this search was served from the query cache.</summary>
    public bool CacheHit { get; init; }
}
