using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    /// <summary>Adds one document asynchronously.</summary>
    /// <remarks>A token-budget rejection affects only this document and does not poison the writer.</remarks>
    public async ValueTask AddDocumentAsync(LeanDocument doc, CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            await AddDocumentAsyncOperationOwned(doc, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>Adds documents asynchronously in list order.</summary>
    /// <remarks>
    /// This operation is not atomic. If a document is rejected, documents accepted
    /// earlier in the list remain buffered and may be committed.
    /// </remarks>
    public async ValueTask AddDocumentsAsync(IReadOnlyList<LeanDocument> documents, CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            await AddDocumentsAsyncOperationOwned(documents, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>Adds a batch through bounded concurrent producer execution.</summary>
    /// <remarks>
    /// This is a throughput-oriented API. It preserves FIFO ordering with other async
    /// commands, but does not preserve input document-ID order within the batch.
    /// Cancellation stops admission before the batch command is accepted; accepted work
    /// remains owned by the writer.
    /// </remarks>
    public async ValueTask AddDocumentsConcurrentAsync(
        IReadOnlyList<LeanDocument> documents,
        CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            await AddDocumentsConcurrentAsyncOperationOwned(documents, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>Adds documents from an asynchronous sequence in bounded batches.</summary>
    /// <remarks>
    /// The sequence is consumed in order. If a document is rejected, documents accepted
    /// earlier in the sequence remain buffered and may be committed.
    /// </remarks>
    public async ValueTask AddDocumentsAsync(
        IAsyncEnumerable<LeanDocument> documents,
        int batchSize = 256,
        CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(documents);

            int effectiveBatchSize = GetEffectiveAsyncBatchSize(batchSize);
            var batch = new List<LeanDocument>(effectiveBatchSize);

            await foreach (var document in documents.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                batch.Add(document);
                if (batch.Count < effectiveBatchSize)
                    continue;

                await AddDocumentsAsyncOperationOwned(batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }

            if (batch.Count > 0)
                await AddDocumentsAsyncOperationOwned(batch, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>Adds an asynchronous sequence through bounded concurrent batches.</summary>
    /// <remarks>
    /// Input consumption stops when cancellation is requested. Each accepted batch is
    /// writer-owned and may assign document IDs in an order different from the input.
    /// </remarks>
    public async ValueTask AddDocumentsConcurrentAsync(
        IAsyncEnumerable<LeanDocument> documents,
        int batchSize = 256,
        CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(documents);

            int effectiveBatchSize = GetEffectiveAsyncBatchSize(batchSize);
            var batch = new List<LeanDocument>(effectiveBatchSize);

            await foreach (var document in documents.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                batch.Add(document);
                if (batch.Count < effectiveBatchSize)
                    continue;

                await AddDocumentsConcurrentAsyncOperationOwned(batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }

            if (batch.Count > 0)
                await AddDocumentsConcurrentAsyncOperationOwned(batch, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    private async ValueTask AddDocumentsConcurrentAsyncOperationOwned(
        IReadOnlyList<LeanDocument> documents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0) return;
        ValidateDocuments(documents);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cmd = new AsyncWriteCommand(documents, AsyncWriteKind.ConcurrentBatch, tcs);
        await EnqueueAsyncWrite(cmd, cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    private async ValueTask AddDocumentAsyncOperationOwned(LeanDocument doc, CancellationToken cancellationToken)
    {
        ValidateDocument(doc);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cmd = new AsyncWriteCommand(doc, AsyncWriteKind.Single, tcs);
        await EnqueueAsyncWrite(cmd, cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    private async ValueTask AddDocumentsAsyncOperationOwned(
        IReadOnlyList<LeanDocument> documents,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
            return;
        ValidateDocuments(documents);

        if (_backpressureSemaphore is null || documents.Count <= _config.MaxQueuedDocs)
        {
            await EnqueueBatchAndWait(documents, cancellationToken).ConfigureAwait(false);
            return;
        }

        // An oversized list is admitted as ordered chunks of at most MaxQueuedDocs documents.
        // Each chunk completes before the next is admitted, so this caller never has more
        // writer-owned queued documents than a batch at the limit would. Cancellation stops
        // admission between chunks; admitted chunks remain writer-owned and complete.
        int chunkSize = _config.MaxQueuedDocs;
        for (int start = 0; start < documents.Count; start += chunkSize)
        {
            int count = Math.Min(chunkSize, documents.Count - start);
            await EnqueueBatchAndWait(new DocumentListChunk(documents, start, count), cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask EnqueueBatchAndWait(IReadOnlyList<LeanDocument> documents, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cmd = new AsyncWriteCommand(documents, AsyncWriteKind.Batch, tcs);
        await EnqueueAsyncWrite(cmd, cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    /// <summary>A read-only window over a caller-owned document list; no documents are copied.</summary>
    private sealed class DocumentListChunk(IReadOnlyList<LeanDocument> source, int start, int count) : IReadOnlyList<LeanDocument>
    {
        public int Count => count;

        public LeanDocument this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
                return source[start + index];
            }
        }

        public IEnumerator<LeanDocument> GetEnumerator()
        {
            for (int i = 0; i < count; i++)
                yield return source[start + i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Adds an adjacent child-parent document block asynchronously.</summary>
    /// <remarks>
    /// Token-budget validation is atomic for the complete block. Document blocks are
    /// not supported when <see cref="IndexWriterConfig.IndexSort"/> is configured.
    /// </remarks>
    public async ValueTask AddDocumentBlockAsync(IReadOnlyList<LeanDocument> block, CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(block);
            if (block.Count < 2)
                throw new ArgumentException("A document block requires at least one child and one parent document.", nameof(block));
            if (_config.IndexSort is not null)
                throw new NotSupportedException(
                    "Document blocks cannot be indexed when IndexSort is configured because physical document sorting would break child-parent adjacency.");
            ValidateDocuments(block);
            if (_backpressureSemaphore is not null && block.Count > _config.MaxQueuedDocs)
            {
                throw new InvalidOperationException(
                    $"Document block contains {block.Count} documents, which exceeds MaxQueuedDocs ({_config.MaxQueuedDocs}).");
            }

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var cmd = new AsyncWriteCommand(block, AsyncWriteKind.Block, tcs);
            await EnqueueAsyncWrite(cmd, cancellationToken).ConfigureAwait(false);
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfIndexingFailedWithCause();
        EnterIndexingOperation();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CommitManager.CommitWithLocks(this);
                }
                finally
                {
                    ExitIndexingOperation();
                }
            });
        }
        catch
        {
            ExitIndexingOperation();
            throw;
        }
    }

    private int GetEffectiveAsyncBatchSize(int requestedBatchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedBatchSize);
        if (_config.MaxQueuedDocs <= 0)
            return requestedBatchSize;

        return Math.Min(requestedBatchSize, _config.MaxQueuedDocs);
    }
}
