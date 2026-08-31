using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Diagnostics;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus;

/// <summary>
/// Mutable builder used only while publishing a <see cref="LeanCorpusDefaults"/> snapshot.
/// Nullable scalar and factory properties use <see langword="null"/> to mean that no
/// process-wide override is published, so the receiving configuration uses its built-in value.
/// </summary>
public sealed class LeanCorpusDefaultOptions
{
    internal LeanCorpusDefaultOptions(LeanCorpusDefaultSnapshot snapshot)
    {
        Codecs = new(snapshot.Codecs);
        IndexOpen = new(snapshot.IndexOpen);
        IndexWriter = new(snapshot.IndexWriter);
        IndexSearcher = new(snapshot.IndexSearcher);
        SearcherManager = new(snapshot.SearcherManager);
        JsonMapping = new(snapshot.JsonMapping);
        Scoring = new(snapshot.Scoring);
        Diagnostics = new(snapshot.Diagnostics);
        Search = new(snapshot.Search);
    }

    /// <summary>Gets defaults applied to newly created codec-aware components.</summary>
    public CodecDefaultOptions Codecs { get; }

    /// <summary>Gets defaults applied when a component opens an existing index.</summary>
    public IndexOpenDefaultOptions IndexOpen { get; }

    /// <summary>Gets defaults applied to newly created index-writer configurations.</summary>
    public IndexWriterDefaultOptions IndexWriter { get; }

    /// <summary>Gets defaults applied to newly created index-searcher configurations.</summary>
    public IndexSearcherDefaultOptions IndexSearcher { get; }

    /// <summary>Gets defaults applied to newly created searcher-manager configurations.</summary>
    public SearcherManagerDefaultOptions SearcherManager { get; }

    /// <summary>Gets defaults applied to newly created JSON mapping options.</summary>
    public JsonMappingDefaultOptions JsonMapping { get; }

    /// <summary>Gets optional scoring factories used by new writer and searcher configurations.</summary>
    public ScoringDefaultOptions Scoring { get; }

    /// <summary>Gets optional diagnostic factories used by new writer and searcher configurations.</summary>
    public DiagnosticsDefaultOptions Diagnostics { get; }

    /// <summary>Gets optional resource defaults applied to new query options.</summary>
    public SearchDefaultOptions Search { get; }

    internal LeanCorpusDefaultSnapshot ToSnapshot()
        => new()
        {
            Codecs = Codecs.ToSnapshot(),
            IndexOpen = IndexOpen.ToSnapshot(),
            IndexWriter = IndexWriter.ToSnapshot(),
            IndexSearcher = IndexSearcher.ToSnapshot(),
            SearcherManager = SearcherManager.ToSnapshot(),
            JsonMapping = JsonMapping.ToSnapshot(),
            Scoring = Scoring.ToSnapshot(),
            Diagnostics = Diagnostics.ToSnapshot(),
            Search = Search.ToSnapshot(),
        };
}

/// <summary>Defaults applied to newly created codec-aware components.</summary>
public sealed class CodecDefaultOptions
{
    internal CodecDefaultOptions(CodecDefaultsSnapshot snapshot)
        => Catalog = snapshot.Catalog.IsSet ? snapshot.Catalog.Value : null;

    /// <summary>Gets or sets the immutable catalogue used by new components. Null clears the override.</summary>
    public CodecCatalog? Catalog { get; set; }

    internal CodecDefaultsSnapshot ToSnapshot()
        => new() { Catalog = Catalog is null ? DefaultOverride<CodecCatalog>.Unset : DefaultOverride<CodecCatalog>.Set(Catalog) };
}

/// <summary>Defaults applied when a component opens an existing index.</summary>
public sealed class IndexOpenDefaultOptions
{
    internal IndexOpenDefaultOptions(IndexOpenDefaultsSnapshot snapshot)
        => CompatibilityMode = snapshot.CompatibilityMode.IsSet ? snapshot.CompatibilityMode.Value : null;

    /// <summary>Gets or sets the compatibility guardrail for newly created components. Null clears the override.</summary>
    public IndexOpenCompatibilityMode? CompatibilityMode { get; set; }

