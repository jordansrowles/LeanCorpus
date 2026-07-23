using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Configuration for the IndexWriter.
/// </summary>
public sealed class IndexWriterConfig
{
    /// <summary>RAM buffer size in megabytes before an automatic flush.</summary>
    public double RamBufferSizeMB { get; set; } = 512.0;

    /// <summary>Hard memory limit for one DWPT before it must be flushed.</summary>
    public double RamPerThreadHardLimitMB { get; set; } = 256.0;

    /// <summary>Maximum number of segment flushes allowed to execute concurrently.</summary>
    public int MaxConcurrentFlushes { get; set; } = 1;

    /// <summary>Maximum number of buffered documents before an automatic flush.</summary>
    public int MaxBufferedDocs { get; set; } = 10_000;

    /// <summary>
    /// Maximum number of documents that can be queued for indexing before AddDocument blocks.
    /// Provides backpressure to prevent unbounded memory growth. Set to 0 to disable (not recommended).
    /// Default: 2 × MaxBufferedDocs.
    /// </summary>
    public int MaxQueuedDocs { get; set; } = 20_000;

    /// <summary>Maximum estimated bytes retained by queued asynchronous documents.</summary>
    public long MaxQueuedBytes { get; set; } = 512L * 1024 * 1024;

    /// <summary>Default analyser used for fields without a specific mapping.</summary>
    public IAnalyser DefaultAnalyser { get; set; } = new StandardAnalyser();

    /// <summary>Per-field analyser overrides. Key is the field name.</summary>
    public Dictionary<string, IAnalyser> FieldAnalysers { get; set; } = new();

    /// <summary>Deletion policy applied after each commit. Default: keep latest only.</summary>
    public IIndexDeletionPolicy DeletionPolicy { get; set; } = new KeepLatestCommitPolicy();

    /// <summary>Scoring model used by IndexSearcher. Default: BM25.</summary>
    public ISimilarity Similarity { get; set; } = Bm25Similarity.Instance;

    /// <summary>Whether to store per-position payloads in the postings.</summary>
    public bool StorePayloads { get; set; }

    /// <summary>Whether to store term vectors for text fields.</summary>
    public bool StoreTermVectors { get; set; }

    /// <summary>
    /// When <c>true</c> (default), <see cref="IndexWriter.Commit"/> flushes file contents and
    /// directory metadata to disk via <c>fsync</c> before and after the <c>segments_N</c> rename,
    /// guaranteeing the commit survives a power loss. Fsync failures are surfaced as
    /// <see cref="IOException"/> from <c>Commit</c>; the commit fails closed rather than reporting
    /// success on a host whose storage refused to flush. Disable only for write-heavy benchmarks
    /// where durability is not required.
    /// </summary>
    public bool DurableCommits { get; set; } = true;

    /// <summary>
    /// Compatibility guardrail applied when opening an existing index. Defaults to strict mode.
    /// </summary>
    public IndexOpenCompatibilityMode CompatibilityMode { get; set; } = IndexOpenCompatibilityMode.Strict;

    /// <summary>
    /// Compression algorithm for stored fields. Default: Deflate.
    /// Options: None, Deflate, Brotli, and any registered optional codec.
    /// </summary>
    public FieldCompressionPolicy CompressionPolicy { get; set; } = FieldCompressionPolicy.Deflate;

    /// <summary>
    /// Number of documents per stored field block. Larger blocks compress better but
    /// increase random-access cost. Default: 16.
    /// </summary>
    public int StoredFieldBlockSize { get; set; } = 16;

    /// <summary>
    /// Skip interval for postings lists. Every N-th doc ID gets a skip pointer for O(log N)
    /// advance. Must be consistent between write and merge paths. Default: 128.
    /// </summary>
    public int PostingsSkipInterval { get; set; } = 128;

    /// <summary>
    /// Segment count threshold that triggers a tiered merge. When the number of segments
    /// at a given size tier reaches this value, the smallest are merged. Default: 10.
    /// When <see cref="MergePolicy"/> is set to a non-default value, this property is ignored.
    /// </summary>
    public int MergeThreshold
    {
        get => _mergeThreshold;
        set
        {
            _mergeThreshold = value;
            if (!_mergePolicyExplicitlySet)
                _mergePolicy = new TieredMergePolicy(value);
        }
    }

    /// <summary>
    /// The merge policy used to select segments for merging. Defaults to
    /// <see cref="TieredMergePolicy"/> with the configured <see cref="MergeThreshold"/>.
    /// Set to <see cref="NoMergePolicy.Instance"/> to disable automatic merging.
    /// </summary>
    public IMergePolicy MergePolicy
    {
        get => _mergePolicy;
        set
        {
            _mergePolicy = value ?? throw new ArgumentNullException(nameof(value));
            _mergePolicyExplicitlySet = true;
        }
    }

