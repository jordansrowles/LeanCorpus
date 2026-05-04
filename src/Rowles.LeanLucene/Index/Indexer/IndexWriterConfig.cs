using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;

namespace Rowles.LeanLucene.Index.Indexer;

/// <summary>
/// Configuration for the IndexWriter.
/// </summary>
public sealed class IndexWriterConfig
{
    /// <summary>RAM buffer size in megabytes before an automatic flush.</summary>
    public double RamBufferSizeMB { get; set; } = 256.0;

    /// <summary>Maximum number of buffered documents before an automatic flush.</summary>
    public int MaxBufferedDocs { get; set; } = 10_000;

    /// <summary>
    /// Maximum number of documents that can be queued for indexing before AddDocument blocks.
    /// Provides backpressure to prevent unbounded memory growth. Set to 0 to disable (not recommended).
    /// Default: 2 × MaxBufferedDocs.
    /// </summary>
    public int MaxQueuedDocs { get; set; } = 20_000;

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
    /// Compression algorithm for stored fields. Default: LZ4 (fast decompression).
    /// Options: None, Lz4, Zstandard.
    /// </summary>
    public FieldCompressionPolicy CompressionPolicy { get; set; } = FieldCompressionPolicy.Lz4;

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
    /// </summary>
    public int MergeThreshold { get; set; } = 10;

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
    /// <see cref="Analysis.StopWords.English"/> (the classic 33-word Lucene-compatible list) is used.
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
    /// Maximum number of unmerged segments before AddDocument blocks until a merge completes.
    /// Provides backpressure to prevent unbounded segment accumulation. Default: 0 (disabled).
    /// </summary>
    public int MergeThrottleSegments { get; set; }

    /// <summary>
    /// Whether vector fields should be normalised (L2) at index time. When true, dot product
    /// equals cosine similarity, enabling cheaper search. Default: <c>true</c>.
    /// </summary>
    public bool NormaliseVectors { get; set; } = true;

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
}