    internal IndexOpenDefaultsSnapshot ToSnapshot()
        => new() { CompatibilityMode = CompatibilityMode.HasValue ? DefaultOverride<IndexOpenCompatibilityMode>.Set(CompatibilityMode.Value) : DefaultOverride<IndexOpenCompatibilityMode>.Unset };
}

/// <summary>
/// Defaults applied to newly created <see cref="Index.Indexer.IndexWriterConfig"/> instances.
/// Nullable scalar properties retain built-in values when unset; setting one to
/// <see langword="null"/> clears its process-wide override.
/// </summary>
public sealed class IndexWriterDefaultOptions
{
    internal IndexWriterDefaultOptions(IndexWriterDefaultsSnapshot snapshot)
    {
        RamBufferSizeMB = Get(snapshot.RamBufferSizeMB);
        RamPerThreadHardLimitMB = Get(snapshot.RamPerThreadHardLimitMB);
        MaxConcurrentFlushes = Get(snapshot.MaxConcurrentFlushes);
        MaxBufferedDocs = Get(snapshot.MaxBufferedDocs);
        MaxQueuedDocs = Get(snapshot.MaxQueuedDocs);
        MaxQueuedBytes = Get(snapshot.MaxQueuedBytes);
        StorePayloads = Get(snapshot.StorePayloads);
        StoreTermVectors = Get(snapshot.StoreTermVectors);
        UseCompoundFile = Get(snapshot.UseCompoundFile);
        DurableCommits = Get(snapshot.DurableCommits);
        CompressionPolicy = Get(snapshot.CompressionPolicy);
        StoredFieldBlockSize = Get(snapshot.StoredFieldBlockSize);
        PostingsSkipInterval = Get(snapshot.PostingsSkipInterval);
        BKDMaxLeafSize = Get(snapshot.BKDMaxLeafSize);
        AnalyserInternCacheSize = Get(snapshot.AnalyserInternCacheSize);
        MaxTokensPerDocument = Get(snapshot.MaxTokensPerDocument);
        TokenBudgetPolicy = Get(snapshot.TokenBudgetPolicy);
        MergeThreshold = Get(snapshot.MergeThreshold);
        MergeThrottleSegments = Get(snapshot.MergeThrottleSegments);
        MaxConcurrentMerges = Get(snapshot.MaxConcurrentMerges);
        MaxPendingMergeBytes = Get(snapshot.MaxPendingMergeBytes);
        NormaliseVectors = Get(snapshot.NormaliseVectors);
        VectorQuantisation = Get(snapshot.VectorQuantisation);
        BuildHnswOnFlush = Get(snapshot.BuildHnswOnFlush);
        HnswSeed = snapshot.HnswSeed.IsSet ? snapshot.HnswSeed.Value : null;
        TrackSequenceNumbers = Get(snapshot.TrackSequenceNumbers);
        SoftDeletesEnabled = Get(snapshot.SoftDeletesEnabled);
        SoftDeleteRetentionSeconds = Get(snapshot.SoftDeleteRetentionSeconds);
        DeletionPolicyFactory = GetReference(snapshot.DeletionPolicyFactory);
        MergePolicyFactory = GetReference(snapshot.MergePolicyFactory);
        Hnsw = new(snapshot.Hnsw);
        Analysis = new(snapshot.Analysis);
    }

