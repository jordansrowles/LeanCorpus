# Configuration reference

This page collects the main indexing, searching, refresh, and per-query settings. Defaults shown here are the values used by a newly constructed configuration object.

Start with defaults and change a setting only for a measured workload or an explicit operational requirement.

## `IndexWriterConfig`

### Buffering and backpressure

| Setting | Default | Guidance |
|---|---:|---|
| `RamBufferSizeMB` | `512` | Flush target across buffered indexing work. Lower values reduce peak memory and create more segments. |
| `RamPerThreadHardLimitMB` | `256` | Hard limit for one documents-writer-per-thread buffer. |
| `MaxConcurrentFlushes` | `1` | Concurrent segment flushes. More concurrency increases I/O and temporary memory. |
| `MaxBufferedDocs` | `10,000` | Document-count flush trigger. |
| `MaxQueuedDocs` | `20,000` | Backpressure limit. `0` disables this guard and is not recommended. |
| `MaxQueuedBytes` | `512 MiB` | Byte-oriented queue guard for unusually large documents. |
| `MaxTokensPerDocument` | `0` | Token budget, with `0` meaning unlimited. |
| `TokenBudgetPolicy` | `Truncate` | Truncate or reject a document when its token budget is exceeded. |

The first reached flush trigger wins. Queue limits are backpressure controls, not extra buffers guaranteed to be filled.

### Analysis and schema

| Setting | Default | Guidance |
|---|---|---|
| `DefaultAnalyser` | `StandardAnalyser` | Used when no field analyser is configured. |
| `FieldAnalysers` | empty | Per-field analyser overrides. |
| `CharFilters` | empty | Character filters applied before tokenisation. |
| `StopWords` | `null` | Optional index-wide stop-word set. |
| `AnalyserInternCacheSize` | `4,096` | Bound for analyser string interning. |
| `Schema` | `null` | Optional `IndexSchema`; set it when field validation is required. |

Index-time and query-time analysis must agree for exact term matching. A schema catches accidental type or option changes before they become mixed segment behaviour.

### Storage and scoring

| Setting | Default | Guidance |
|---|---:|---|
| `Similarity` | BM25 | Index-time scoring metadata and default search scoring model. |
| `CompressionPolicy` | `Deflate` | Stored-field block compression. |
| `StoredFieldBlockSize` | `16` | Documents per stored-field compression block. |
| `PostingsSkipInterval` | `128` | Skip-data interval for postings. |
| `StorePayloads` | `false` | Persists token payloads where analysis supplies them. |
| `StoreTermVectors` | `false` | Persists per-document term vectors. |
| `NormaliseVectors` | `true` | Normalises vectors at index time for cosine search. |
| `VectorQuantisation` | `None` | Optional vector storage quantisation. |
| `BuildHnswOnFlush` | `true` | Builds an HNSW graph for vector fields on flush. |
| `HnswBuildConfig` | library default | Controls graph connectivity and construction search. |
| `HnswSeed` | `null` | Optional deterministic graph-construction seed. |

Term vectors, payloads, vectors, and DocValues increase index size. Enable them only for features that consume them.

### Commits, merging, and compatibility

| Setting | Default | Guidance |
|---|---:|---|
| `DurableCommits` | `true` | Flushes file and directory metadata before commit success is reported. |
| `DeletionPolicy` | `KeepLatestCommitPolicy` | Retains only the latest unpinned commit. |
| `CompatibilityMode` | `Strict` | Refuses incompatible index formats. |
| `MergeThreshold` | `10` | Compatibility shorthand for the default tiered merge policy threshold. |
| `MergePolicy` | `TieredMergePolicy(10)` | Selects merge candidates. An explicitly assigned policy takes precedence over `MergeThreshold`. |
| `MergeThrottleSegments` | `0` | Segment-count write throttle, disabled at `0`. |
| `MaxConcurrentMerges` | `1` | Concurrent merge operations. |
| `MaxPendingMergeBytes` | `4 GiB` | Backpressure bound for scheduled merge work. |
| `BKDMaxLeafSize` | `512` | Maximum numeric points in a BKD leaf. |
| `IndexSort` | `null` | Optional physical segment sort. |
| `TrackSequenceNumbers` | `false` | Persists document sequence numbers for workflows that require them. |
| `SoftDeletesEnabled` | `false` | Enables soft-deletion metadata. |
| `SoftDeleteRetentionSeconds` | `86,400` | Retains soft-deleted documents for one day before merge reclamation. |

