using System.Diagnostics;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Manages the DocumentsWriterPerThread pool and concurrent indexing paths.
/// All methods are static — operates via a single <see cref="IndexWriter"/> parameter.
/// </summary>
internal static class DwptManager
{
    public static void InitialiseDwptPool(IndexWriter writer)
    {
        if (writer.DwptPool is not null)
            return;

        writer.DwptPool = new DocumentsWriterPerThread[writer.ResolvedIndexingConcurrency];
        for (int i = 0; i < writer.ResolvedIndexingConcurrency; i++)
        {
            var dwpt = CreateThreadLocalDocumentWriter(writer.DefaultAnalyser, writer.Config);
            writer.DwptPool[i] = dwpt;
            Interlocked.Add(ref writer.ActiveDwptBytes, dwpt.EstimatedRamBytes);
        }
    }

    public static void AddDocument(IndexWriter writer, LeanDocument doc)
    {
        writer.EnterIndexingOperation();
        try { AddDocumentCore(writer, doc, abortOnFatalFailure: true, out _); }
        finally { writer.ExitIndexingOperation(); }
    }

    private static void AddDocumentCore(IndexWriter writer, LeanDocument doc, bool abortOnFatalFailure, out bool mutated)
    {
        bool acquired = false;
        bool enteredDwpt = false;
        mutated = false;
        try
        {
            writer.ValidateDocument(doc);
            BackpressureController.AcquireBackpressureSlot(writer);
            acquired = writer.BackpressureSemaphore is not null;
            if (acquired)
                Interlocked.Increment(ref writer.SemaphoreSlotsHeld);

            var pool = writer.DwptPool ?? throw new InvalidOperationException(
                "DWPT pool is not initialised.");

            int slot = GetProducerSlot(writer, pool.Length);
            var dwpt = pool[slot];

            lock (dwpt)
            {
                writer.ThrowIfIndexingFailed();
                dwpt.ValidateDocument(doc);
                writer.ValidateVectorDimensions(doc);
                enteredDwpt = true;
                mutated = true;
                long before = dwpt.EstimatedRamBytes;
                dwpt.AddPrevalidatedDocument(doc);
                Interlocked.Add(ref writer.ActiveDwptBytes, dwpt.EstimatedRamBytes - before);
            }

            EvaluateAutomaticFlush(writer, dwpt);

            if (writer.ShouldThrottleForMerge())
                writer.ThrottleMerge();
        }
        catch (TokenBudgetExceededException)
        {
            // DocumentsWriterPerThread validates the document before mutating
            // its buffers. Reject therefore skips only this document and must
            // not poison the writer or discard earlier accepted documents.
            if (acquired)
                ReleaseBackpressure(writer, 1);
            throw;
        }
        catch (Exception ex)
        {
            if (enteredDwpt && abortOnFatalFailure)
            {
                writer.MarkIndexingFailed(ex);
                ReconcileFatalFailure(writer);
            }
            else if (acquired)
                ReleaseBackpressure(writer, 1);
            throw;
        }
    }

    public static void AddDocumentBlock(IndexWriter writer, IReadOnlyList<LeanDocument> block)
    {
        writer.EnterIndexingOperation();
        int acquired = 0;
        bool enteredDwpt = false;
        bool addedToHeldSlots = false;
        try
        {
            ArgumentNullException.ThrowIfNull(block);
            if (block.Count < 2)
                throw new ArgumentException("A document block requires at least one child and one parent document.", nameof(block));
            if (writer.Config.IndexSort is not null)
                throw new NotSupportedException(
                    "Document blocks cannot be indexed when IndexSort is configured because physical document sorting would break child-parent adjacency.");
            writer.ValidateDocuments(block);
            if (writer.BackpressureSemaphore is not null && block.Count > writer.Config.MaxQueuedDocs)
                throw new InvalidOperationException(
                    $"Document block contains {block.Count} documents, which exceeds MaxQueuedDocs ({writer.Config.MaxQueuedDocs}).");

            for (int i = 0; i < block.Count; i++)
            {
            BackpressureController.AcquireBackpressureSlot(writer);
                if (writer.BackpressureSemaphore is not null)
                    acquired++;
            }
            if (acquired > 0)
            {
                Interlocked.Add(ref writer.SemaphoreSlotsHeld, acquired);
                addedToHeldSlots = true;
            }

            var pool = writer.DwptPool!;
            int slot = GetProducerSlot(writer, pool.Length);
            var dwpt = pool[slot];
            lock (dwpt)
            {
                writer.ThrowIfIndexingFailed();
                dwpt.ValidateDocumentBlock(block);
                writer.ValidateVectorDimensions(block);
                enteredDwpt = true;
                long before = dwpt.EstimatedRamBytes;
                dwpt.AddPrevalidatedDocumentBlock(block);
                Interlocked.Add(ref writer.ActiveDwptBytes, dwpt.EstimatedRamBytes - before);
            }
            EvaluateAutomaticFlush(writer, dwpt);
        }
        catch (TokenBudgetExceededException)
        {
            // The whole block is preflighted before its first document is
            // added, so a rejected block leaves the DWPT unchanged.
            BackpressureController.ReleaseFailedBackpressureSlots(writer, acquired, addedToHeldSlots);
            throw;
        }
        catch (Exception ex)
        {
            if (enteredDwpt)
            {
                writer.MarkIndexingFailed(ex);
                ReconcileFatalFailure(writer);
            }
            else
            {
                BackpressureController.ReleaseFailedBackpressureSlots(writer, acquired, addedToHeldSlots);
            }
            throw;
        }
        finally
        {
            writer.ExitIndexingOperation();
        }
    }