    /// <summary>Gets or sets the RAM buffer size in megabytes.</summary>
    public double? RamBufferSizeMB { get; set; }
    /// <summary>Gets or sets the hard per-thread RAM limit in megabytes.</summary>
    public double? RamPerThreadHardLimitMB { get; set; }
    /// <summary>Gets or sets the maximum number of concurrent flushes.</summary>
    public int? MaxConcurrentFlushes { get; set; }
    /// <summary>Gets or sets the maximum buffered document count.</summary>
    public int? MaxBufferedDocs { get; set; }
    /// <summary>Gets or sets the maximum queued document count.</summary>
    public int? MaxQueuedDocs { get; set; }
    /// <summary>Gets or sets the maximum estimated bytes retained by queued documents.</summary>
    public long? MaxQueuedBytes { get; set; }
    /// <summary>Gets or sets whether postings payloads are stored.</summary>
    public bool? StorePayloads { get; set; }
    /// <summary>Gets or sets whether term vectors are stored.</summary>
    public bool? StoreTermVectors { get; set; }
    /// <summary>Gets or sets whether immutable segment files use compound storage.</summary>
    public bool? UseCompoundFile { get; set; }
    /// <summary>Gets or sets whether commits are flushed durably.</summary>
    public bool? DurableCommits { get; set; }
    /// <summary>Gets or sets the stored-fields compression policy.</summary>
    public FieldCompressionPolicy? CompressionPolicy { get; set; }
    /// <summary>Gets or sets the stored-field block size.</summary>
    public int? StoredFieldBlockSize { get; set; }
    /// <summary>Gets or sets the postings skip interval.</summary>
    public int? PostingsSkipInterval { get; set; }
    /// <summary>Gets or sets the BKD leaf size.</summary>
    public int? BKDMaxLeafSize { get; set; }
    /// <summary>Gets or sets the analyser intern-cache size.</summary>
    public int? AnalyserInternCacheSize { get; set; }
    /// <summary>Gets or sets the maximum tokens per document.</summary>
    public int? MaxTokensPerDocument { get; set; }
    /// <summary>Gets or sets the token-budget policy.</summary>
    public TokenBudgetPolicy? TokenBudgetPolicy { get; set; }
    /// <summary>Gets or sets the tiered merge threshold.</summary>
    public int? MergeThreshold { get; set; }
    /// <summary>Gets or sets the segment count at which merge throttling starts.</summary>
    public int? MergeThrottleSegments { get; set; }
    /// <summary>Gets or sets the maximum concurrent background merges.</summary>
    public int? MaxConcurrentMerges { get; set; }
    /// <summary>Gets or sets the pending merge-byte limit.</summary>
    public long? MaxPendingMergeBytes { get; set; }
    /// <summary>Gets or sets whether vectors are normalised at index time.</summary>
    public bool? NormaliseVectors { get; set; }
    /// <summary>Gets or sets the vector quantisation strategy.</summary>
    public VectorQuantisation? VectorQuantisation { get; set; }
    /// <summary>Gets or sets whether HNSW graphs are built during flush.</summary>
    public bool? BuildHnswOnFlush { get; set; }
    /// <summary>Gets or sets the optional HNSW seed. Null clears the override.</summary>
    public long? HnswSeed { get; set; }
    /// <summary>Gets or sets whether sequence numbers are tracked.</summary>
    public bool? TrackSequenceNumbers { get; set; }
    /// <summary>Gets or sets whether soft deletes are enabled.</summary>
    public bool? SoftDeletesEnabled { get; set; }
    /// <summary>Gets or sets the soft-delete retention period in seconds.</summary>
    public double? SoftDeleteRetentionSeconds { get; set; }
    /// <summary>Gets or sets a factory for a fresh deletion policy per writer configuration. Null clears the override.</summary>
    public Func<IIndexDeletionPolicy>? DeletionPolicyFactory { get; set; }
    /// <summary>Gets or sets a factory for a merge policy per writer configuration. Null clears the override.</summary>
    public Func<IMergePolicy>? MergePolicyFactory { get; set; }
    /// <summary>Gets HNSW build defaults.</summary>
    public HnswBuildDefaultOptions Hnsw { get; }
    /// <summary>Gets analysis defaults and factories.</summary>
    public AnalysisDefaultOptions Analysis { get; }

