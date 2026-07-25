# Async indexing

`IndexWriter` has async overloads so the calling thread stays responsive during disk I/O.

## AddDocumentAsync

```csharp
await writer.AddDocumentAsync(doc);
```

Behaves identically to `AddDocument`. Internally queues work to the thread pool; the document goes through the same `DocumentsWriterPerThread` pipeline.

## CommitAsync

```csharp
await writer.CommitAsync();
```

Flushes and prepares a new commit point asynchronously. After the task completes, the commit is durable and visible to new searchers after a refresh.

## Streaming documents

```csharp
var documents = GetDocumentsAsync(cancellationToken);
await writer.AddDocumentsAsync(documents, batchSize: 256);
```

Pulls documents in batches, flushes each batch, respects the cancellation token. Committed batches are retained if the source later faults.

## Cancellation

```csharp
var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
await writer.AddDocumentsAsync(documents, cancellationToken: cts.Token);
```

Cancellation stops further ingestion and skips the final commit. Documents already flushed to a segment are recoverable after the next `CommitAsync`.

## Backpressure

`AddDocumentsAsync` reads from the enumerable on the calling thread. If the producer outpaces the writer, memory grows. Use `Channel<LeanDocument>` in the producer to cap in-flight documents.

`batchSize` (default 256) trades off flush frequency against recoverable work. Smaller batches reduce lost work on cancellation at the cost of more frequent flushes.

## See also

- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.AddDocumentAsync%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.CommitAsync%2A>
- <xref:Rowles.LeanCorpus.Index.Indexer.IndexWriter.AddDocumentsAsync%2A>
