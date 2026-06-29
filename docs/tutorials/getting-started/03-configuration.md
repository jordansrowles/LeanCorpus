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

## Per-field boosts and sort

```csharp
var config = new IndexWriterConfig
{
    FieldBoosts = new Dictionary<string, float> { ["title"] = 3.0f },
    IndexSort = new IndexSort(new SortField("publishedAt", SortFieldType.Long, descending: true)),
};
```

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
