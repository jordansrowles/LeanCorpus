# Concurrent indexing

`IndexWriter` is thread-safe for `AddDocument`. For high-throughput ingest, the
concurrent path uses per-thread document writers (DWPTs).

## Multi-threaded add

```csharp
var docs = LoadDocuments();

Parallel.ForEach(docs, doc => writer.AddDocument(doc));

writer.Commit();
```

## Tuning

`IndexWriterConfig.MaxQueuedDocs` (default `20_000`) caps the in-flight queue per
DWPT. `MaxBufferedDocs` and `RamBufferSizeMB` together govern when a flush fires.

## Lock-free fast path

For workloads that never need to reorder buffered docs, `AddDocumentLockFree`
bypasses the global lock. Use through the higher-level helper:

```csharp
writer.AddDocumentsConcurrent(docs);
```

This explicitly requests the concurrent ingestion pipeline.

## Initialising the DWPT pool

DWPTs are created lazily. To pre-warm:

```csharp
writer.InitialiseDwptPool();
```

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.AddDocumentsConcurrent%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig>
