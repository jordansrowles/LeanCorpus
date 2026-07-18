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
    public static void InitialiseDwptPool(IndexWriter writer, int threadCount = 0)
    {
        if (writer.DwptPool is not null)
            return;
        if (threadCount <= 0)
            threadCount = Math.Max(1, Environment.ProcessorCount);

        writer.DwptPool = new DocumentsWriterPerThread[threadCount];
        for (int i = 0; i < threadCount; i++)
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
            {
                lock (writer.WriteLock)
                    writer.SemaphoreSlotsHeld++;
            }

            var pool = writer.DwptPool ?? throw new InvalidOperationException(
                "DWPT pool is not initialised.");

            int slot = GetProducerSlot(writer, pool.Length);
            var dwpt = pool[slot];

            lock (dwpt)
            {
                enteredDwpt = true;
                dwpt.AddDocument(doc);
            }

            long hardThreshold = (long)(writer.Config.RamPerThreadHardLimitMB * 1024 * 1024);
            long activeBytes = 0;
            foreach (var activeDwpt in pool)
                activeBytes += activeDwpt.EstimatedRamBytes;
            writer.Config.Metrics.RecordWriterMemory(activeBytes, 0, writer.PendingDeletes.Count * 96L);
            long sharedLimit = (long)(writer.Config.RamBufferSizeMB * 1024 * 1024);
            long queuedLimit = writer.Config.MaxQueuedBytes > 0 ? writer.Config.MaxQueuedBytes : long.MaxValue;
            bool sharedLimitReached = activeBytes >= Math.Min(sharedLimit, queuedLimit);
            if (dwpt.EstimatedRamBytes >= hardThreshold || sharedLimitReached ||
                dwpt.DocCount >= writer.Config.MaxBufferedDocs)
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

                        snapshot = DwptFlushSnapshot.CaptureFrom(dwpt);
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
                lock (writer.WriteLock)
                    writer.SemaphoreSlotsHeld += acquired;
            }

            var pool = writer.DwptPool!;
            int slot = GetProducerSlot(writer, pool.Length);
            var dwpt = pool[slot];
            lock (dwpt)
            {
                enteredDwpt = true;
                dwpt.AddDocumentBlock(block);
            }
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

            writer.ValidateDocuments(documents);

            // Phase 1: analyse documents in parallel. Each thread gets its own
            // DWPT and accumulates documents from its assigned partitions.
            // No I/O or shared mutable state is touched here.
            var threadDwpts = new System.Collections.Concurrent.ConcurrentBag<DocumentsWriterPerThread>();

            Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, documents.Count),
                () => CreateThreadLocalDocumentWriter(writer.DefaultAnalyser, writer.Config),
                (range, _, dwpt) =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                        dwpt.AddDocument(documents[i]);
                    return dwpt;
                },
                dwpt =>
                {
                    if (dwpt.DocCount > 0)
                        threadDwpts.Add(dwpt);
                });

            // Verify that every document was accounted for.
            int totalAnalysed = 0;
            foreach (var dwpt in threadDwpts)
                totalAnalysed += dwpt.DocCount;
            if (totalAnalysed != documents.Count)
            {
                throw new InvalidOperationException(
                    $"AddDocumentsConcurrent document count mismatch: " +
                    $"input={documents.Count}, analysed={totalAnalysed}. " +
                    $"Some partitions did not index all of their assigned documents.");
            }

            // Phase 2: flush segments and publish under WriteLock so that
            // concurrent deletes and commits cannot interleave between
            // segment creation and visibility.
            lock (writer.WriteLock)
            {
                foreach (var dwpt in threadDwpts)
                {
                    int ordinal = writer.NextSegmentOrdinal++;
                    long seqEnd = 0, seqStart = 0;
                    if (writer.Config.TrackSequenceNumbers)
                    {
                        seqEnd = Interlocked.Add(ref writer.NextSequenceNumberMut, dwpt.DocCount);
                        seqStart = seqEnd - dwpt.DocCount;
                    }

                    var segInfo = SegmentFlusher.FlushFromDwpt(
                        dwpt, writer.Config, writer.Directory.DirectoryPath,
                        ordinal, writer.CommitGeneration,
                        seqStart, seqEnd,
                        out _);

                    writer.CommittedSegments.Add(segInfo);
                    dwpt.ClearAll();
                }

                writer.ContentChangedSinceCommit = true;
            }

            // Flush directory metadata so all new segment files are visible
            // in the directory listing.
            Store.DirectoryFsync.Sync(writer.Directory.DirectoryPath, strict: false);
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
        int release;
        lock (writer.WriteLock)
        {
            release = Math.Min(count, writer.SemaphoreSlotsHeld);
            writer.SemaphoreSlotsHeld -= release;
        }
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

            int release = writer.SemaphoreSlotsHeld;
            writer.SemaphoreSlotsHeld = 0;
            BackpressureController.ReleaseSemaphoreSlots(writer, release);
        }
    }

    private static int GetProducerSlot(IndexWriter writer, int poolLength)
    {
        int threadId = Environment.CurrentManagedThreadId;
        return writer.DwptThreadSlots.GetOrAdd(threadId, _ =>
            (int)((uint)(Interlocked.Increment(ref writer.DwptCounter) - 1) % (uint)poolLength));
    }

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
