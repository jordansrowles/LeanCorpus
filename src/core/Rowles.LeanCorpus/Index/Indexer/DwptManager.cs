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

            var newSegments = new System.Collections.Concurrent.ConcurrentBag<SegmentInfo>();

            Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, documents.Count),
                () => CreateThreadLocalDocumentWriter(writer.DefaultAnalyser, writer.Config),
                (range, _, dwpt) =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                        dwpt.AddDocument(documents[i]);

                    if (dwpt.DocCount == 0) return dwpt;

                    int ordinal = Interlocked.Increment(ref writer.NextSegmentOrdinal) - 1;
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
                        out int _unused);

                    newSegments.Add(segInfo);

                    writer.ContentChangedSinceCommit = true;
                    dwpt.ClearAll();
                    return dwpt;
                },
                dwpt => { });

            // Verify that every document was accounted for by the parallel
            // partitions.
            int totalFlushed = 0;
            foreach (var seg in newSegments)
                totalFlushed += seg.DocCount;
            if (totalFlushed != documents.Count)
            {
                throw new InvalidOperationException(
                    $"AddDocumentsConcurrent document count mismatch: " +
                    $"input={documents.Count}, flushed={totalFlushed}. " +
                    $"Some partitions did not index all of their assigned documents.");
            }

            // Flush the directory metadata to durable storage so that every
            // segment file created by the parallel partitions is visible in
            // the directory listing before we add those segments to the
            // writer's committed list.
            Store.DirectoryFsync.Sync(writer.Directory.DirectoryPath, strict: false);

            lock (writer.WriteLock)
            {
                foreach (var seg in newSegments)
                    writer.CommittedSegments.Add(seg);
            }
        }
        finally
        {
            writer.ExitIndexingOperation();
        }
    }

    public static void FlushDwptPool(IndexWriter writer)
    {
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

        return new DocumentsWriterPerThread(threadLocalDefaultAnalyser, threadLocalFieldAnalysers, config.StorePayloads);
    }
}
