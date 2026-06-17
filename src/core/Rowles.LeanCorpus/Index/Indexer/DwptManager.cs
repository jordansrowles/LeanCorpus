using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Manages the DocumentsWriterPerThread pool and concurrent indexing paths.
/// All methods are static — operates on <see cref="DwptState"/> and parameters.
/// </summary>
internal static class DwptManager
{
    public static void InitialiseDwptPool(DwptState state, IndexWriterConfig config, IAnalyser defaultAnalyser, int threadCount = 0)
    {
        if (threadCount <= 0)
            threadCount = Math.Max(1, Environment.ProcessorCount);

        state.DwptPool = new DocumentsWriterPerThread[threadCount];
        for (int i = 0; i < threadCount; i++)
            state.DwptPool[i] = CreateThreadLocalDocumentWriter(defaultAnalyser, config);
    }

    public static void AddDocumentLockFree(
        DwptState state,
        CommitState commitState,
        MMapDirectory directory,
        IndexWriterConfig config,
        Lock writeLock,
        LeanDocument doc,
        Action enterIndexingOperation,
        Action exitIndexingOperation,
        Action<LeanDocument> validateDocument,
        Action markIndexingFailed)
    {
        enterIndexingOperation();
        try
        {
            validateDocument(doc);

            var pool = state.DwptPool ?? throw new InvalidOperationException(
                "DWPT pool not initialised. Call InitialiseDwptPool() first.");

            int slot = (int)((uint)Interlocked.Increment(ref state.DwptCounter) % (uint)pool.Length);
            var dwpt = pool[slot];

            lock (dwpt)
            {
                dwpt.AddDocument(doc);
            }

            long ramThreshold = (long)(config.RamBufferSizeMB * 1024 * 1024) / pool.Length;
            if (dwpt.EstimatedRamBytes > ramThreshold)
            {
                lock (writeLock)
                {
                    lock (dwpt)
                    {
                        if (dwpt.DocCount > 0)
                        {
                            int ordinal = commitState.NextSegmentOrdinal++;
                            long seqEnd = 0, seqStart = 0;
                            if (config.TrackSequenceNumbers)
                            {
                                seqEnd = Interlocked.Add(ref commitState.NextSequenceNumber, dwpt.DocCount);
                                seqStart = seqEnd - dwpt.DocCount;
                            }

                            var segInfo = SegmentFlusher.FlushFromDwpt(
                                dwpt, config, directory.DirectoryPath,
                                ordinal, commitState.CommitGeneration,
                                seqStart, seqEnd,
                                out _);

                            commitState.CommittedSegments.Add(segInfo);
                            commitState.ContentChangedSinceCommit = true;
                            dwpt.ClearAll();
                        }
                    }
                }
            }
        }
        finally
        {
            exitIndexingOperation();
        }
    }

    public static void AddDocumentsConcurrent(
        DwptState state,
        CommitState commitState,
        MMapDirectory directory,
        IndexWriterConfig config,
        Lock writeLock,
        IAnalyser defaultAnalyser,
        IReadOnlyList<LeanDocument> documents,
        Action enterIndexingOperation,
        Action exitIndexingOperation,
        Action<IReadOnlyList<LeanDocument>> validateDocuments)
    {
        enterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(documents);
            if (documents.Count == 0) return;

            validateDocuments(documents);

            var newSegments = new System.Collections.Concurrent.ConcurrentBag<SegmentInfo>();

            Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, documents.Count),
                () => CreateThreadLocalDocumentWriter(defaultAnalyser, config),
                (range, _, dwpt) =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                        dwpt.AddDocument(documents[i]);

                    if (dwpt.DocCount == 0) return dwpt;

                    int ordinal = Interlocked.Increment(ref commitState.NextSegmentOrdinal) - 1;
                    long seqEnd = 0, seqStart = 0;
                    if (config.TrackSequenceNumbers)
                    {
                        seqEnd = Interlocked.Add(ref commitState.NextSequenceNumber, dwpt.DocCount);
                        seqStart = seqEnd - dwpt.DocCount;
                    }

                    var segInfo = SegmentFlusher.FlushFromDwpt(
                        dwpt, config, directory.DirectoryPath,
                        ordinal, commitState.CommitGeneration,
                        seqStart, seqEnd,
                        out int _unused);

                    newSegments.Add(segInfo);
                    commitState.ContentChangedSinceCommit = true;
                    dwpt.ClearAll();
                    return dwpt;
                },
                dwpt => { });

            lock (writeLock)
            {
                foreach (var seg in newSegments)
                    commitState.CommittedSegments.Add(seg);
            }
        }
        finally
        {
            exitIndexingOperation();
        }
    }

    public static void FlushDwptPool(
        DwptState state,
        MMapDirectory directory,
        IndexWriterConfig config,
        CommitState commitState)
    {
        var pool = state.DwptPool;
        if (pool == null) return;

        foreach (var dwpt in pool)
        {
            lock (dwpt)
            {
                if (dwpt.DocCount == 0) continue;

                int ordinal = commitState.NextSegmentOrdinal++;
                long seqEnd = 0, seqStart = 0;
                if (config.TrackSequenceNumbers)
                {
                    seqEnd = Interlocked.Add(ref commitState.NextSequenceNumber, dwpt.DocCount);
                    seqStart = seqEnd - dwpt.DocCount;
                }

                var segInfo = SegmentFlusher.FlushFromDwpt(
                    dwpt, config, directory.DirectoryPath,
                    ordinal, commitState.CommitGeneration,
                    seqStart, seqEnd,
                    out _);

                commitState.CommittedSegments.Add(segInfo);
                commitState.ContentChangedSinceCommit = true;
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

        return new DocumentsWriterPerThread(threadLocalDefaultAnalyser, threadLocalFieldAnalysers, config.StorePayloads);
    }
}