    private int _mergeThreshold = 10;
    private IMergePolicy _mergePolicy = new TieredMergePolicy(10);
    private bool _mergePolicyExplicitlySet;

    /// <summary>
    /// Maximum number of point values in a BKD tree leaf node. Smaller leaves give faster
    /// range queries at the cost of larger index files. Default: 512.
    /// </summary>
    public int BKDMaxLeafSize { get; set; } = 512;

    /// <summary>
    /// Maximum number of entries in the StandardAnalyser token intern cache.
    /// Larger caches reduce per-token string allocation for repeated terms. Default: 4096.
    /// </summary>
    public int AnalyserInternCacheSize { get; set; } = 4096;

    /// <summary>
    /// Custom stop words for the default StandardAnalyser. When <see langword="null"/>,
    /// <see cref="Analysis.StopWords.English"/> (the classic 33-word English list) is used.
    /// Set to <see cref="Analysis.StopWords.EnglishExtended"/> for more aggressive filtering,
    /// or pass an empty list to disable stop word removal entirely.
    /// </summary>
    public IReadOnlyList<string>? StopWords { get; set; }

    /// <summary>
    /// Metrics collector for flush, merge, and commit latency tracking.
    /// Default: <see cref="Diagnostics.NullMetricsCollector"/> (no-op).
    /// </summary>
    public Diagnostics.IMetricsCollector Metrics { get; set; } = Diagnostics.NullMetricsCollector.Instance;

    /// <summary>
    /// Character-level filters applied to text before tokenisation.
    /// Runs in order before the analyser. Default: empty (no char filters).
    /// </summary>
    public IReadOnlyList<ICharFilter> CharFilters { get; set; } = [];

    /// <summary>
    /// Maximum number of tokens allowed per text field per document.
    /// 0 means unlimited (no budget enforcement). Default: 0.
    /// </summary>
    public int MaxTokensPerDocument { get; set; }

    /// <summary>
    /// Action taken when a document exceeds <see cref="MaxTokensPerDocument"/>.
    /// Default: <see cref="TokenBudgetPolicy.Truncate"/>.
    /// </summary>
    public TokenBudgetPolicy TokenBudgetPolicy { get; set; } = TokenBudgetPolicy.Truncate;

    /// <summary>
    /// Optional schema defining per-field types and validation rules.
    /// When null (default), documents are accepted without schema validation.
    /// </summary>
    public IndexSchema? Schema { get; set; }

    /// <summary>
    /// Optional index-time sort order. When set, documents within each segment
    /// are physically reordered at flush time. Default: null (insertion order).
    /// </summary>
    public IndexSort? IndexSort { get; set; }

    /// <summary>
    /// Maximum number of unmerged segments before AddDocument schedules a background merge
    /// and blocks until it completes. Provides backpressure to prevent unbounded segment
    /// accumulation. Default: 0 (disabled).
    /// </summary>
    public int MergeThrottleSegments { get; set; }

    /// <summary>Maximum number of background merges allowed to run concurrently.</summary>
    public int MaxConcurrentMerges { get; set; } = 1;

    /// <summary>Pending merge bytes that trigger producer backpressure. Zero disables the byte limit.</summary>
    public long MaxPendingMergeBytes { get; set; } = 4L * 1024 * 1024 * 1024;

    /// <summary>
    /// Whether vector fields should be normalised (L2) at index time. When true, dot product
    /// equals cosine similarity, enabling cheaper search. Default: <c>true</c>.
    /// </summary>
    public bool NormaliseVectors { get; set; } = true;

    /// <summary>
    /// Quantisation strategy for vector fields. <see cref="Codecs.Vectors.VectorQuantisation.None"/> (default)
    /// stores raw float32 vectors. <see cref="Codecs.Vectors.VectorQuantisation.Int8"/> gives ~4× storage
    /// reduction with minimal recall loss. <see cref="Codecs.Vectors.VectorQuantisation.BBQ"/> gives ~32×
    /// reduction at some recall cost. Default: <see cref="Codecs.Vectors.VectorQuantisation.None"/>.
    /// </summary>
    public VectorQuantisation VectorQuantisation { get; set; } = VectorQuantisation.None;

    /// <summary>
    /// Build an HNSW graph for every vector field at flush time. Disable to fall back to
    /// flat brute-force scan (useful for tiny indices where the build overhead outweighs benefit).
    /// Default: <c>true</c>.
    /// </summary>
    public bool BuildHnswOnFlush { get; set; } = true;

    /// <summary>HNSW build configuration applied to every vector field. See <see cref="Codecs.Hnsw.HnswBuildConfig"/>.</summary>
    public Codecs.Hnsw.HnswBuildConfig HnswBuildConfig { get; set; } = new();

