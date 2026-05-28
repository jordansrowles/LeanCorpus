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

            // Check per-DWPT RAM threshold and flush if needed
            long ramThreshold = (long)(_config.RamBufferSizeMB * 1024 * 1024) / pool.Length;
            if (dwpt.EstimatedRamBytes > ramThreshold)
            {
                lock (_writeLock)
                {
                    lock (dwpt)
                    {
                        if (dwpt.DocCount > 0)
                        {
                            MergeDwpt(dwpt);
                            ResetDwpt(dwpt);
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
    /// Partitions the input across all available processors and merges results into the
    /// main buffer under a single lock acquisition per DWPT.
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

            // Validate every document up front so a single bad doc fails the call,
            // not silently corrupts the index on a per-partition basis.
            ValidateDocuments(documents);

            var perThreadResults = new System.Collections.Concurrent.ConcurrentBag<DocumentsWriterPerThread>();

            Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, documents.Count),
                () => CreateThreadLocalDocumentWriter(),
                (range, _, dwpt) =>
                {
                    for (int i = range.Item1; i < range.Item2; i++)
                        dwpt.AddDocument(documents[i]);
                    return dwpt;
                },
                dwpt => perThreadResults.Add(dwpt));

            lock (_writeLock)
            {
                foreach (var dwpt in perThreadResults)
                    MergeDwpt(dwpt);
            }
        }
        finally
        {
            ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Flushes all DWPT pool buffers into the main buffer and then flushes to disk.
    /// Called during <see cref="Commit"/> to ensure all buffered data is persisted.
    /// </summary>
    private void FlushDwptPool()
    {
        var pool = _dwptPool;
        if (pool == null) return;

        foreach (var dwpt in pool)
        {
            lock (dwpt)
            {
                if (dwpt.DocCount > 0)
                {
                    MergeDwpt(dwpt);
                    ResetDwpt(dwpt);
                }
            }
        }
    }

    /// <summary>
    /// Resets a DWPT to empty state after its contents have been merged.
    /// </summary>
    private static void ResetDwpt(DocumentsWriterPerThread dwpt)
    {
        dwpt.ClearAll();
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

    private void MergeDwpt(DocumentsWriterPerThread dwpt)
    {
        int docBase = _buffer.DocCount;
        foreach (var (qt, srcAcc) in dwpt.Postings)
        {
            if (!_buffer.Postings.TryGetValue(qt, out var dstAcc))
            {
                dstAcc = new PostingAccumulator();
                _buffer.Postings[qt] = dstAcc;
                _buffer.PostingsRamBytes += dstAcc.EstimatedBytes;
            }
            var srcIds = srcAcc.DocIds;
            bool srcHasPositions = srcAcc.HasPositions;
            for (int i = 0; i < srcIds.Length; i++)
            {
                int remappedDocId = srcIds[i] + docBase;
                long before = dstAcc.EstimatedBytes;
                if (srcHasPositions)
                {
                    if (srcAcc.HasPayloads)
                    {
                        var positions = srcAcc.GetPositions(i);
                        var payloads = new byte[]?[positions.Length];
                        for (int p = 0; p < positions.Length; p++)
                            payloads[p] = srcAcc.GetPayload(i, p);
                        dstAcc.AddPositionsWithPayloads(remappedDocId, positions, payloads);
                    }
                    else
                    {
                        dstAcc.AddPositions(remappedDocId, srcAcc.GetPositions(i));
                    }
                }
                else
                {
                    dstAcc.AddDocOnly(remappedDocId);
                }
                _buffer.PostingsRamBytes += dstAcc.EstimatedBytes - before;
            }
        }

        int dwptDocCount = dwpt.DocCount;
        var srcDocStarts = dwpt.StoredDocStarts;
        var srcFieldIds = dwpt.StoredFieldIds;
        var srcValues = dwpt.StoredValues;
        var srcIdToName = dwpt.StoredFieldIdToName;
        int srcEntryTotal = srcFieldIds.Count;
        for (int d = 0; d < dwptDocCount; d++)
        {
            _buffer.StoredDocStarts.Add(_buffer.StoredFieldIds.Count);
            int start = srcDocStarts[d];
            int end = (d + 1) < dwptDocCount ? srcDocStarts[d + 1] : srcEntryTotal;
            for (int e = start; e < end; e++)
            {
                AppendMergedStoredField(srcIdToName[srcFieldIds[e]], srcValues[e]);
            }
        }

        foreach (var (fieldName, counts) in dwpt.DocTokenCounts)
        {
            if (!_buffer.DocTokenCounts.TryGetValue(fieldName, out var dstCounts))
            {
                dstCounts = new int[_config.MaxBufferedDocs];
                _buffer.DocTokenCounts[fieldName] = dstCounts;
            }

            int newTotal = docBase + dwpt.DocCount;
            if (newTotal > dstCounts.Length)
            {
                Array.Resize(ref dstCounts, Math.Max(dstCounts.Length * 2, newTotal));
                _buffer.DocTokenCounts[fieldName] = dstCounts;
            }

            for (int i = 0; i < dwpt.DocCount && i < counts.Length; i++)
                dstCounts[docBase + i] = counts[i];
        }

        foreach (var (boostFieldName, boosts) in dwpt.FieldBoosts)
        {
            if (!_buffer.FieldBoosts.TryGetValue(boostFieldName, out var dstBoosts))
            {
                dstBoosts = new Dictionary<int, float>();
                _buffer.FieldBoosts[boostFieldName] = dstBoosts;
            }

            foreach (var (docId, boost) in boosts)
                dstBoosts[docBase + docId] = boost;
        }

        foreach (var fn in dwpt.FieldNames)
            _buffer.FieldNames.Add(fn);

        foreach (var (field, map) in dwpt.NumericIndex)
        {
            if (!_buffer.NumericIndex.TryGetValue(field, out var dstMap))
            {
                dstMap = new Dictionary<int, double>();
                _buffer.NumericIndex[field] = dstMap;
            }
            foreach (var (docId, val) in map)
                dstMap[docId + docBase] = val;
        }

        foreach (var (field, list) in dwpt.NumericDocValues)
        {
            if (!_buffer.NumericDocValues.TryGetValue(field, out var dstList))
            {
                dstList = new List<double>();
                _buffer.NumericDocValues[field] = dstList;
            }
            while (dstList.Count < docBase) dstList.Add(0);
            dstList.AddRange(list);
        }

        // Sorted doc values: pad to docBase, then copy each per-doc slot into the writer's column.
        foreach (var (field, list) in dwpt.SortedDocValues)
        {
            if (!_buffer.SortedDocValues.TryGetValue(field, out var dstList))
            {
                dstList = new List<string?>();
                _buffer.SortedDocValues[field] = dstList;
            }
            while (dstList.Count < docBase) dstList.Add(null);
            for (int i = 0; i < dwpt.DocCount; i++)
            {
                int globalDocId = docBase + i;
                while (dstList.Count <= globalDocId) dstList.Add(null);
                if (i < list.Count)
                    dstList[globalDocId] = list[i];
            }
        }

        MergeMultiValuedDocValues(dwpt.SortedSetDocValues, _buffer.SortedSetDocValues, docBase);
        MergeMultiValuedDocValues(dwpt.SortedNumericDocValues, _buffer.SortedNumericDocValues, docBase);
        MergeMultiValuedDocValues(dwpt.BinaryDocValues, _buffer.BinaryDocValues, docBase);

        // Vectors: remap per-doc keys into the writer's docId space.
        foreach (var (field, perField) in dwpt.Vectors)
        {
            if (!_buffer.Vectors.TryGetValue(field, out var dstPerField))
            {
                dstPerField = new Dictionary<int, ReadOnlyMemory<float>>();
                _buffer.Vectors[field] = dstPerField;
            }
            foreach (var (localDocId, vec) in perField)
                dstPerField[localDocId + docBase] = vec;
        }

        _buffer.DocCount += dwpt.DocCount;
        _contentChangedSinceCommit = true;
        if (ShouldFlush())
            FlushSegment();
    }

    private void AppendMergedStoredField(string fieldName, StoredFieldValue value)
    {
        if (!_buffer.StoredFieldNameToId.TryGetValue(fieldName, out int fid))
        {
            fid = _buffer.StoredFieldIdToName.Count;
            _buffer.StoredFieldNameToId[fieldName] = fid;
            _buffer.StoredFieldIdToName.Add(fieldName);
        }

        _buffer.StoredFieldIds.Add(fid);
        _buffer.StoredFieldValues.Add(value);
        _buffer.EstimatedRamBytes += value.EstimatedSize;
    }

    private static void MergeMultiValuedDocValues<T>(
        Dictionary<string, Dictionary<int, List<T>>> source,
        Dictionary<string, Dictionary<int, List<T>>> destination,
        int docBase)
    {
        foreach (var (field, sourceMap) in source)
        {
            if (!destination.TryGetValue(field, out var destinationMap))
            {
                destinationMap = new Dictionary<int, List<T>>();
                destination[field] = destinationMap;
            }

            foreach (var (localDocId, values) in sourceMap)
                destinationMap[docBase + localDocId] = [.. values];
        }
    }
}
