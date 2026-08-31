using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Codecs.CodecKit;

namespace Rowles.LeanCorpus.Search.Searcher;

/// <summary>
/// Configuration for the IndexSearcher.
/// </summary>
public sealed class IndexSearcherConfig
{
    private Diagnostics.SlowQueryLog? _slowQueryLog;
    private bool _ownsSlowQueryLog;
    private bool _diagnosticsOwnedByManager;
    private int _slowQueryLogDisposed;

    /// <summary>Initialises a configuration and captures the current process-wide defaults.</summary>
    public IndexSearcherConfig()
        : this(LeanCorpusDefaults.GetSnapshot(), applyFactories: true)
    {
    }

    internal IndexSearcherConfig(LeanCorpusDefaultSnapshot snapshot, bool applyFactories)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        CodecCatalog = Effective(snapshot.Codecs.Catalog, CodecCatalog.Default);
        CompatibilityMode = Effective(snapshot.IndexOpen.CompatibilityMode, IndexOpenCompatibilityMode.Strict);

        var defaults = snapshot.IndexSearcher;
        ParallelSearch = Effective(defaults.ParallelSearch, ParallelSearch);
        MaxConcurrency = Effective(defaults.MaxConcurrency, MaxConcurrency);
        EnableBlockMaxWand = Effective(defaults.EnableBlockMaxWand, EnableBlockMaxWand);
        MaxCachedSegmentReaders = Effective(defaults.MaxCachedSegmentReaders, MaxCachedSegmentReaders);
        EnableQueryCache = Effective(defaults.QueryCache.Enabled, EnableQueryCache);
        QueryCacheMaxEntries = Effective(defaults.QueryCache.MaxEntries, QueryCacheMaxEntries);

        if (!applyFactories)
            return;