    public static void AddDocumentsConcurrent(IndexWriter writer, IReadOnlyList<LeanDocument> documents)
    {
        writer.EnterIndexingOperation();
        try { AddDocumentsConcurrentOperationOwned(writer, documents); }
        finally { writer.ExitIndexingOperation(); }
    }

    internal static void AddDocumentsConcurrentOperationOwned(IndexWriter writer, IReadOnlyList<LeanDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0) return;

        var failureLock = new Lock();
        (int Index, Exception Error)? fatalFailure = null;
        (int Index, Exception Error)? rejection = null;
        Parallel.For(0, documents.Count,
            new ParallelOptions { MaxDegreeOfParallelism = writer.ResolvedIndexingConcurrency },
            (i, loopState) =>
            {
                if (loopState.LowestBreakIteration is long lowestFailure && i > lowestFailure)
                    return;
                bool mutated = false;
                try
                {
                    AddDocumentCore(writer, documents[i], abortOnFatalFailure: false, out mutated);
                }
                catch (TokenBudgetExceededException ex)
                {
                    lock (failureLock)
                    {
                        if (rejection is null || i < rejection.Value.Index)
                            rejection = (i, ex);
                    }
                }
                catch (Exception ex) when (!mutated)
                {
                    lock (failureLock)
                    {
                        if (rejection is null || i < rejection.Value.Index)
                            rejection = (i, ex);
                    }
                    loopState.Break();
                }
                catch (Exception ex)
                {
                    lock (failureLock)
                    {
                        if (fatalFailure is null || i < fatalFailure.Value.Index)
                            fatalFailure = (i, ex);

                        // Close admission before the parallel loop unwinds. The lowest-index
                        // failure remains selected below once every lower iteration has settled.
                        writer.MarkIndexingFailed();
                    }
                    // Break preserves completion of every lower document index, allowing
                    // the public primary failure to be selected deterministically.
                    loopState.Break();
                }
            });

