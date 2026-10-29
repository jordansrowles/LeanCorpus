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

`MaxQueuedDocs` (default `20_000`) caps the number of documents admitted into the
DWPT buffers. When the cap is reached, producers trigger a DWPT flush snapshot and
wait for an admission slot to be released. The slot is released when the snapshot
is detached, before its physical flush finishes. Async writes also use a bounded
command channel sized by this value when it is positive, or by its default
capacity of 4,096 when the document limit is disabled. `MaxBufferedDocs`, `RamBufferSizeMB` and
`MaxQueuedBytes` provide separate automatic-flush limits. A capacity of one can
therefore cause one-document flush snapshots under concurrent single-document adds.

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

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.AddDocumentsConcurrent(System.Collections.Generic.IReadOnlyList{Rowles.LeanCorpus.Document.LeanDocument})>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriterConfig>
