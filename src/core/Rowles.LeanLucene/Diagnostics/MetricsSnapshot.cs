namespace Rowles.LeanLucene.Diagnostics;

/// <summary>
/// Point-in-time snapshot of operational metrics.
/// </summary>
public sealed class MetricsSnapshot
{
    /// <summary>Gets an empty snapshot with all counters at zero.</summary>
    public static readonly MetricsSnapshot Empty = new();

    /// <summary>Gets the total number of searches executed.</summary>
    public long SearchCount { get; init; }

    /// <summary>Gets the cumulative search latency in milliseconds.</summary>
    public long SearchTotalMs { get; init; }

    /// <summary>Gets the maximum observed search latency in milliseconds.</summary>
    public long SearchMaxMs { get; init; }

    /// <summary>Gets the average search latency in milliseconds.</summary>
    public double SearchAvgMs { get; init; }

    /// <summary>Gets the total number of query cache hits.</summary>
    public long CacheHits { get; init; }

    /// <summary>Gets the total number of query cache misses.</summary>
    public long CacheMisses { get; init; }

    /// <summary>Gets the fraction of cache lookups that were hits (0–1).</summary>
    public double CacheHitRate { get; init; }

    /// <summary>Gets the total number of segment flush operations.</summary>
    public long FlushCount { get; init; }

    /// <summary>Gets the cumulative time spent flushing segments in milliseconds.</summary>
    public long FlushTotalMs { get; init; }

    /// <summary>Gets the total number of segment merge operations.</summary>
    public long MergeCount { get; init; }

    /// <summary>Gets the total number of segments merged across all merge operations.</summary>
    public long MergeSegments { get; init; }

    /// <summary>Gets the cumulative time spent merging segments in milliseconds.</summary>
    public long MergeTotalMs { get; init; }

    /// <summary>Gets the total number of index commit operations.</summary>
    public long CommitCount { get; init; }

    /// <summary>Gets the cumulative time spent committing in milliseconds.</summary>
    public long CommitTotalMs { get; init; }

    /// <summary>
    /// Latency histogram: buckets are [&lt;1ms, &lt;5ms, &lt;10ms, &lt;50ms, &lt;100ms, &lt;500ms, &lt;1000ms, ≥1000ms].
    /// </summary>
    public long[]? LatencyHistogram { get; init; }

    /// <summary>Total number of HNSW graph traversals performed during search.</summary>
    public long HnswSearchCount { get; init; }

    /// <summary>Cumulative time spent in HNSW graph traversal in milliseconds.</summary>
    public long HnswSearchTotalMs { get; init; }

    /// <summary>Total nodes visited across all HNSW graph traversals (recall-vs-cost signal).</summary>
    public long HnswNodesVisited { get; init; }

    /// <summary>Total number of HNSW graphs built (flush + merge).</summary>
    public long HnswBuildCount { get; init; }

    /// <summary>Cumulative time spent building HNSW graphs in milliseconds.</summary>
    public long HnswBuildTotalMs { get; init; }

    /// <summary>Total nodes inserted across all HNSW build operations.</summary>
    public long HnswNodesBuilt { get; init; }
}
