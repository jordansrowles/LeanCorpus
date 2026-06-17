using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;

namespace Rowles.LeanCorpus.Index.Indexer;

public sealed partial class IndexWriter
{
    private DocumentsWriterPerThread[]? _dwptPool;
    private int _dwptCounter; // for round-robin assignment via Interlocked

    /// <summary>
    /// Initialises the DWPT pool for concurrent indexing.
    /// Call once before using <see cref="AddDocumentLockFree"/> or <see cref="AddDocumentsConcurrent"/>.
    /// </summary>
    /// <param name="threadCount">Number of per-thread writers to allocate (default: processor count).</param>
    public void InitialiseDwptPool(int threadCount = 0)
    {
        if (threadCount <= 0)
            threadCount = Math.Max(1, Environment.ProcessorCount);

        _dwptPool = new DocumentsWriterPerThread[threadCount];
        for (int i = 0; i < threadCount; i++)
            _dwptPool[i] = CreateThreadLocalDocumentWriter();
    }

    /// <summary>
    /// Lock-free document addition using per-thread DWPT buffers.
    /// Uses <see cref="Interlocked.Increment(ref int)"/> for round-robin DWPT selection.
    /// Each DWPT flushes independently when its RAM threshold is reached.
    /// Call <see cref="InitialiseDwptPool"/> before first use.
    /// </summary>
    public void AddDocumentLockFree(LeanDocument doc)
    {
        EnterIndexingOperation();
        try
        {
            ValidateDocument(doc);

            var pool = _dwptPool ?? throw new InvalidOperationException(
                "DWPT pool not initialised. Call InitialiseDwptPool() first.");

            // Round-robin DWPT selection  --  lock-free via Interlocked
            int slot = (int)((uint)Interlocked.Increment(ref _dwptCounter) % (uint)pool.Length);
            var dwpt = pool[slot];

            // Per-DWPT lock (not global  --  only contention on the same slot)
            lock (dwpt)
            {
                dwpt.AddDocument(doc);
            }

            // Check per-DWPT RAM threshold and flush directly to a segment if needed
            long ramThreshold = (long)(_config.RamBufferSizeMB * 1024 * 1024) / pool.Length;
            if (dwpt.EstimatedRamBytes > ramThreshold)
            {
                lock (_writeLock)
                {
                    lock (dwpt)
                    {
                        if (dwpt.DocCount > 0)
                        {
                            int ordinal = _nextSegmentOrdinal++;
                            long seqEnd = 0, seqStart = 0;
                            if (_config.TrackSequenceNumbers)
                            {
                                seqEnd = Interlocked.Add(ref _nextSequenceNumber, dwpt.DocCount);
                                seqStart = seqEnd - dwpt.DocCount;
                            }

                            var segInfo = SegmentFlusher.FlushFromDwpt(
                                dwpt, _config, _directory.DirectoryPath,
                                ordinal, _commitGeneration,
                                seqStart, seqEnd,
                                out _);

                            _committedSegments.Add(segInfo);
                            _contentChangedSinceCommit = true;
                            dwpt.ClearAll();
                        }
                    }
                }
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Indexes a batch of documents using parallel per-thread writer buffers (DWPT).
    /// Each partition flushes its own segment to disk; the
    /// <see cref="IMergePolicy"/> consolidates them later.
    /// </summary>
    /// <param name="documents">The documents to index concurrently.</param>
    /// <exception cref="ObjectDisposedException">Thrown if the writer has been disposed.</exception>
    public void AddDocumentsConcurrent(IReadOnlyList<Document.LeanDocument> documents)
    {
        EnterIndexingOperation();
        try
        {
            ArgumentNullException.ThrowIfNull(documents);
            if (documents.Count == 0) return;

            ValidateDocuments(documents);

            var newSegments = new System.Collections.Concurrent.ConcurrentBag<SegmentInfo>();

            Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, documents.Count),
                () => CreateThreadLocalDocumentWriter(),
                (range, _, dwpt) =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                        dwpt.AddDocument(documents[i]);

                    if (dwpt.DocCount == 0) return dwpt;

                    int ordinal = Interlocked.Increment(ref _nextSegmentOrdinal) - 1;
                    // Reserve a contiguous block of sequence numbers atomically.
                    long seqEnd = 0, seqStart = 0;
                    if (_config.TrackSequenceNumbers)
                    {
                        seqEnd = Interlocked.Add(ref _nextSequenceNumber, dwpt.DocCount);
                        seqStart = seqEnd - dwpt.DocCount;
                    }

                    var segInfo = SegmentFlusher.FlushFromDwpt(
                        dwpt, _config, _directory.DirectoryPath,
                        ordinal, _commitGeneration,
                        seqStart, seqEnd,
                        out int _unused);

                    newSegments.Add(segInfo);
                    _contentChangedSinceCommit = true;
                    dwpt.ClearAll();
                    return dwpt;
                },
                dwpt => { });

            lock (_writeLock)
            {
                foreach (var seg in newSegments)
                    _committedSegments.Add(seg);
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Flushes any remaining DWPT pool contents to segments.
    /// Called during commit to drain partial DWPT buffers.
    /// </summary>
    private void FlushDwptPool()
    {
        var pool = _dwptPool;
        if (pool == null) return;

        foreach (var dwpt in pool)
        {
            lock (dwpt)
            {
                if (dwpt.DocCount == 0) continue;

                int ordinal = _nextSegmentOrdinal++;
                long seqEnd = 0, seqStart = 0;
                if (_config.TrackSequenceNumbers)
                {
                    seqEnd = Interlocked.Add(ref _nextSequenceNumber, dwpt.DocCount);
                    seqStart = seqEnd - dwpt.DocCount;
                }

                var segInfo = SegmentFlusher.FlushFromDwpt(
                    dwpt, _config, _directory.DirectoryPath,
                    ordinal, _commitGeneration,
                    seqStart, seqEnd,
                    out _);

                _committedSegments.Add(segInfo);
                _contentChangedSinceCommit = true;
                dwpt.ClearAll();
            }
        }
    }

    /// <summary>
    /// Creates a DocumentsWriterPerThread with fresh analyser instances for thread-safe parallel indexing.
    /// </summary>
    private DocumentsWriterPerThread CreateThreadLocalDocumentWriter()
    {
        IAnalyser threadLocalDefaultAnalyser = _defaultAnalyser switch
        {
            StandardAnalyser => new StandardAnalyser(_config.AnalyserInternCacheSize, _config.StopWords),
            WhitespaceAnalyser => new WhitespaceAnalyser(_config.AnalyserInternCacheSize),
            KeywordAnalyser => new KeywordAnalyser(_config.AnalyserInternCacheSize),
            SimpleAnalyser => new SimpleAnalyser(_config.AnalyserInternCacheSize),
            StemmedAnalyser => new StemmedAnalyser(),
            Analyser a => a.Clone(),
            _ => _defaultAnalyser
        };

        var threadLocalFieldAnalysers = new Dictionary<string, IAnalyser>(_config.FieldAnalysers.Count);
        foreach (var kvp in _config.FieldAnalysers)
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

        return new DocumentsWriterPerThread(threadLocalDefaultAnalyser, threadLocalFieldAnalysers, _config.StorePayloads);
    }

}
