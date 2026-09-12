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
        try { AddDocumentCore(writer, doc, abortOnFatalFailure: true); }
        finally { writer.ExitIndexingOperation(); }
    }

    private static void AddDocumentCore(IndexWriter writer, LeanDocument doc, bool abortOnFatalFailure)
    {
        bool acquired = false;
        bool enteredDwpt = false;
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
                dwpt.ValidateDocument(doc);
                writer.ValidateVectorDimensions(doc);
                enteredDwpt = true;
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
                AbortUncommittedWriterState(writer);
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
                Interlocked.Add(ref writer.SemaphoreSlotsHeld, acquired);

            var pool = writer.DwptPool!;
            int slot = GetProducerSlot(writer, pool.Length);
            var dwpt = pool[slot];
            lock (dwpt)
            {
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
            ReleaseBackpressure(writer, acquired);
            throw;
        }
        catch (Exception ex)
        {
            if (enteredDwpt)
            {
                writer.MarkIndexingFailed(ex);
                AbortUncommittedWriterState(writer);
            }
            else
            {
                ReleaseBackpressure(writer, acquired);
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
        (int Index, TokenBudgetExceededException Error)? rejection = null;
        Parallel.For(0, documents.Count,
            new ParallelOptions { MaxDegreeOfParallelism = writer.ResolvedIndexingConcurrency },
            (i, loopState) =>
            {
                if (loopState.LowestBreakIteration is long lowestFailure && i > lowestFailure)
                    return;
                try
                {
                    AddDocumentCore(writer, documents[i], abortOnFatalFailure: false);
                }
                catch (TokenBudgetExceededException ex)
                {
                    lock (failureLock)
                    {
                        if (rejection is null || i < rejection.Value.Index)
                            rejection = (i, ex);
                    }
                }
                catch (Exception ex)
                {
                    lock (failureLock)
                    {
                        if (fatalFailure is null || i < fatalFailure.Value.Index)
                            fatalFailure = (i, ex);
                    }
                    // Break preserves completion of every lower document index, allowing
                    // the public primary failure to be selected deterministically.
                    loopState.Break();
                }
            });

        if (fatalFailure is { } fatal)
        {
            writer.MarkIndexingFailed(fatal.Error);
            AbortUncommittedWriterState(writer);
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
        var pending = writer.FlushPending;
        if (pending.Count == 0) return;

        foreach (var state in pending)
        {
            if (state.Result is null)
            {
                // Flush I/O not done yet — run it now
                state.Result = ExecutePhysicalFlush(writer, state.Batch,
                    state.SegmentOrdinal, writer.CommitGeneration,
                    state.SeqStart, state.SeqEnd);
            }

            writer.CommittedSegments.Add(state.Result);
            writer.ContentChangedSinceCommit = true;
        }

        pending.Clear();
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

                int ordinal = writer.NextSegmentOrdinal++;
                long seqEnd = 0, seqStart = 0;
                if (writer.Config.TrackSequenceNumbers)
                {
                    seqEnd = Interlocked.Add(ref writer.NextSequenceNumberMut, dwpt.DocCount);
                    seqStart = seqEnd - dwpt.DocCount;
                }

                var snapshot = DetachFlushBatch(writer, dwpt);

                // Flush from snapshot - I/O still under _writeLock for Step 1 simplicity
                var segInfo = ExecutePhysicalFlush(writer, snapshot,
                    ordinal, writer.CommitGeneration,
                    seqStart, seqEnd);

                writer.CommittedSegments.Add(segInfo);
                writer.ContentChangedSinceCommit = true;
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
        int ordinal = 0;
        long seqStart = 0, seqEnd = 0;
        lock (dwpt)
        {
            if (dwpt.DocCount == 0)
                return;

            ordinal = Interlocked.Increment(ref writer.NextSegmentOrdinal) - 1;
            if (writer.Config.TrackSequenceNumbers)
            {
                seqEnd = Interlocked.Add(ref writer.NextSequenceNumberMut, dwpt.DocCount);
                seqStart = seqEnd - dwpt.DocCount;
            }
            batch = DetachFlushBatch(writer, dwpt);
        }

        var segment = ExecutePhysicalFlush(writer, batch, ordinal, writer.CommitGeneration, seqStart, seqEnd);
        lock (writer.WriteLock)
        {
            writer.CommittedSegments.Add(segment);
            writer.ContentChangedSinceCommit = true;
        }
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

    /// <summary>
    /// Centralises physical flush admission. It is deliberately synchronous until the
    /// detached-flush coordinator supplies ordered asynchronous publication.
    /// </summary>
    private static SegmentInfo ExecutePhysicalFlush(
        IndexWriter writer, DwptFlushBatch batch, int ordinal, int commitGeneration, long seqStart, long seqEnd)
    {
        var semaphore = writer.FlushSemaphore ?? throw new InvalidOperationException("Flush admission is not initialised.");
        bool acquired = false;
        try
        {
            // Once detached, a batch remains writer-owned through shutdown and must
            // reach a terminal flush or failure state before indexing operations drain.
            semaphore.Wait();
            acquired = true;
            Interlocked.Increment(ref writer.ActiveFlushCount);
            writer.Config.PhysicalFlushStarted?.Invoke();
            return SegmentFlusher.FlushFromBatch(batch, writer.Config, writer.Directory.DirectoryPath,
                ordinal, commitGeneration, seqStart, seqEnd);
        }
        finally
        {
            try
            {
                if (acquired)
                    writer.Config.PhysicalFlushCompleted?.Invoke();
            }
            finally
            {
                batch.Dispose();
                if (batch.PendingBytesAccounted)
                    Interlocked.Add(ref writer.PendingFlushBytes, -batch.EstimatedBytes);
                if (acquired)
                {
                    Interlocked.Decrement(ref writer.ActiveFlushCount);
                    semaphore.Release();
                }
            }
        }
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
