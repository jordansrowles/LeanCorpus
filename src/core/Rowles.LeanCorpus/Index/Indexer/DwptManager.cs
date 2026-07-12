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
        if (threadCount <= 0)
            threadCount = Math.Max(1, Environment.ProcessorCount);

        writer.DwptPool = new DocumentsWriterPerThread[threadCount];
        for (int i = 0; i < threadCount; i++)
            writer.DwptPool[i] = CreateThreadLocalDocumentWriter(writer.DefaultAnalyser, writer.Config);
    }

    public static void AddDocumentLockFree(IndexWriter writer, LeanDocument doc)
    {
        writer.EnterIndexingOperation();
        try
        {
            writer.ValidateDocument(doc);

            var pool = writer.DwptPool ?? throw new InvalidOperationException(
                "DWPT pool not initialised. Call InitialiseDwptPool() first.");

            int slot = (int)((uint)Interlocked.Increment(ref writer.DwptCounter) % (uint)pool.Length);
            var dwpt = pool[slot];

            lock (dwpt)
            {
                dwpt.AddDocument(doc);
            }

            long ramThreshold = (long)(writer.Config.RamBufferSizeMB * 1024 * 1024) / pool.Length;
            if (dwpt.EstimatedRamBytes > ramThreshold)
            {
                lock (writer.WriteLock)
                {
                    lock (dwpt)
                    {
                        if (dwpt.DocCount > 0)
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
                            writer.ContentChangedSinceCommit = true;
                            dwpt.ClearAll();
                        }
                    }
                }
            }
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

                var segInfo = SegmentFlusher.FlushFromDwpt(
                    dwpt, writer.Config, writer.Directory.DirectoryPath,
                    ordinal, writer.CommitGeneration,
                    seqStart, seqEnd,
                    out _);

                writer.CommittedSegments.Add(segInfo);
                writer.ContentChangedSinceCommit = true;
                dwpt.ClearAll();
            }
        }
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