    internal IndexWriterDefaultsSnapshot ToSnapshot()
        => new()
        {
            RamBufferSizeMB = ToOverride(RamBufferSizeMB),
            RamPerThreadHardLimitMB = ToOverride(RamPerThreadHardLimitMB),
            MaxConcurrentFlushes = ToOverride(MaxConcurrentFlushes),
            MaxBufferedDocs = ToOverride(MaxBufferedDocs),
            MaxQueuedDocs = ToOverride(MaxQueuedDocs),
            MaxQueuedBytes = ToOverride(MaxQueuedBytes),
            StorePayloads = ToOverride(StorePayloads),
            StoreTermVectors = ToOverride(StoreTermVectors),
            UseCompoundFile = ToOverride(UseCompoundFile),
            DurableCommits = ToOverride(DurableCommits),
            CompressionPolicy = ToOverride(CompressionPolicy),
            StoredFieldBlockSize = ToOverride(StoredFieldBlockSize),
            PostingsSkipInterval = ToOverride(PostingsSkipInterval),
            BKDMaxLeafSize = ToOverride(BKDMaxLeafSize),
            AnalyserInternCacheSize = ToOverride(AnalyserInternCacheSize),
            MaxTokensPerDocument = ToOverride(MaxTokensPerDocument),
            TokenBudgetPolicy = ToOverride(TokenBudgetPolicy),
            MergeThreshold = ToOverride(MergeThreshold),
            MergeThrottleSegments = ToOverride(MergeThrottleSegments),
            MaxConcurrentMerges = ToOverride(MaxConcurrentMerges),
            MaxPendingMergeBytes = ToOverride(MaxPendingMergeBytes),
            NormaliseVectors = ToOverride(NormaliseVectors),
            VectorQuantisation = ToOverride(VectorQuantisation),
            BuildHnswOnFlush = ToOverride(BuildHnswOnFlush),
            HnswSeed = HnswSeed.HasValue ? DefaultOverride<long?>.Set(HnswSeed) : DefaultOverride<long?>.Unset,
            TrackSequenceNumbers = ToOverride(TrackSequenceNumbers),
            SoftDeletesEnabled = ToOverride(SoftDeletesEnabled),
            SoftDeleteRetentionSeconds = ToOverride(SoftDeleteRetentionSeconds),
            DeletionPolicyFactory = DeletionPolicyFactory is null ? DefaultOverride<Func<IIndexDeletionPolicy>>.Unset : DefaultOverride<Func<IIndexDeletionPolicy>>.Set(DeletionPolicyFactory),
            MergePolicyFactory = MergePolicyFactory is null ? DefaultOverride<Func<IMergePolicy>>.Unset : DefaultOverride<Func<IMergePolicy>>.Set(MergePolicyFactory),
            Hnsw = Hnsw.ToSnapshot(),
            Analysis = Analysis.ToSnapshot(),
        };

    private static T? Get<T>(DefaultOverride<T> value) where T : struct
        => value.IsSet ? value.Value : null;

    private static T? GetReference<T>(DefaultOverride<T> value) where T : class
        => value.IsSet ? value.Value : null;
    private static DefaultOverride<T> ToOverride<T>(T? value) where T : struct
        => value.HasValue ? DefaultOverride<T>.Set(value.Value) : DefaultOverride<T>.Unset;
}

/// <summary>
/// Scalar HNSW build defaults. A fresh value-like build configuration is made per writer.
/// A null scalar clears that process-wide override.
/// </summary>
public sealed class HnswBuildDefaultOptions
{
    internal HnswBuildDefaultOptions(HnswBuildDefaultsSnapshot snapshot)
    {
        M = snapshot.M.IsSet ? snapshot.M.Value : null;
        EfConstruction = snapshot.EfConstruction.IsSet ? snapshot.EfConstruction.Value : null;
        M0 = snapshot.M0.IsSet ? snapshot.M0.Value : null;
    }

    /// <summary>Gets or sets the maximum neighbours above layer zero.</summary>
    public int? M { get; set; }
    /// <summary>Gets or sets the HNSW construction candidate set size.</summary>
    public int? EfConstruction { get; set; }
    /// <summary>Gets or sets the layer-zero neighbour limit. Zero uses twice <see cref="M"/>.</summary>
    public int? M0 { get; set; }

    internal HnswBuildDefaultsSnapshot ToSnapshot()
        => new()
        {
            M = ToOverride(M),
            EfConstruction = ToOverride(EfConstruction),
            M0 = ToOverride(M0),
        };

    private static DefaultOverride<int> ToOverride(int? value)
        => value.HasValue ? DefaultOverride<int>.Set(value.Value) : DefaultOverride<int>.Unset;
}

