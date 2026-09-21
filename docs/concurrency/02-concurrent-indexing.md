# Concurrent indexing

`IndexWriter` is thread-safe for `AddDocument`. For high-throughput ingest, use the concurrent path.

## Multi-threaded add

```csharp
var docs = LoadDocuments();
Parallel.ForEach(docs, doc => writer.AddDocument(doc));
writer.Commit();
```

## Concurrent pipeline

```csharp
writer.AddDocumentsConcurrent(docs);
```

`MaxQueuedDocs` (default `20_000`) caps the in-flight queue. `MaxBufferedDocs` and `RamBufferSizeMB` govern when a flush fires.

Each DWPT keeps its own postings arena and other document-local state. When a
threshold is reached, the writer freezes and detaches that state as an owned
snapshot, then reuses the DWPT with fresh buffers. The physical flush can run
while producers continue, but the snapshot remains responsible for returning
all pooled postings resources on both success and failure.

## Pre-warm DWPT pool

DWPTs are created lazily. Pre-warm for consistent latency:

```csharp
writer.InitialiseDwptPool();
```

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.AddDocumentsConcurrent%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig>