Changing an on-disk setting affects newly written segments. Existing segments retain their original format until merged or migrated.

## Process-wide defaults

`LeanCorpusDefaults` can supply optional defaults for configurations created later in
the same process. It does not alter an existing `IndexWriterConfig` or an active
`IndexWriter`, and an explicit property value on the configuration always wins.

```csharp
LeanCorpusDefaults.Configure(options =>
{
    options.IndexWriter.DurableCommits = false;
});

var config = new IndexWriterConfig(); // DurableCommits is false
var durable = new IndexWriterConfig { DurableCommits = true }; // explicit value wins
```

With no override, including after `LeanCorpusDefaults.Reset()`, the production
default remains `DurableCommits = true`. Configure process-wide defaults during
application startup before creating writers.

### Diagnostics

| Setting | Default | Guidance |
|---|---|---|
| `Metrics` | null collector | Receives indexing, flush, merge, and commit measurements. |

## `IndexSearcherConfig`

| Setting | Default | Guidance |
|---|---:|---|
| `Similarity` | BM25 | Search-time scoring model. Keep it compatible with indexed norms. |
| `CompatibilityMode` | `Strict` | Applies index-open format guardrails. |
| `ParallelSearch` | `false` | Searches segments in parallel. Useful for expensive multi-segment workloads, but adds scheduling overhead. |
| `MaxConcurrency` | `-1` | Automatic concurrency when parallel search is enabled. |
| `EnableQueryCache` | `false` | Caches complete `TopDocs` results by query fingerprint and result count. |
| `QueryCacheMaxEntries` | `1,024` | Soft entry cap. The current cache generation is replaced when the cap is exceeded. |
| `MaxCachedSegmentReaders` | `256` | Bound for lazily opened segment readers. |
| `EnableBlockMaxWand` | `false` | Enables score-bound skipping for supported top-N queries. |
| `Metrics` | null collector | Search metrics destination. |
| `SlowQueryLog` | `null` | Optional structured slow-query logger. |
| `SearchAnalytics` | `null` | Optional query analytics collector. |

The query cache is not an LRU and does not store per-segment filter bitsets. See [Query cache](../tips/02-query-cache.md).

## `SearcherManagerConfig`

| Setting | Default | Guidance |
|---|---:|---|
| `RefreshInterval` | `1 second` | Poll interval for newly committed generations. |
| `SearcherConfig` | new defaults | Applied to every replacement searcher. |
| `CompatibilityMode` | `Strict` | Checked before refresh opens commit metadata. |

Shorter refresh intervals improve visibility latency but increase metadata polling and searcher turnover. Use explicit leases around every acquired searcher.

## `SearchOptions`

| Setting | Default | Guidance |
|---|---:|---|
| `MaxResultBytes` | `long.MaxValue` | Approximate bound for retained result candidates. |
| `StreamResults` | `false` | Requests segment-order streaming rather than a fully collected global top-N. |
| `Timeout` | `null` | Optional wall-clock limit. Checked at documented safe points. |
| `CancellationToken` | `None` | Cooperative cancellation token. |

Convenience factories cover the common cases:

```csharp
var budgeted = SearchOptions.WithBudget(32 * 1024 * 1024);
var timed = SearchOptions.WithTimeout(TimeSpan.FromMilliseconds(250));
var bounded = SearchOptions.WithBudgetAndTimeout(
    32 * 1024 * 1024,
    TimeSpan.FromMilliseconds(250));

var cancellable = new SearchOptions
{
    MaxResultBytes = 32 * 1024 * 1024,
    Timeout = TimeSpan.FromMilliseconds(250),
    CancellationToken = cancellationToken,
};
```

Early termination sets `TopDocs.IsPartial`. Treat partial results as a deliberate API state, not a successful complete search.

## Related material

- [Writer configuration](03-configuration.md)
- [Resource controls](../searching/09-resource-controls.md)
- [Production deployment](../tips/04-production-deployment.md)
- [Validation and compatibility](../index-management/03-validation-recovery.md)
