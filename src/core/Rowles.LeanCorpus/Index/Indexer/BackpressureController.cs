namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Manages backpressure semaphore acquisition and release for flow control.
/// All methods are static — operates on <see cref="BackpressureState"/> and parameters.
/// </summary>
internal static class BackpressureController
{
    public static void AcquireBackpressureSlot(
        BackpressureState state,
        Lock writeLock,
        DocumentBufferState buffer,
        IndexWriterConfig config,
        CommitState commitState,
        string directoryPath)
    {
        if (state.BackpressureSemaphore is null) return;
        if (state.BackpressureSemaphore.Wait(0)) return;

        if (Interlocked.CompareExchange(ref state.FlushElection, 1, 0) == 0)
        {
            try
            {
                lock (writeLock)
                {
                    if (buffer.DocCount > 0)
                        IndexWriter.FlushSegmentStatic(buffer, config, directoryPath, commitState,
                            state.BackpressureSemaphore, ref state.SemaphoreSlotsHeld);
                }
            }
            finally
            {
                Volatile.Write(ref state.FlushElection, 0);
            }
        }
        state.BackpressureSemaphore.Wait();
    }

    public static async ValueTask AcquireBackpressureSlotAsync(
        BackpressureState state,
        Lock writeLock,
        DocumentBufferState buffer,
        IndexWriterConfig config,
        CommitState commitState,
        string directoryPath,
        CancellationToken cancellationToken)
    {
        if (state.BackpressureSemaphore is null)
            return;

        if (state.BackpressureSemaphore.Wait(0))
            return;

        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref state.FlushElection, 1, 0) == 0)
        {
            try
            {
                lock (writeLock)
                {
                    if (buffer.DocCount > 0)
                        IndexWriter.FlushSegmentStatic(buffer, config, directoryPath, commitState,
                            state.BackpressureSemaphore, ref state.SemaphoreSlotsHeld);
                }
            }
            finally
            {
                Volatile.Write(ref state.FlushElection, 0);
            }
        }

        try
        {
            await state.BackpressureSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Semaphore disposed during shutdown — caller's disposed check catches this.
        }
    }

    public static void ReleaseSemaphoreSlots(BackpressureState state, int count)
    {
        if (state.BackpressureSemaphore is null || count <= 0)
            return;

        try
        {
            state.BackpressureSemaphore.Release(count);
        }
        catch (ObjectDisposedException)
        {
            // Semaphore already disposed during shutdown — ignore.
        }
    }

    public static void ReleaseFailedBackpressureSlots(
        BackpressureState state,
        Lock writeLock,
        int acquired,
        bool addedToHeldSlots)
    {
        if (state.BackpressureSemaphore is null || acquired <= 0)
            return;

        if (!addedToHeldSlots)
        {
            ReleaseSemaphoreSlots(state, acquired);
            return;
        }

        int toRelease;
        lock (writeLock)
        {
            toRelease = Math.Min(acquired, Math.Max(0, state.SemaphoreSlotsHeld));
            if (toRelease > 0)
                state.SemaphoreSlotsHeld -= toRelease;
        }

        if (toRelease > 0)
            ReleaseSemaphoreSlots(state, toRelease);
    }
}