/// <summary>
/// Analysis defaults with factory-backed ownership and copied collections.
/// A null factory or stop-word list clears that process-wide override.
/// </summary>
public sealed class AnalysisDefaultOptions
{
    private readonly Dictionary<string, Func<IAnalyser>> _fieldAnalyserFactories = new(StringComparer.Ordinal);
    private readonly List<Func<ICharFilter>> _charFilterFactories = [];

    internal AnalysisDefaultOptions(AnalysisDefaultsSnapshot snapshot)
    {
        DefaultAnalyserFactory = snapshot.DefaultAnalyserFactory.IsSet ? snapshot.DefaultAnalyserFactory.Value : null;
        foreach (var pair in snapshot.FieldAnalyserFactories)
            _fieldAnalyserFactories.Add(pair.Key, pair.Value);
        StopWords = snapshot.StopWords.IsSet ? snapshot.StopWords.Value.ToArray() : null;
        _charFilterFactories.AddRange(snapshot.CharFilterFactories);
    }

    /// <summary>Gets or sets a factory that creates the default analyser for a writer. Null clears the override.</summary>
    public Func<IAnalyser>? DefaultAnalyserFactory { get; set; }

    /// <summary>Gets the field analyser factories. Use <see cref="ForField"/> to register one.</summary>
    public IReadOnlyDictionary<string, Func<IAnalyser>> FieldAnalyserFactories => _fieldAnalyserFactories;

    /// <summary>Gets or sets copied stop words for the default analyser. Null clears the override.</summary>
    public IReadOnlyList<string>? StopWords { get; set; }

    /// <summary>Gets the ordered character-filter factories.</summary>
    public IReadOnlyList<Func<ICharFilter>> CharFilterFactories => _charFilterFactories;

    /// <summary>Registers or replaces the analyser factory for a field.</summary>
    public void ForField(string fieldName, Func<IAnalyser> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(factory);
        _fieldAnalyserFactories[fieldName] = factory;
    }

    /// <summary>Adds a character-filter factory after the existing factories.</summary>
    public void AddCharFilter(Func<ICharFilter> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _charFilterFactories.Add(factory);
    }

    internal AnalysisDefaultsSnapshot ToSnapshot()
    {
        var fields = new Dictionary<string, Func<IAnalyser>>(_fieldAnalyserFactories.Count, StringComparer.Ordinal);
        foreach (var pair in _fieldAnalyserFactories)
        {
            ArgumentNullException.ThrowIfNull(pair.Value);
            fields.Add(pair.Key, pair.Value);
        }

        string[]? stopWords = null;
        if (StopWords is not null)
        {
            stopWords = StopWords.ToArray();
            for (int i = 0; i < stopWords.Length; i++)
                ArgumentNullException.ThrowIfNull(stopWords[i]);
        }

        var charFilters = _charFilterFactories.ToArray();
        for (int i = 0; i < charFilters.Length; i++)
            ArgumentNullException.ThrowIfNull(charFilters[i]);

        return new AnalysisDefaultsSnapshot
        {
            DefaultAnalyserFactory = DefaultAnalyserFactory is null
                ? DefaultOverride<Func<IAnalyser>>.Unset
                : DefaultOverride<Func<IAnalyser>>.Set(DefaultAnalyserFactory),
            FieldAnalyserFactories = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Func<IAnalyser>>(fields),
            StopWords = stopWords is null ? DefaultOverride<string[]>.Unset : DefaultOverride<string[]>.Set(stopWords),
            CharFilterFactories = Array.AsReadOnly(charFilters),
        };
    }
}

/// <summary>
/// Defaults applied to newly created searcher configurations.
/// A null scalar clears that process-wide override and restores the built-in value.
/// </summary>
public sealed class IndexSearcherDefaultOptions
{
    internal IndexSearcherDefaultOptions(IndexSearcherDefaultsSnapshot snapshot)
    {
        ParallelSearch = snapshot.ParallelSearch.IsSet ? snapshot.ParallelSearch.Value : null;
        MaxConcurrency = snapshot.MaxConcurrency.IsSet ? snapshot.MaxConcurrency.Value : null;
        EnableBlockMaxWand = snapshot.EnableBlockMaxWand.IsSet ? snapshot.EnableBlockMaxWand.Value : null;
        MaxCachedSegmentReaders = snapshot.MaxCachedSegmentReaders.IsSet ? snapshot.MaxCachedSegmentReaders.Value : null;
        QueryCache = new(snapshot.QueryCache);
    }

