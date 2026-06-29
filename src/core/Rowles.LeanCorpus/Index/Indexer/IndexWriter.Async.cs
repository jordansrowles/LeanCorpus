using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    public async ValueTask AddDocumentAsync(LeanDocument doc, CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            ValidateDocument(doc);

            await BackpressureController.AcquireBackpressureSlotAsync(this, cancellationToken).ConfigureAwait(false);

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

                    if (ShouldThrottleForMerge() && _buffer.DocCount > 0)
                        FlushSegment();

                    enteredCore = true;
                    AddDocumentCore(doc);
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();
                BackpressureController.ReleaseFailedBackpressureSlots(this, acquired: 1, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

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
                    await AddDocumentAsync(documents[i], cancellationToken).ConfigureAwait(false);
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
                        await BackpressureController.AcquireBackpressureSlotAsync(this, cancellationToken).ConfigureAwait(false);
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
                BackpressureController.ReleaseFailedBackpressureSlots(this, acquired, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

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

                await AddDocumentsAsync(batch, cancellationToken).ConfigureAwait(false);
                batch.Clear();
            }

            if (batch.Count > 0)
                await AddDocumentsAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

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
                        await BackpressureController.AcquireBackpressureSlotAsync(this, cancellationToken).ConfigureAwait(false);
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
                            _buffer.ParentDocIds ??= new HashSet<int>();
                            _buffer.ParentDocIds.Add(_buffer.DocCount);
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
                BackpressureController.ReleaseFailedBackpressureSlots(this, acquired, addedToHeldSlots);
                throw;
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

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
