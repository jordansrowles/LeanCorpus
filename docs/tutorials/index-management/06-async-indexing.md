# Async indexing

`IndexWriter` exposes async overloads for indexing operations so you can keep the
calling thread responsive during disk I/O.

## AddDocumentAsync

```csharp
await writer.AddDocumentAsync(doc);
```

Behaves identically to `AddDocument` but yields the thread during the synchronous
portions of the write. Internally it queues work to the thread pool; the document is
still written through the same `DocumentsWriterPerThread` pipeline.

## CommitAsync

```csharp
await writer.CommitAsync();
```

Prepares a new commit point and flushes to disk asynchronously. After the returned
task completes, the commit is durable and visible to new searchers that open after a
refresh.

## AddDocumentsAsync (streaming)

`AddDocumentsAsync` streams documents from an `IAsyncEnumerable<LeanDocument>` using
bounded batches:

```csharp
var documents = GetDocumentsAsync(cancellationToken);
await writer.AddDocumentsAsync(documents, batchSize: 256);
```

The method pulls documents in batches, flushes each batch, and respects the supplied
cancellation token. When the enumerable completes it writes the final batch
automatically. Committed batches are retained if the source later faults.

## Cancellation

Every async method accepts an optional `CancellationToken`:

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await writer.AddDocumentsAsync(documents, batchSize: 256, cancellationToken: cts.Token);
```

Cancellation stops further document ingestion and skips the final commit. Documents
already flushed to a segment are recoverable after the next `CommitAsync`.

## Backpressure

`AddDocumentsAsync` reads from the enumerable on the calling thread. If the producer
is faster than the writer can flush, memory grows. Use `Channel<LeanDocument>` or
`System.Threading.Channels` in the producer to cap the number of in-flight documents
when the source outpaces the sink.

The `batchSize` parameter (default 256) controls the trade-off between flush
frequency and recoverable work. Smaller batches reduce lost work on cancellation at
the cost of more frequent flushes.

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.AddDocumentAsync%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.CommitAsync%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.AddDocumentsAsync%2A>