        if (snapshot.Scoring.SimilarityFactory.IsSet)
            Similarity = Require(snapshot.Scoring.SimilarityFactory.Value(), nameof(snapshot.Scoring.SimilarityFactory));
        if (snapshot.Scoring.PerFieldSimilarityFactories.Count > 0)
        {
            var similarities = new Dictionary<string, ISimilarity>(
                snapshot.Scoring.PerFieldSimilarityFactories.Count, StringComparer.Ordinal);
            foreach (var pair in snapshot.Scoring.PerFieldSimilarityFactories)
                similarities.Add(pair.Key, Require(pair.Value(), $"{nameof(snapshot.Scoring.PerFieldSimilarityFactories)}[{pair.Key}]"));
            PerFieldSimilarities = similarities;
        }
        if (snapshot.Diagnostics.MetricsCollectorFactory.IsSet)
            Metrics = Require(snapshot.Diagnostics.MetricsCollectorFactory.Value(), nameof(snapshot.Diagnostics.MetricsCollectorFactory));
        if (snapshot.Diagnostics.SlowQueryLogFactory.IsSet)
            SetGlobalSlowQueryLog(snapshot.Diagnostics.SlowQueryLogFactory.Value());
        if (snapshot.Diagnostics.SearchAnalyticsFactory.IsSet)
            SearchAnalytics = snapshot.Diagnostics.SearchAnalyticsFactory.Value();
    }

    private static T Effective<T>(DefaultOverride<T> value, T builtIn)
        => value.IsSet ? value.Value : builtIn;

    private static T Require<T>(T? value, string name) where T : class
        => value ?? throw new InvalidOperationException($"The global default factory '{name}' returned null.");

    internal void Validate()
    {
        if (MaxCachedSegmentReaders < 1)
            throw new ArgumentOutOfRangeException(nameof(MaxCachedSegmentReaders), MaxCachedSegmentReaders,
                "MaxCachedSegmentReaders must be at least one.");
        if (EnableQueryCache && QueryCacheMaxEntries < 1)
            throw new ArgumentOutOfRangeException(nameof(QueryCacheMaxEntries), QueryCacheMaxEntries,
                "QueryCacheMaxEntries must be at least one when query caching is enabled.");
    }

    internal void SetGlobalSlowQueryLog(Diagnostics.SlowQueryLog? value)
    {
        _slowQueryLog = value;
        _ownsSlowQueryLog = value is not null;
    }

    internal void SetDiagnosticsOwnedByManager() => _diagnosticsOwnedByManager = true;

    internal void DisposeOwnedDiagnostics()
    {
        if (_diagnosticsOwnedByManager || !_ownsSlowQueryLog || _slowQueryLog is null)
            return;
        if (Interlocked.Exchange(ref _slowQueryLogDisposed, 1) == 0)
            _slowQueryLog.Dispose();
    }

    internal void DisposeManagerOwnedDiagnostics()
    {
        if (!_diagnosticsOwnedByManager || !_ownsSlowQueryLog || _slowQueryLog is null)
            return;
        if (Interlocked.Exchange(ref _slowQueryLogDisposed, 1) == 0)
            _slowQueryLog.Dispose();
    }

    /// <summary>Gets or sets the immutable codec catalogue used for compatibility inspection.</summary>
    public CodecCatalog CodecCatalog { get; set; } = CodecCatalog.Default;

    /// <summary>Scoring model. Default: BM25.</summary>
    public ISimilarity Similarity { get; set; } = Bm25Similarity.Instance;

    /// <summary>
    /// Optional field-specific scoring models. Fields not present use <see cref="Similarity"/>.
    /// </summary>
    public IReadOnlyDictionary<string, ISimilarity>? PerFieldSimilarities { get; set; }

    /// <summary>
    /// Compatibility guardrail applied when opening an index. Defaults to strict mode.
    /// </summary>
    public IndexOpenCompatibilityMode CompatibilityMode { get; set; } = IndexOpenCompatibilityMode.Strict;

    /// <summary>
    /// Whether to use parallel segment search when multiple segments exist.
    /// Disable for deterministic ordering and low-latency small-segment workloads. Default: false.
    /// </summary>
    public bool ParallelSearch { get; set; }

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
    /// Maximum number of heavy segment-reader states retained by this searcher.
    /// Active states are protected from eviction. Default: 256.
    /// </summary>
    public int MaxCachedSegmentReaders { get; set; } = 256;

    /// <summary>
    /// Optional shared query cache. When set, <see cref="IndexSearcher"/> uses this
    /// cache instead of creating a per-instance one. <see cref="SearcherManager"/>
    /// sets this to persist the cache across searcher refreshes.
    /// </summary>
    internal QueryCache? SharedCache { get; set; }

    /// <summary>
    /// Metrics collector for search latency, cache hit/miss, etc.
    /// Default: <see cref="Diagnostics.NullMetricsCollector"/> (no-op).
    /// </summary>
    public Diagnostics.IMetricsCollector Metrics { get; set; } = Diagnostics.NullMetricsCollector.Instance;

    /// <summary>
    /// Optional slow query log. When set, queries exceeding the configured threshold
    /// are written as JSON lines to the log output. Default: null (disabled). The owner
    /// of this configuration graph is responsible for disposing the log. A manager keeps
    /// a factory-created log across searcher refreshes and disposes it with the manager.
    /// </summary>
    public Diagnostics.SlowQueryLog? SlowQueryLog
    {
        get => _slowQueryLog;
        set
        {
            _slowQueryLog = value;
            _ownsSlowQueryLog = false;
        }
    }

    /// <summary>
    /// Optional per-search event analytics. When set, each search produces a
    /// <see cref="Diagnostics.SearchEvent"/> in a bounded ring buffer. Default: null (disabled).
    /// </summary>
    public Diagnostics.SearchAnalytics? SearchAnalytics { get; set; }

    /// <summary>
    /// Enable Block-Max WAND scoring for disjunctive (OR) queries.
    /// When true, the searcher uses per-block impact metadata to skip
    /// non-competitive blocks during top-K evaluation. Most effective for
    /// large OR queries against indexes with many documents per term.
    /// Default: false.
    /// </summary>
    public bool EnableBlockMaxWand { get; set; }
}