    /// <summary>Gets or sets whether parallel segment search is enabled.</summary>
    public bool? ParallelSearch { get; set; }
    /// <summary>Gets or sets the maximum search parallelism.</summary>
    public int? MaxConcurrency { get; set; }
    /// <summary>Gets or sets whether Block-Max WAND is enabled.</summary>
    public bool? EnableBlockMaxWand { get; set; }
    /// <summary>Gets or sets the bounded segment-reader cache size.</summary>
    public int? MaxCachedSegmentReaders { get; set; }
    /// <summary>Gets query-cache defaults.</summary>
    public QueryCacheDefaultOptions QueryCache { get; }

    internal IndexSearcherDefaultsSnapshot ToSnapshot()
        => new()
        {
            ParallelSearch = ToOverride(ParallelSearch),
            MaxConcurrency = ToOverride(MaxConcurrency),
            EnableBlockMaxWand = ToOverride(EnableBlockMaxWand),
            MaxCachedSegmentReaders = ToOverride(MaxCachedSegmentReaders),
            QueryCache = QueryCache.ToSnapshot(),
        };

    private static DefaultOverride<T> ToOverride<T>(T? value) where T : struct
        => value.HasValue ? DefaultOverride<T>.Set(value.Value) : DefaultOverride<T>.Unset;
}

/// <summary>
/// Defaults for the per-searcher query result cache. A null scalar clears that
/// process-wide override and restores the built-in value.
/// </summary>
public sealed class QueryCacheDefaultOptions
{
    internal QueryCacheDefaultOptions(QueryCacheDefaultsSnapshot snapshot)
    {
        Enabled = snapshot.Enabled.IsSet ? snapshot.Enabled.Value : null;
        MaxEntries = snapshot.MaxEntries.IsSet ? snapshot.MaxEntries.Value : null;
    }

    /// <summary>Gets or sets whether query caching is enabled.</summary>
    public bool? Enabled { get; set; }
    /// <summary>Gets or sets the maximum number of cached query results.</summary>
    public int? MaxEntries { get; set; }

    internal QueryCacheDefaultsSnapshot ToSnapshot()
        => new()
        {
            Enabled = ToOverride(Enabled),
            MaxEntries = ToOverride(MaxEntries),
        };

    private static DefaultOverride<T> ToOverride<T>(T? value) where T : struct
        => value.HasValue ? DefaultOverride<T>.Set(value.Value) : DefaultOverride<T>.Unset;
}

/// <summary>Defaults applied to newly created searcher managers. A null interval clears the override.</summary>
public sealed class SearcherManagerDefaultOptions
{
    internal SearcherManagerDefaultOptions(SearcherManagerDefaultsSnapshot snapshot)
    {
        RefreshInterval = snapshot.RefreshInterval.IsSet ? snapshot.RefreshInterval.Value : null;
    }

    /// <summary>Gets or sets the background refresh interval.</summary>
    public TimeSpan? RefreshInterval { get; set; }

    internal SearcherManagerDefaultsSnapshot ToSnapshot()
        => new()
        {
            RefreshInterval = ToOverride(RefreshInterval),
        };

    private static DefaultOverride<T> ToOverride<T>(T? value) where T : struct
        => value.HasValue ? DefaultOverride<T>.Set(value.Value) : DefaultOverride<T>.Unset;
}

/// <summary>Defaults applied to newly created JSON mapping options. A null value clears its override.</summary>
public sealed class JsonMappingDefaultOptions
{
    internal JsonMappingDefaultOptions(JsonMappingDefaultsSnapshot snapshot)
    {
        FieldNameSeparator = snapshot.FieldNameSeparator.IsSet ? snapshot.FieldNameSeparator.Value : null;
        MaxDepth = snapshot.MaxDepth.IsSet ? snapshot.MaxDepth.Value : null;
        StringFieldMaxLength = snapshot.StringFieldMaxLength.IsSet ? snapshot.StringFieldMaxLength.Value : null;
    }

