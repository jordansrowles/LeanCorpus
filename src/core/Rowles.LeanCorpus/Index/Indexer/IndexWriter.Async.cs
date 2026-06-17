using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    /// <summary>
    /// Indexes a single document asynchronously.
    /// </summary>
    public async ValueTask AddDocumentAsync(LeanDocument doc, CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            ValidateDocument(doc);

            await BackpressureController.AcquireBackpressureSlotAsync(BackpressureState, WriteLock,
                Buffer, Config, CommitState, Directory.DirectoryPath, cancellationToken).ConfigureAwait(false);

            bool addedToHeldSlots = false;
            bool enteredCore = false;
            try
            {
                lock (WriteLock)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                        throw new ObjectDisposedException(nameof(IndexWriter));

                    if (BackpressureState.BackpressureSemaphore is not null)
                    {
                        BackpressureState.SemaphoreSlotsHeld++;
                        addedToHeldSlots = true;
                    }

                    if (ShouldThrottleForMerge() && Buffer.DocCount > 0)
                        FlushSegment();

                    enteredCore = true;
                    AddDocumentCore(doc);
                }
            }
            catch
            {
                if (enteredCore)
                    MarkIndexingFailed();
                BackpressureController.ReleaseFailedBackpressureSlots(BackpressureState, WriteLock, acquired: 1, addedToHeldSlots);
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
    /// </summary>
    public async ValueTask AddDocumentsAsync(IReadOnlyList<LeanDocument> documents, CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(documents);
            if (documents.Count == 0)
                return;
            ValidateDocuments(documents);

            if (BackpressureState.BackpressureSemaphore is not null && documents.Count > Config.MaxQueuedDocs)
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
                if (BackpressureState.BackpressureSemaphore is not null)
                {
                    for (int i = 0; i < documents.Count; i++)
                    {
                        await BackpressureController.AcquireBackpressureSlotAsync(BackpressureState, WriteLock,
                            Buffer, Config, CommitState, Directory.DirectoryPath, cancellationToken).ConfigureAwait(false);
                        acquired++;
                    }
                }

                lock (WriteLock)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                        throw new ObjectDisposedException(nameof(IndexWriter));

                    if (BackpressureState.BackpressureSemaphore is not null)
                    {
                        BackpressureState.SemaphoreSlotsHeld += acquired;
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
                BackpressureController.ReleaseFailedBackpressureSlots(BackpressureState, WriteLock, acquired, addedToHeldSlots);
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
    /// </summary>
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

    /// <summary>
    /// Indexes a block of child documents followed by a parent document asynchronously.
    /// </summary>
    public async ValueTask AddDocumentBlockAsync(IReadOnlyList<LeanDocument> block, CancellationToken cancellationToken = default)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(block);
            if (block.Count < 2)
                throw new ArgumentException("A document block requires at least one child and one parent document.", nameof(block));
            ValidateDocuments(block);
            if (BackpressureState.BackpressureSemaphore is not null && block.Count > Config.MaxQueuedDocs)
            {
                throw new InvalidOperationException(
                    $"Document block contains {block.Count} documents, which exceeds MaxQueuedDocs ({Config.MaxQueuedDocs}).");
            }

            int acquired = 0;
            bool addedToHeldSlots = false;
            bool enteredCore = false;
            try
            {
                if (BackpressureState.BackpressureSemaphore is not null)
                {
                    for (int i = 0; i < block.Count; i++)
                    {
                        await BackpressureController.AcquireBackpressureSlotAsync(BackpressureState, WriteLock,
                            Buffer, Config, CommitState, Directory.DirectoryPath, cancellationToken).ConfigureAwait(false);
                        acquired++;
                    }
                }

                lock (WriteLock)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                        throw new ObjectDisposedException(nameof(IndexWriter));

                    if (BackpressureState.BackpressureSemaphore is not null)
                    {
                        BackpressureState.SemaphoreSlotsHeld += acquired;
                        addedToHeldSlots = true;
                    }

                    for (int i = 0; i < block.Count; i++)
                    {
                        if (i == block.Count - 1)
                        {
                            Buffer.ParentDocIds ??= new HashSet<int>();
                            Buffer.ParentDocIds.Add(Buffer.DocCount);
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
                BackpressureController.ReleaseFailedBackpressureSlots(BackpressureState, WriteLock, acquired, addedToHeldSlots);
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
    /// </summary>
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
                    CommitManager.CommitWithLocks(CommitState, Directory, Config, Buffer,
                        WriteLock, MergeState.MergeIoLock, MergeState.MergeLock,
                        DwptState, SnapshotState, MergeState,
                        BackpressureState.BackpressureSemaphore, ref BackpressureState.SemaphoreSlotsHeld);
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
        if (Config.MaxQueuedDocs <= 0)
            return requestedBatchSize;

        return Math.Min(requestedBatchSize, Config.MaxQueuedDocs);
    }
}
