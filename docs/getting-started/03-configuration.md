# Writer configuration

`IndexWriterConfig` controls buffering, merging, compression, and analysis.

## Common setup

```csharp
var config = new IndexWriterConfig
{
    DefaultAnalyser = new StandardAnalyser(),
    RamBufferSizeMB = 512.0,
    MaxBufferedDocs = 10_000,
    MaxQueuedDocs   = 20_000,
    MergeThreshold  = 10,
    DurableCommits  = true,
    CompressionPolicy = FieldCompressionPolicy.Deflate,
    StoredFieldBlockSize = 16,
};
```

## Defaults

| Setting | Default | What it does |
|---|---|---|
| `RamBufferSizeMB` | `512.0` | Memory buffer before flush |
| `MaxBufferedDocs` | `10_000` | Doc count before flush |
| `MaxQueuedDocs` | `20_000` | Backpressure cap; `AddDocument` blocks past this |
| `DefaultAnalyser` | `StandardAnalyser` | Analyser for fields without a mapping |
| `Similarity` | `Bm25Similarity.Instance` | Scoring model |
| `DeletionPolicy` | `KeepLatestCommitPolicy` | Which old commits survive |
| `DurableCommits` | `true` | `fsync` before declaring commit successful |
| `CompressionPolicy` | `Deflate` | Stored field compression |
| `StoredFieldBlockSize` | `16` | Docs per compression block |
| `PostingsSkipInterval` | `128` | Postings skip-list frequency |
| `MergeThreshold` | `10` | Segment count that triggers a merge |
| `BKDMaxLeafSize` | `512` | BKD tree leaf capacity |
| `MaxTokensPerDocument` | `0` (unlimited) | Token cap per document |
| `TokenBudgetPolicy` | `Truncate` | What happens when the cap is hit |
| `StoreTermVectors` | `false` | Whether to persist term vectors |
| `Metrics` | `NullMetricsCollector.Instance` | Metrics backend |

## Process-wide defaults

Configure process-wide defaults once during application startup, before creating
writers, searchers, managers, or query options:

```csharp
LeanCorpusDefaults.Configure(options =>
{
    options.Codecs.Catalog = myCatalog;

    options.IndexWriter.RamBufferSizeMB = 256;
    options.IndexWriter.MaxBufferedDocs = 5_000;
    options.IndexWriter.UseCompoundFile = true;

    options.IndexSearcher.ParallelSearch = true;
    options.IndexSearcher.QueryCache.Enabled = true;

    options.SearcherManager.RefreshInterval =
        TimeSpan.FromMilliseconds(500);
});
```

These are defaults for future configuration objects. The precedence is:

```text
built-in -> process-wide default -> local configuration -> request option
```

An explicit value on `IndexWriterConfig`, `IndexSearcherConfig`,
`SearcherManagerConfig`, or a request options object wins. Existing
configurations and active components keep the values captured when they were
created. `LeanCorpusDefaults.Reset()` restores the built-in values for future
objects.

Writer defaults such as `CompressionPolicy`, `StoredFieldBlockSize`,
`PostingsSkipInterval`, `VectorQuantisation`, HNSW build parameters,
`TrackSequenceNumbers`, and `SoftDeletesEnabled` affect newly written segment
representation or persisted metadata. They do not replace the codec and
segment metadata used to read an existing index.

Factories create fresh analysis, policy, scoring, or diagnostic instances for
each receiving configuration. Stop-word lists and factory mappings are copied
when the defaults are published. A `SearcherManager` keeps its
factory-created slow-query log and analytics objects across searcher refreshes
and disposes a factory-created slow-query log when the manager is disposed.

Keep `Schema`, `IndexSort`, migration choices, destructive maintenance flags,
`CancellationToken`, `TopK`, and request filters local to the operation that
needs them.

## Backpressure and merge throttling

`MaxQueuedDocs` (default 20,000) caps the number of documents waiting to be flushed. When the queue is full, `AddDocument` blocks until a flush frees space. Prevents out-of-memory conditions under sustained write load.

`MergeThrottleSegments` (default 0, disabled) blocks writes when the segment count exceeds the threshold. Set it to force merges to catch up before more documents are accepted, keeping the segment count bounded when indexing faster than merges can consolidate:

```csharp
var config = new IndexWriterConfig
{
    MergeThrottleSegments = 100,
};
```

## Field boosts and index sort

```csharp
var document = new LeanDocument();
document.Add(new TextField(
    "title",
    "A compact corpus",
    stored: true,
    boost: 3.0f));

var config = new IndexWriterConfig
{
    IndexSort = new IndexSort(
        SortField.Int64(
            "publishedAt",
            descending: true)),
};
```

Field boosts belong to field values and are persisted in segment norms. `IndexSort` controls physical document order for newly written and merged segments.

## Schema validation

```csharp
var schema = new IndexSchema { StrictMode = true }
    .Add(new FieldMapping("id",    FieldType.String) { IsRequired = true })
    .Add(new FieldMapping("title", FieldType.Text)   { IsRequired = true })
    .Add(new FieldMapping("price", FieldType.Numeric));

var config = new IndexWriterConfig { Schema = schema };
```

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig>
- <xref:Rowles.LeanCorpus.Codecs.StoredFields.FieldCompressionPolicy>
