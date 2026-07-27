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
