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
            writer.DwptPool[i] = CreateThreadLocalDocumentWriter(writer.DefaultAnalyser, writer.Config);
    }

    public static void AddDocument(IndexWriter writer, LeanDocument doc)
    {
        writer.EnterIndexingOperation();
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
                writer.ValidateVectorDimensions([doc]);
                enteredDwpt = true;
                long before = dwpt.EstimatedRamBytes;
                dwpt.AddPrevalidatedDocument(doc);
                Interlocked.Add(ref writer.ActiveDwptBytes, dwpt.EstimatedRamBytes - before);
            }

            long activeBytes = Volatile.Read(ref writer.ActiveDwptBytes);
            writer.Config.Metrics.RecordWriterMemory(activeBytes, 0, writer.PendingDeletes.Count * 96L);
            long hardThreshold = writer.Config.RamPerThreadHardLimitMB > 0
                ? (long)(writer.Config.RamPerThreadHardLimitMB * 1024 * 1024)
                : long.MaxValue;
            long sharedLimit = writer.Config.RamBufferSizeMB > 0
                ? (long)(writer.Config.RamBufferSizeMB * 1024 * 1024)
                : long.MaxValue;
            long queuedLimit = writer.Config.MaxQueuedBytes > 0 ? writer.Config.MaxQueuedBytes : long.MaxValue;
            bool sharedLimitReached = activeBytes >= Math.Min(sharedLimit, queuedLimit);
            if (dwpt.EstimatedRamBytes >= hardThreshold || sharedLimitReached ||
                (writer.Config.MaxBufferedDocs > 0 && dwpt.DocCount >= writer.Config.MaxBufferedDocs))
            {
                DwptFlushSnapshot? snapshot = null;
                int ordinal = 0;
                long seqEnd = 0, seqStart = 0;

                lock (dwpt)
                {
                    if (dwpt.DocCount > 0)
                    {
                        ordinal = Interlocked.Increment(ref writer.NextSegmentOrdinal) - 1;
                        if (writer.Config.TrackSequenceNumbers)
                        {
                            seqEnd = Interlocked.Add(ref writer.NextSequenceNumberMut, dwpt.DocCount);
                            seqStart = seqEnd - dwpt.DocCount;
                        }

                        long detachedBytes = dwpt.EstimatedRamBytes;
                        snapshot = DwptFlushSnapshot.CaptureFrom(dwpt);
                        Interlocked.Add(ref writer.ActiveDwptBytes, -detachedBytes);
                        ReleaseBackpressure(writer, snapshot.DocCount);
                    }
                }

                if (snapshot != null)
                {
                    // Flush synchronously outside _writeLock
                    var segInfo = SegmentFlusher.FlushFromSnapshot(
                        snapshot, writer.Config, writer.Directory.DirectoryPath,
                        ordinal, writer.CommitGeneration,
                        seqStart, seqEnd);

                    // Publish briefly under _writeLock
                    lock (writer.WriteLock)
                    {
                        writer.CommittedSegments.Add(segInfo);
                        writer.ContentChangedSinceCommit = true;
                    }
                }
            }

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
        catch
        {
            if (enteredDwpt)
            {
                AbortUncommittedWriterState(writer);
                writer.MarkIndexingFailed();
            }
            else if (acquired)
                ReleaseBackpressure(writer, 1);
            throw;
        }
        finally
        {
            writer.ExitIndexingOperation();
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
        }
        catch (TokenBudgetExceededException)
        {
            // The whole block is preflighted before its first document is
            // added, so a rejected block leaves the DWPT unchanged.
            ReleaseBackpressure(writer, acquired);
            throw;
        }
        catch
        {
            if (enteredDwpt)
            {
                AbortUncommittedWriterState(writer);
                writer.MarkIndexingFailed();
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
        try
        {
            ArgumentNullException.ThrowIfNull(documents);
            if (documents.Count == 0) return;

            Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, documents.Count),
                new ParallelOptions { MaxDegreeOfParallelism = writer.ResolvedIndexingConcurrency },
                range =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                        AddDocument(writer, documents[i]);
                });
        }
        finally
        {
            writer.ExitIndexingOperation();
        }
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
                state.Result = SegmentFlusher.FlushFromSnapshot(
                    state.Snapshot, writer.Config, writer.Directory.DirectoryPath,
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

                var snapshot = DwptFlushSnapshot.CaptureFrom(dwpt);
                Interlocked.Add(ref writer.ActiveDwptBytes, -snapshot.EstimatedBytes);
                ReleaseBackpressure(writer, snapshot.DocCount);

                // Flush from snapshot - I/O still under _writeLock for Step 1 simplicity
                var segInfo = SegmentFlusher.FlushFromSnapshot(
                    snapshot, writer.Config, writer.Directory.DirectoryPath,
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

    private static void AbortUncommittedWriterState(IndexWriter writer)
    {
        lock (writer.WriteLock)
        {
            if (writer.DwptPool is not null)
            {
                foreach (var dwpt in writer.DwptPool)
                {
                    lock (dwpt)
                        dwpt.ClearAll();
                }
            }

            Interlocked.Exchange(ref writer.ActiveDwptBytes, 0);
            int release = Interlocked.Exchange(ref writer.SemaphoreSlotsHeld, 0);
            BackpressureController.ReleaseSemaphoreSlots(writer, release);
        }
    }

    private static int GetProducerSlot(IndexWriter writer, int poolLength)
        => (int)((uint)Environment.CurrentManagedThreadId % (uint)poolLength);

    private static DocumentsWriterPerThread CreateThreadLocalDocumentWriter(
        IAnalyser defaultAnalyser, IndexWriterConfig config)
    {
        IAnalyser threadLocalDefaultAnalyser = defaultAnalyser switch
        {
            StandardAnalyser => new StandardAnalyser(config.AnalyserInternCacheSize, config.StopWords),
            WhitespaceAnalyser => new WhitespaceAnalyser(config.AnalyserInternCacheSize),
            KeywordAnalyser => new KeywordAnalyser(config.AnalyserInternCacheSize),
            SimpleAnalyser => new SimpleAnalyser(config.AnalyserInternCacheSize),
            StemmedAnalyser => new StemmedAnalyser(),
            Analyser a => a.Clone(),
            _ => defaultAnalyser
        };

        var threadLocalFieldAnalysers = new Dictionary<string, IAnalyser>(config.FieldAnalysers.Count);
        foreach (var kvp in config.FieldAnalysers)
        {
            threadLocalFieldAnalysers[kvp.Key] = kvp.Value switch
            {
                StandardAnalyser => new StandardAnalyser(),
                WhitespaceAnalyser => new WhitespaceAnalyser(),
                KeywordAnalyser => new KeywordAnalyser(),
                SimpleAnalyser => new SimpleAnalyser(),
                StemmedAnalyser => new StemmedAnalyser(),
                Analyser a => a.Clone(),
                _ => kvp.Value
            };
        }

        return new DocumentsWriterPerThread(threadLocalDefaultAnalyser, threadLocalFieldAnalysers, config);
    }
}
