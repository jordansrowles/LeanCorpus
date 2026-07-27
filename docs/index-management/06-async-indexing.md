# Async indexing

`IndexWriter` provides asynchronous ingestion for applications that must avoid blocking while waiting for the writer queue and backpressure. Indexing semantics, validation, flushes, merges, and commits are shared with the synchronous path.

## One document

```csharp
await writer.AddDocumentAsync(document, cancellationToken);
```

The document is validated, written to the writer's bounded asynchronous command channel, and completed after the command has passed through the normal documents-writer-per-thread pipeline. It is not implemented as one `Task.Run` per document.

Use synchronous `AddDocument` in a dedicated indexing worker when the caller is already allowed to block. Use the async form in request, stream, or channel consumers that need cooperative backpressure.

## A known batch

```csharp
IReadOnlyList<LeanDocument> batch = BuildBatch();
await writer.AddDocumentsAsync(batch, cancellationToken);
```

The batch is validated before it is queued. If backpressure is enabled and the batch exceeds `MaxQueuedDocs`, LeanCorpus submits its documents individually instead of attempting to reserve an impossible batch.

## Stream documents

```csharp
await writer.AddDocumentsAsync(
    GetDocumentsAsync(cancellationToken),
    batchSize: 256,
    cancellationToken);
```

The effective batch size is the smaller of the requested size and `MaxQueuedDocs` when that limit is enabled. The writer consumes the `IAsyncEnumerable` with cancellation and sends each full batch through the same asynchronous channel.

This method does not commit each batch. Call `CommitAsync` according to the application's durability and visibility policy.

## Document blocks

```csharp
await writer.AddDocumentBlockAsync(
    [childOne, childTwo, parent],
    cancellationToken);
```

A block requires at least one child and one final parent. It is rejected when it exceeds `MaxQueuedDocs` under bounded backpressure because splitting it would break block-join adjacency.

## Commit

```csharp
await writer.CommitAsync(cancellationToken);
```

`CommitAsync` runs the synchronous commit manager on a thread-pool worker. Completion means the normal commit contract has completed, including durability when `DurableCommits` is enabled. A `SearcherManager` still needs to refresh before its readers observe the new generation.

## Cancellation and failures

Cancellation can stop waiting to enqueue, stream enumeration, or a commit before its work begins. Work already accepted by the writer may have changed in-memory or flushed segment state even when a later operation throws.

An indexing call does not imply a commit. On exception:

1. record the source checkpoint or failed document;
2. decide whether to retry that unit;
3. commit only the accepted work the application wants to retain;
4. use `Rollback()` when the whole uncommitted writer session must be abandoned.

Do not blindly retry a batch unless the source operation is idempotent or documents have stable update keys.

## Parallel producers

Multiple producers may call the writer, but increasing caller parallelism beyond flush and storage capacity only increases queue pressure. Start with a small number of producers and observe `MaxQueuedDocs`, `MaxQueuedBytes`, flush latency, and pending merge bytes.

Use an application `Channel<LeanDocument>` when source acquisition itself needs a separate bound or prioritisation policy. The writer already provides its own downstream bound.

## Sync or async

| Situation | Prefer |
|---|---|
| Dedicated worker thread, simple batch job | Synchronous methods |
| ASP.NET request or asynchronous message consumer | Async methods |
| `IAsyncEnumerable` source | `AddDocumentsAsync` |
| Atomic child and parent adjacency | `AddDocumentBlock` or `AddDocumentBlockAsync` |

Async improves caller scheduling, not codec or storage throughput by itself. Measure end-to-end indexing rate and allocation before increasing concurrency.