    /// <summary>
    /// Optional deterministic seed for HNSW graph construction. When null, a random seed is generated
    /// per segment and persisted into the <c>.hnsw</c> file. Set explicitly for reproducible builds.
    /// </summary>
    public long? HnswSeed { get; set; }

    /// <summary>
    /// When <c>true</c>, each document is assigned a monotonically-increasing sequence number
    /// and the per-segment sequence number range is persisted in segment metadata.
    /// Default: <c>false</c> (off for backward compatibility).
    /// </summary>
    public bool TrackSequenceNumbers { get; set; }

    /// <summary>
    /// When <c>true</c>, soft-deleted documents are retained on disk until
    /// <see cref="SoftDeleteRetentionSeconds"/> elapses. The soft-delete timestamp is written
    /// alongside the live-docs bitmap in the <c>.del</c> file. Default: <c>false</c>.
    /// </summary>
    public bool SoftDeletesEnabled { get; set; }

    /// <summary>
    /// Minimum number of seconds to retain soft-deleted documents before they are eligible
    /// for physical reclamation during a merge. Only used when <see cref="SoftDeletesEnabled"/>
    /// is <c>true</c>. Default: 86400 (24 hours).
    /// </summary>
    public double SoftDeleteRetentionSeconds { get; set; } = 86400.0;

    /// <summary>
    /// Validates that property values are individually sensible and mutually consistent.
    /// Called by <see cref="IndexWriter"/> at construction time so that misconfiguration
    /// is surfaced before any state is set up.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when a property is invalid.</exception>
    internal void Validate()
    {
        if (RamBufferSizeMB < 0)
            throw new ArgumentException("RamBufferSizeMB must not be negative.", nameof(RamBufferSizeMB));

        if (RamPerThreadHardLimitMB <= 0)
            throw new ArgumentException("RamPerThreadHardLimitMB must be positive.", nameof(RamPerThreadHardLimitMB));

        if (MaxConcurrentFlushes < 1)
            throw new ArgumentException("MaxConcurrentFlushes must be at least 1.", nameof(MaxConcurrentFlushes));

        if (MaxBufferedDocs < 0)
            throw new ArgumentException("MaxBufferedDocs must not be negative.", nameof(MaxBufferedDocs));

        if (RamBufferSizeMB <= 0.0 && MaxBufferedDocs == 0)
            throw new ArgumentException(
                "At least one flush trigger must be configured. Set RamBufferSizeMB > 0 or MaxBufferedDocs > 0.");

        if (MaxQueuedDocs < 0)
            throw new ArgumentException("MaxQueuedDocs must not be negative.", nameof(MaxQueuedDocs));

        if (MaxQueuedBytes < 0)
            throw new ArgumentException("MaxQueuedBytes must not be negative.", nameof(MaxQueuedBytes));

        if (StoredFieldBlockSize < 1)
            throw new ArgumentException("StoredFieldBlockSize must be at least 1.", nameof(StoredFieldBlockSize));

        if (PostingsSkipInterval < 1)
            throw new ArgumentException("PostingsSkipInterval must be at least 1.", nameof(PostingsSkipInterval));

        if (BKDMaxLeafSize < 2)
            throw new ArgumentException("BKDMaxLeafSize must be at least 2.", nameof(BKDMaxLeafSize));

        if (AnalyserInternCacheSize < 0)
            throw new ArgumentException("AnalyserInternCacheSize must not be negative.", nameof(AnalyserInternCacheSize));

        if (MaxTokensPerDocument < 0)
            throw new ArgumentException("MaxTokensPerDocument must not be negative.", nameof(MaxTokensPerDocument));

        if (MergeThrottleSegments < 0)
            throw new ArgumentException("MergeThrottleSegments must not be negative.", nameof(MergeThrottleSegments));

        if (MaxConcurrentMerges < 1)
            throw new ArgumentException("MaxConcurrentMerges must be at least 1.", nameof(MaxConcurrentMerges));

        if (MaxPendingMergeBytes < 0)
            throw new ArgumentException("MaxPendingMergeBytes must not be negative.", nameof(MaxPendingMergeBytes));

        if (MergeThreshold < 2)
            throw new ArgumentException("MergeThreshold must be at least 2.", nameof(MergeThreshold));

        if (SoftDeletesEnabled && SoftDeleteRetentionSeconds <= 0)
            throw new ArgumentException(
                "SoftDeleteRetentionSeconds must be positive when SoftDeletesEnabled is true.",
                nameof(SoftDeleteRetentionSeconds));

        if (!Codecs.StoredFields.CompressionCodecRegistry.TryGet((byte)CompressionPolicy, out _))
            throw new ArgumentException(
                $"No compression codec is registered for policy '{CompressionPolicy}'. " +
                "Install the matching compression package or register a codec before opening the writer.",
                nameof(CompressionPolicy));
    }
}
