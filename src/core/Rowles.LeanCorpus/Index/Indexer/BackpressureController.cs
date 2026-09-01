namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Manages backpressure semaphore acquisition and release for flow control.
/// All methods are static — operates via a single <see cref="IndexWriter"/> parameter.
/// </summary>
internal static class BackpressureController
{
    public static void AcquireBackpressureSlot(IndexWriter writer)
    {
        var semaphore = writer.BackpressureSemaphore;
        if (semaphore is null) return;
        if (semaphore.Wait(0)) return;

        if (Interlocked.CompareExchange(ref writer.FlushElection, 1, 0) == 0)
        {
            try
            {
                lock (writer.WriteLock)
                {
                    DwptManager.FlushDwptPool(writer);
                    if (writer.Buffer.DocCount > 0)
                        IndexWriter.FlushSegmentStatic(writer);
                }
            }
            finally
            {
                Volatile.Write(ref writer.FlushElection, 0);
            }
        }
        try
        {
            semaphore.Wait(writer.ShutdownToken);
        }
        catch (OperationCanceledException) when (writer.IsClosing)
        {
            throw new ObjectDisposedException(
                nameof(IndexWriter),
                "The writer is shutting down.");
        }
    }



    public static void ReleaseSemaphoreSlots(IndexWriter writer, int count)
    {
        if (writer.BackpressureSemaphore is null || count <= 0)
            return;

        try
        {
            writer.BackpressureSemaphore.Release(count);
        }
        catch (ObjectDisposedException)
        {
            // Semaphore already disposed during shutdown — ignore.
        }
    }

    public static void ReleaseFailedBackpressureSlots(
        IndexWriter writer,
        int acquired,
        bool addedToHeldSlots)
    {
        if (writer.BackpressureSemaphore is null || acquired <= 0)
            return;

        if (!addedToHeldSlots)
        {
            ReleaseSemaphoreSlots(writer, acquired);
            return;
        }

        int toRelease = TakeHeldSlots(writer, acquired);

        if (toRelease > 0)
            ReleaseSemaphoreSlots(writer, toRelease);
    }

    /// <summary>
    /// Atomically removes up to <paramref name="requested"/> acquired slots. This deliberately
    /// does not take the writer lock: a producer can release slots while holding a DWPT monitor,
    /// whereas commit takes the writer lock before entering that monitor.
    /// </summary>
    internal static int TakeHeldSlots(IndexWriter writer, int requested)
    {
        while (requested > 0)
        {
            int current = Volatile.Read(ref writer.SemaphoreSlotsHeld);
            if (current <= 0)
                return 0;

            int release = Math.Min(requested, current);
            if (Interlocked.CompareExchange(
                ref writer.SemaphoreSlotsHeld,
                current - release,
                current) == current)
            {
                return release;
            }
        }

        return 0;
    }
}
