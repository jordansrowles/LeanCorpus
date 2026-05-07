namespace Rowles.LeanLucene.Search.Searcher;

/// <summary>
/// Configuration for the IndexSearcher.
/// </summary>
public sealed class IndexSearcherConfig
{
    /// <summary>Scoring model. Default: BM25.</summary>
    public ISimilarity Similarity { get; set; } = Bm25Similarity.Instance;

    /// <summary>
    /// Whether to use parallel segment search when multiple segments exist.
    /// Disable for deterministic ordering or low-latency single-segment workloads. Default: true.
    /// </summary>
    public bool ParallelSearch { get; set; } = true;

    /// <summary>
    /// Maximum degree of parallelism for multi-segment search.
    /// -1 means use Environment.ProcessorCount. Default: -1.
    /// </summary>
    public int MaxConcurrency { get; set; } = -1;

    /// <summary>
    /// Enable the query result cache. When true, repeat queries against the same
    /// commit generation return cached results. Default: false.
    /// </summary>
    public bool EnableQueryCache { get; set; }

    /// <summary>
    /// Maximum number of entries in the query result cache. Default: 1024.
    /// </summary>
    public int QueryCacheMaxEntries { get; set; } = 1024;

    /// <summary>
    /// Metrics collector for search latency, cache hit/miss, etc.
    /// Default: <see cref="Diagnostics.NullMetricsCollector"/> (no-op).
    /// </summary>
    public Diagnostics.IMetricsCollector Metrics { get; set; } = Diagnostics.NullMetricsCollector.Instance;

    /// <summary>
    /// Optional slow query log. When set, queries exceeding the configured threshold
    /// are written as JSON lines to the log output. Default: null (disabled).
    /// </summary>
    public Diagnostics.SlowQueryLog? SlowQueryLog { get; set; }

    /// <summary>
    /// Optional per-search event analytics. When set, each search produces a
    /// <see cref="Diagnostics.SearchEvent"/> in a bounded ring buffer. Default: null (disabled).
    /// </summary>
    public Diagnostics.SearchAnalytics? SearchAnalytics { get; set; }
}