    /// <summary>Gets or sets the separator for nested field names.</summary>
    public string? FieldNameSeparator { get; set; }
    /// <summary>Gets or sets the maximum mapped JSON depth.</summary>
    public int? MaxDepth { get; set; }
    /// <summary>Gets or sets the maximum length mapped as a string field.</summary>
    public int? StringFieldMaxLength { get; set; }

    internal JsonMappingDefaultsSnapshot ToSnapshot()
        => new()
        {
            FieldNameSeparator = FieldNameSeparator is null ? DefaultOverride<string>.Unset : DefaultOverride<string>.Set(FieldNameSeparator),
            MaxDepth = ToOverride(MaxDepth),
            StringFieldMaxLength = ToOverride(StringFieldMaxLength),
        };

    private static DefaultOverride<T> ToOverride<T>(T? value) where T : struct
        => value.HasValue ? DefaultOverride<T>.Set(value.Value) : DefaultOverride<T>.Unset;
}

/// <summary>
/// Scoring factories applied consistently to new writer and searcher configurations.
/// Null factories clear their process-wide overrides.
/// </summary>
public sealed class ScoringDefaultOptions
{
    private readonly Dictionary<string, Func<ISimilarity>> _perFieldFactories = new(StringComparer.Ordinal);

    internal ScoringDefaultOptions(ScoringDefaultsSnapshot snapshot)
    {
        SimilarityFactory = snapshot.SimilarityFactory.IsSet ? snapshot.SimilarityFactory.Value : null;
        foreach (var pair in snapshot.PerFieldSimilarityFactories)
            _perFieldFactories.Add(pair.Key, pair.Value);
    }

    /// <summary>Gets or sets a factory for the default scoring model. Null clears the override.</summary>
    public Func<ISimilarity>? SimilarityFactory { get; set; }

    /// <summary>Gets the per-field scoring factories.</summary>
    public IReadOnlyDictionary<string, Func<ISimilarity>> PerFieldSimilarityFactories => _perFieldFactories;

    /// <summary>Registers or replaces a per-field scoring factory.</summary>
    public void ForField(string fieldName, Func<ISimilarity> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        ArgumentNullException.ThrowIfNull(factory);
        _perFieldFactories[fieldName] = factory;
    }

    internal ScoringDefaultsSnapshot ToSnapshot()
    {
        var fields = new Dictionary<string, Func<ISimilarity>>(_perFieldFactories.Count, StringComparer.Ordinal);
        foreach (var pair in _perFieldFactories)
        {
            ArgumentNullException.ThrowIfNull(pair.Value);
            fields.Add(pair.Key, pair.Value);
        }

        return new ScoringDefaultsSnapshot
        {
            SimilarityFactory = SimilarityFactory is null
                ? DefaultOverride<Func<ISimilarity>>.Unset
                : DefaultOverride<Func<ISimilarity>>.Set(SimilarityFactory),
            PerFieldSimilarityFactories = new System.Collections.ObjectModel.ReadOnlyDictionary<string, Func<ISimilarity>>(fields),
        };
    }
}

/// <summary>
/// Factory-backed diagnostic defaults with explicit component ownership.
/// Null factories clear their process-wide overrides.
/// </summary>
public sealed class DiagnosticsDefaultOptions
{
    internal DiagnosticsDefaultOptions(DiagnosticsDefaultsSnapshot snapshot)
    {
        MetricsCollectorFactory = snapshot.MetricsCollectorFactory.IsSet ? snapshot.MetricsCollectorFactory.Value : null;
        SlowQueryLogFactory = snapshot.SlowQueryLogFactory.IsSet ? snapshot.SlowQueryLogFactory.Value : null;
        SearchAnalyticsFactory = snapshot.SearchAnalyticsFactory.IsSet ? snapshot.SearchAnalyticsFactory.Value : null;
    }

