# Writer configuration

<xref:Rowles.LeanLucene.Index.Indexer.IndexWriterConfig> exposes the knobs that affect
buffering, merging, compression, and analysis.

## Common settings

```csharp
var config = new IndexWriterConfig
{
    DefaultAnalyser = new StandardAnalyser(),
    RamBufferSizeMB = 256.0,
    MaxBufferedDocs = 10_000,
    MaxQueuedDocs   = 20_000,
    MergeThreshold  = 10,
    DurableCommits  = true,
    CompressionPolicy = FieldCompressionPolicy.Deflate,
    StoredFieldBlockSize = 16,
};
```

## Defaults

| Setting | Default |
|---|---|
| `RamBufferSizeMB` | `256.0` |
| `MaxBufferedDocs` | `10_000` |
| `MaxQueuedDocs` | `20_000` |
| `DefaultAnalyser` | `StandardAnalyser` |
| `Similarity` | `Bm25Similarity.Instance` |
| `DeletionPolicy` | `KeepLatestCommitPolicy` |
| `DurableCommits` | `true` |
| `CompressionPolicy` | `Deflate` |
| `StoredFieldBlockSize` | `16` |
| `PostingsSkipInterval` | `128` |
| `MergeThreshold` | `10` |
| `BKDMaxLeafSize` | `512` |
| `MaxTokensPerDocument` | `0` (unlimited) |
| `TokenBudgetPolicy` | `Truncate` |
| `Metrics` | `NullMetricsCollector.Instance` |

## What each knob affects

- **`RamBufferSizeMB` / `MaxBufferedDocs`**: in-memory buffer before a flush.
- **`MergeThreshold`**: number of segments before a background merge runs.
- **`DurableCommits`**: when true, fsyncs before declaring a commit successful.
- **`Schema`**: optional <xref:Rowles.LeanLucene.Index.Indexer.IndexSchema>; rejects
  bad documents at `AddDocument` time.

## See also

- <xref:Rowles.LeanLucene.Index.Indexer.IndexWriterConfig>
- <xref:Rowles.LeanLucene.Codecs.StoredFields.FieldCompressionPolicy>