        if (fatalFailure is { } fatal)
        {
            writer.MarkIndexingFailed(fatal.Error);
            ReconcileFatalFailure(writer);
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(fatal.Error).Throw();
        }
        if (rejection is { } rejected)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(rejected.Error).Throw();
    }

    /// <summary>
    /// Drains all pending detached flushes and publishes their segments.
    /// Caller must hold <see cref="IndexWriter.WriteLock"/>.
    /// </summary>
    internal static void WaitForPendingFlushes(IndexWriter writer)
    {
        Debug.Assert(writer.WriteLock.IsHeldByCurrentThread, "WaitForPendingFlushes requires the caller to hold writer.WriteLock.");
        writer.FlushCoordinator.DrainAndPublish();
    }

    /// <summary>
    /// Flushes every non-empty DWPT in the pool into committed segments.
    /// Caller must hold <see cref="IndexWriter.WriteLock"/>.
    /// </summary>
    public static void FlushDwptPool(IndexWriter writer)
    {
        Debug.Assert(writer.WriteLock.IsHeldByCurrentThread, "FlushDwptPool requires the caller to hold writer.WriteLock.");
        var pool = writer.DwptPool;
        if (pool == null) return;

        foreach (var dwpt in pool)
        {
            lock (dwpt)
            {
                if (dwpt.DocCount == 0) continue;

                var batch = DetachFlushBatch(writer, dwpt);
                writer.FlushCoordinator.Submit(batch, writer.CommitGeneration);
            }
        }
    }

    private static void ReleaseBackpressure(IndexWriter writer, int count)
    {
        if (writer.BackpressureSemaphore is null || count <= 0)
            return;
        int release = BackpressureController.TakeHeldSlots(writer, count);
        BackpressureController.ReleaseSemaphoreSlots(writer, release);
    }

    /// <summary>
    /// The sole automatic-flush policy. Blocks invoke it only after the entire block has
    /// been accepted, preserving parent-child adjacency in the detached batch.
    /// </summary>
    private static void EvaluateAutomaticFlush(IndexWriter writer, DocumentsWriterPerThread dwpt)
    {
        long activeBytes = Volatile.Read(ref writer.ActiveDwptBytes);
        long pendingBytes = Volatile.Read(ref writer.PendingFlushBytes);
        writer.Config.Metrics.RecordWriterMemory(activeBytes, pendingBytes, writer.PendingDeletes.Count * 96L);

        long hardLimit = writer.Config.RamPerThreadHardLimitMB > 0
            ? (long)(writer.Config.RamPerThreadHardLimitMB * 1024 * 1024)
            : long.MaxValue;
        long sharedLimit = writer.Config.RamBufferSizeMB > 0
            ? (long)(writer.Config.RamBufferSizeMB * 1024 * 1024)
            : long.MaxValue;
        long queuedLimit = writer.Config.MaxQueuedBytes > 0 ? writer.Config.MaxQueuedBytes : long.MaxValue;
        bool flush = dwpt.EstimatedRamBytes >= hardLimit
            || activeBytes + pendingBytes >= Math.Min(sharedLimit, queuedLimit)
            || (writer.Config.MaxBufferedDocs > 0 && dwpt.DocCount >= writer.Config.MaxBufferedDocs);
        if (!flush)
            return;

        DwptFlushBatch? batch = null;
        lock (dwpt)
        {
            if (dwpt.DocCount == 0)
                return;

            batch = DetachFlushBatch(writer, dwpt);
        }

        writer.FlushCoordinator.Submit(batch, writer.CommitGeneration);

        while (IsRetainedMemoryOverBudget(writer))
        {
            if (!writer.FlushCoordinator.WaitForPhysicalProgress())
                break;
            lock (writer.WriteLock)
                writer.FlushCoordinator.PublishCompletedPrefix();
        }
    }

    private static bool IsRetainedMemoryOverBudget(IndexWriter writer)
    {
        long sharedLimit = writer.Config.RamBufferSizeMB > 0
            ? (long)(writer.Config.RamBufferSizeMB * 1024 * 1024)
            : long.MaxValue;
        long queuedLimit = writer.Config.MaxQueuedBytes > 0 ? writer.Config.MaxQueuedBytes : long.MaxValue;
        long effectiveLimit = Math.Min(sharedLimit, queuedLimit);
        return Volatile.Read(ref writer.ActiveDwptBytes) + Volatile.Read(ref writer.PendingFlushBytes) >= effectiveLimit;
    }

    private static DwptFlushBatch DetachFlushBatch(IndexWriter writer, DocumentsWriterPerThread dwpt)
    {
        var batch = DwptFlushBatch.CaptureFrom(dwpt);
        Interlocked.Add(ref writer.ActiveDwptBytes, dwpt.EstimatedRamBytes - batch.EstimatedBytes);
        Interlocked.Add(ref writer.PendingFlushBytes, batch.EstimatedBytes);
        batch.PendingBytesAccounted = true;
        ReleaseBackpressure(writer, batch.DocCount);
        return batch;
    }


    private static void AbortUncommittedWriterState(IndexWriter writer)
    {
        lock (writer.WriteLock)
        {
            long retainedBytes = 0;
            if (writer.DwptPool is not null)
            {
                foreach (var dwpt in writer.DwptPool)
                {
                    lock (dwpt)
                    {
                        dwpt.ClearAll();
                        retainedBytes += dwpt.EstimatedRamBytes;
                    }
                }
            }

            Interlocked.Exchange(ref writer.ActiveDwptBytes, retainedBytes);
            int release = Interlocked.Exchange(ref writer.SemaphoreSlotsHeld, 0);
            BackpressureController.ReleaseSemaphoreSlots(writer, release);
        }
    }

    private static void ReconcileFatalFailure(IndexWriter writer)
    {
        if (!writer.TryOwnFailureReconciliation())
            return;

        // Each DWPT monitor serialises mutation with this clear. Producers which
        // reach a selected DWPT after poisoning re-check the writer state first.
        AbortUncommittedWriterState(writer);
    }

    private static int GetProducerSlot(IndexWriter writer, int poolLength)
        => (int)((uint)Environment.CurrentManagedThreadId % (uint)poolLength);

    private static DocumentsWriterPerThread CreateThreadLocalDocumentWriter(
        IAnalyser defaultAnalyser, IndexWriterConfig config)
    {
        IAnalyser threadLocalDefaultAnalyser = CreateOwnedAnalyser(defaultAnalyser, "DefaultAnalyser");

        var threadLocalFieldAnalysers = new Dictionary<string, IAnalyser>(config.FieldAnalysers.Count);
        foreach (var kvp in config.FieldAnalysers)
            threadLocalFieldAnalysers[kvp.Key] = CreateOwnedAnalyser(kvp.Value, $"FieldAnalysers['{kvp.Key}']");

        return new DocumentsWriterPerThread(threadLocalDefaultAnalyser, threadLocalFieldAnalysers, config);
    }

    private static IAnalyser CreateOwnedAnalyser(IAnalyser analyser, string configurationName)
    {
        if (analyser is not IThreadLocalAnalyser owned)
        {
            throw new InvalidOperationException(
                $"{configurationName} ({analyser.GetType().FullName}) does not implement {nameof(IThreadLocalAnalyser)}. " +
                "Concurrent indexing requires an explicit independent analyser ownership contract.");
        }

        return owned.CreateThreadLocalAnalyser()
            ?? throw new InvalidOperationException($"{configurationName} returned a null thread-local analyser.");
    }
}
