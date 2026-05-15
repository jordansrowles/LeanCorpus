using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    /// <summary>
    /// Indexes a single document asynchronously.
    /// Uses asynchronous backpressure waits when the writer queue is full, then
    /// reuses the same synchronous indexing core as <see cref="AddDocument"/>.
    /// </summary>
    /// <param name="doc">The document to index.</param>
    /// <param name="cancellationToken">Cancels the operation before the document enters the writer critical section.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is cancelled before indexing starts.</exception>
    public async ValueTask AddDocumentAsync(LeanDocument doc, CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            ValidateDocument(doc);

            await AcquireBackpressureSlotAsync(cancellationToken);

            bool addedToHeldSlots = false;
            bool enteredCore = false;
            try
            {
                lock (_writeLock)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                        throw new ObjectDisposedException(nameof(IndexWriter));

                    if (_backpressureSemaphore is not null)
                    {
                        _semaphoreSlotsHeld++;
                        addedToHeldSlots = true;
                    }

                    if (ShouldThrottleForMerge() && _bufferedDocCount > 0)
                        FlushSegment();

                    enteredCore = true;
                    AddDocumentCore(doc);
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();
                ReleaseFailedBackpressureSlots(acquired: 1, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Indexes a batch of documents asynchronously with a single writer-lock acquisition.
    /// Uses asynchronous backpressure waits when needed, then reuses the same synchronous
    /// batch indexing core as <see cref="AddDocuments"/>.
    /// </summary>
    /// <param name="documents">The documents to index.</param>
    /// <param name="cancellationToken">Cancels the operation before the batch enters the writer critical section.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    /// <exception cref="OperationCanceledException">Thrown if <paramref name="cancellationToken"/> is cancelled before indexing starts.</exception>
    public async ValueTask AddDocumentsAsync(IReadOnlyList<LeanDocument> documents, CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(documents);
            if (documents.Count == 0)
                return;
            ValidateDocuments(documents);

            if (_backpressureSemaphore is not null && documents.Count > _config.MaxQueuedDocs)
            {
                for (int i = 0; i < documents.Count; i++)
                    await AddDocumentAsync(documents[i], cancellationToken);
                return;
            }

            int acquired = 0;
            bool addedToHeldSlots = false;
            bool enteredCore = false;
            try
            {
                if (_backpressureSemaphore is not null)
                {
                    for (int i = 0; i < documents.Count; i++)
                    {
                        await AcquireBackpressureSlotAsync(cancellationToken);
                        acquired++;
                    }
                }

                lock (_writeLock)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                        throw new ObjectDisposedException(nameof(IndexWriter));

                    if (_backpressureSemaphore is not null)
                    {
                        _semaphoreSlotsHeld += acquired;
                        addedToHeldSlots = true;
                    }

                    for (int i = 0; i < documents.Count; i++)
                    {
                        enteredCore = true;
                        AddDocumentCore(documents[i]);
                    }
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();
                ReleaseFailedBackpressureSlots(acquired, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Indexes streamed documents from an async enumerable using bounded batches.
    /// Fully submitted batches are retained if the source later faults; the current
    /// in-memory batch is discarded when the enumerable fails before the next flush.
    /// </summary>
    /// <param name="documents">The streamed documents to index.</param>
    /// <param name="batchSize">The maximum batch size submitted to the writer at once. Must be greater than zero.</param>
    /// <param name="cancellationToken">Cancels source consumption or a pending batch submission.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize"/> is less than one.</exception>
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

            await foreach (var document in documents.WithCancellation(cancellationToken))
            {
                batch.Add(document);
                if (batch.Count < effectiveBatchSize)
                    continue;

                await AddDocumentsAsync(batch, cancellationToken);
                batch.Clear();
            }

            if (batch.Count > 0)
                await AddDocumentsAsync(batch, cancellationToken);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Indexes a block of child documents followed by a parent document asynchronously.
    /// Cancellation is honoured only before the block enters the writer critical section,
    /// preserving the same all-or-nothing block semantics as <see cref="AddDocumentBlock"/>.
    /// </summary>
    /// <param name="block">The documents to index as a block. The last element is the parent.</param>
    /// <param name="cancellationToken">Cancels the operation before the block enters the writer critical section.</param>
    /// <exception cref="ArgumentException">Thrown if the block has fewer than two documents.</exception>
    public async ValueTask AddDocumentBlockAsync(IReadOnlyList<LeanDocument> block, CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(block);
            if (block.Count < 2)
                throw new ArgumentException("A document block requires at least one child and one parent document.", nameof(block));
            ValidateDocuments(block);
            if (_backpressureSemaphore is not null && block.Count > _config.MaxQueuedDocs)
            {
                throw new InvalidOperationException(
                    $"Document block contains {block.Count} documents, which exceeds MaxQueuedDocs ({_config.MaxQueuedDocs}).");
            }

            int acquired = 0;
            bool addedToHeldSlots = false;
            bool enteredCore = false;
            try
            {
                if (_backpressureSemaphore is not null)
                {
                    for (int i = 0; i < block.Count; i++)
                    {
                        await AcquireBackpressureSlotAsync(cancellationToken);
                        acquired++;
                    }
                }

                lock (_writeLock)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                        throw new ObjectDisposedException(nameof(IndexWriter));

                    if (_backpressureSemaphore is not null)
                    {
                        _semaphoreSlotsHeld += acquired;
                        addedToHeldSlots = true;
                    }

                    for (int i = 0; i < block.Count; i++)
                    {
                        if (i == block.Count - 1)
                        {
                            _parentDocIds ??= new HashSet<int>();
                            _parentDocIds.Add(_bufferedDocCount);
                        }

                        enteredCore = true;
                        AddDocumentCore(block[i], suppressFlush: true);
                    }

                    if (ShouldFlush())
                        FlushSegment();
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();
                ReleaseFailedBackpressureSlots(acquired, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Flushes buffered work and publishes a durable commit on a background worker thread.
    /// Cancellation is honoured before the commit starts; once started, the commit runs to completion
    /// so the on-disk durability sequence stays identical to <see cref="Commit"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the operation before the commit starts.</param>
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    CommitWithLocks();
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

    private async ValueTask AcquireBackpressureSlotAsync(CancellationToken cancellationToken)
    {
        if (_backpressureSemaphore is null)
            return;

        if (_backpressureSemaphore.Wait(0))
            return;

        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref _flushElection, 1, 0) == 0)
        {
            try
            {
                lock (_writeLock)
                {
                    if (_bufferedDocCount > 0)
                        FlushSegment();
                }
            }
            finally
            {
                Volatile.Write(ref _flushElection, 0);
            }
        }

        try
        {
            await _backpressureSemaphore.WaitAsync(cancellationToken);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
            throw new ObjectDisposedException(nameof(IndexWriter));
        }

        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(IndexWriter));
    }

    private void ReleaseFailedBackpressureSlots(int acquired, bool addedToHeldSlots)
    {
        if (_backpressureSemaphore is null || acquired <= 0)
            return;

        if (!addedToHeldSlots)
        {
            ReleaseSemaphoreSlots(acquired);
            return;
        }

        int toRelease;
        lock (_writeLock)
        {
            toRelease = Math.Min(acquired, Math.Max(0, _semaphoreSlotsHeld));
            if (toRelease > 0)
                _semaphoreSlotsHeld -= toRelease;
        }

        if (toRelease > 0)
            ReleaseSemaphoreSlots(toRelease);
    }

    private void ReleaseSemaphoreSlots(int count)
    {
        if (_backpressureSemaphore is null || count <= 0)
            return;

        try
        {
            _backpressureSemaphore.Release(count);
        }
        catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
        {
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