    /// <summary>Gets or sets a factory for a metrics collector per configuration graph.</summary>
    public Func<Diagnostics.IMetricsCollector>? MetricsCollectorFactory { get; set; }
    /// <summary>Gets or sets a factory for a slow-query log per searcher configuration graph.</summary>
    public Func<Diagnostics.SlowQueryLog?>? SlowQueryLogFactory { get; set; }
    /// <summary>Gets or sets a factory for analytics per searcher configuration graph.</summary>
    public Func<Diagnostics.SearchAnalytics?>? SearchAnalyticsFactory { get; set; }

    internal DiagnosticsDefaultsSnapshot ToSnapshot()
        => new()
        {
            MetricsCollectorFactory = MetricsCollectorFactory is null
                ? DefaultOverride<Func<Diagnostics.IMetricsCollector>>.Unset
                : DefaultOverride<Func<Diagnostics.IMetricsCollector>>.Set(MetricsCollectorFactory),
            SlowQueryLogFactory = SlowQueryLogFactory is null
                ? DefaultOverride<Func<Diagnostics.SlowQueryLog?>>.Unset
                : DefaultOverride<Func<Diagnostics.SlowQueryLog?>>.Set(SlowQueryLogFactory),
            SearchAnalyticsFactory = SearchAnalyticsFactory is null
                ? DefaultOverride<Func<Diagnostics.SearchAnalytics?>>.Unset
                : DefaultOverride<Func<Diagnostics.SearchAnalytics?>>.Set(SearchAnalyticsFactory),
        };
}

/// <summary>
/// Optional global query resource defaults. A null scalar clears its process-wide override.
/// Cancellation tokens and request filters remain local to each request.
/// </summary>
public sealed class SearchDefaultOptions
{
    private TimeSpan? _timeout;
    private bool _timeoutIsSet;

    internal SearchDefaultOptions(SearchDefaultsSnapshot snapshot)
    {
        MaxResultBytes = snapshot.MaxResultBytes.IsSet ? snapshot.MaxResultBytes.Value : null;
        _timeout = snapshot.Timeout.IsSet ? snapshot.Timeout.Value : null;
        _timeoutIsSet = snapshot.Timeout.IsSet;
        Hnsw = new(snapshot.Hnsw);
    }

    /// <summary>Gets or sets the default result accumulator byte budget.</summary>
    public long? MaxResultBytes { get; set; }
    /// <summary>Gets or sets the default query timeout. Null clears the override.</summary>
    public TimeSpan? Timeout
    {
        get => _timeout;
        set
        {
            _timeout = value;
            _timeoutIsSet = true;
        }
    }
    /// <summary>Gets HNSW search defaults.</summary>
    public HnswSearchDefaultOptions Hnsw { get; }

    internal SearchDefaultsSnapshot ToSnapshot()
        => new()
        {
            MaxResultBytes = MaxResultBytes.HasValue ? DefaultOverride<long>.Set(MaxResultBytes.Value) : DefaultOverride<long>.Unset,
            Timeout = _timeoutIsSet
                ? DefaultOverride<TimeSpan?>.Set(Timeout)
                : DefaultOverride<TimeSpan?>.Unset,
            Hnsw = Hnsw.ToSnapshot(),
        };
}

/// <summary>
/// Optional HNSW query-search defaults. TopK and filter state remain local to each request.
/// A null scalar clears that process-wide override.
/// </summary>
public sealed class HnswSearchDefaultOptions
{
    internal HnswSearchDefaultOptions(HnswSearchDefaultsSnapshot snapshot)
    {
        Ef = snapshot.Ef.IsSet ? snapshot.Ef.Value : null;
        MaxPostFilterRetries = snapshot.MaxPostFilterRetries.IsSet ? snapshot.MaxPostFilterRetries.Value : null;
    }

    /// <summary>Gets or sets the default HNSW candidate set size.</summary>
    public int? Ef { get; set; }
    /// <summary>Gets or sets the default post-filter retry count.</summary>
    public int? MaxPostFilterRetries { get; set; }

    internal HnswSearchDefaultsSnapshot ToSnapshot()
        => new()
        {
            Ef = ToOverride(Ef),
            MaxPostFilterRetries = ToOverride(MaxPostFilterRetries),
        };

    private static DefaultOverride<int> ToOverride(int? value)
        => value.HasValue ? DefaultOverride<int>.Set(value.Value) : DefaultOverride<int>.Unset;
}
