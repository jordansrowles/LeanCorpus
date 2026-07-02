namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Manages backpressure semaphore acquisition and release for flow control.
/// All methods are static — operates via a single <see cref="IndexWriter"/> parameter.
/// </summary>
internal static class BackpressureController
{
    public static void AcquireBackpressureSlot(IndexWriter writer)
    {
        if (writer.BackpressureSemaphore is null) return;
        if (writer.BackpressureSemaphore.Wait(0)) return;

        if (Interlocked.CompareExchange(ref writer.FlushElection, 1, 0) == 0)
        {
            try
            {
                lock (writer.WriteLock)
                {
                    if (writer.Buffer.DocCount > 0)
                        IndexWriter.FlushSegmentStatic(writer);
                }
            }
            finally
            {
                Volatile.Write(ref writer.FlushElection, 0);
            }
        }
        writer.BackpressureSemaphore.Wait();
    }

    public static async ValueTask AcquireBackpressureSlotAsync(
        IndexWriter writer,
        CancellationToken cancellationToken)
    {
        if (writer.BackpressureSemaphore is null)
            return;

        if (await writer.BackpressureSemaphore.WaitAsync(TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false))
            return;

        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref writer.FlushElection, 1, 0) == 0)
        {
            try
            {
                lock (writer.WriteLock)
                {
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
            await writer.BackpressureSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Semaphore disposed during shutdown — caller's disposed check catches this.
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

        int toRelease;
        lock (writer.WriteLock)
        {
            toRelease = Math.Min(acquired, Math.Max(0, writer.SemaphoreSlotsHeld));
            if (toRelease > 0)
                writer.SemaphoreSlotsHeld -= toRelease;
        }

        if (toRelease > 0)
            ReleaseSemaphoreSlots(writer, toRelease);
    }
}
