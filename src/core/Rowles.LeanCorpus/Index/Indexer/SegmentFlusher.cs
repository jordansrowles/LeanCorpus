using System.Buffers;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;
namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Pure function: takes <see cref="DocumentBufferState"/> and writes a segment to disk.
/// All helpers are static, operating only on the buffer, config, and path state passed in.
/// </summary>
internal static class SegmentFlusher
{
    public static SegmentInfo Flush(
        DocumentBufferState buffer,
        IndexWriterConfig config,
        string directoryPath,
        ref int nextSegmentOrdinal,
        int commitGeneration,
        long flushSeqNoStart,
        long nextSequenceNumber)
    {
        var segId = $"seg_{nextSegmentOrdinal++}";
        var segInfo = FlushCore(new BufferFlushSource(buffer), config, directoryPath, segId,
            commitGeneration, flushSeqNoStart, nextSequenceNumber, minDocsForHnsw: 0);

        var basePath = Path.Combine(directoryPath, segId);

        // Term vectors
        if (config.StoreTermVectors)
        {
            WriteTermVectors(basePath, buffer.DocCount, buffer.EnumeratePostings());
        }

        // Parent bitset
        if (buffer.ParentDocIds is { Count: > 0 })
        {
            var pbs = new ParentBitSet(buffer.DocCount);
            foreach (var pid in buffer.ParentDocIds)
                pbs.Set(pid);
            pbs.WriteTo(basePath + ".pbs");
        }

        CompleteSegment(segInfo, config, directoryPath);

        return segInfo;
    }

    private static SegmentInfo FlushCore(
        IFlushSource source,
        IndexWriterConfig config,
        string directoryPath,
        string segId,
        int commitGeneration,
        long flushSeqNoStart,
        long nextSequenceNumber,
        int minDocsForHnsw)
    {
        // Apply index-time sorting for every flush source. DWPT and detached
        // snapshot flushes use this path as well as the original buffer flush,
        // so sorted metadata cannot get out of sync with physical doc order.
        if (config.IndexSort is not null)
        {
            var sortPerm = ComputeSortPermutation(source, config.IndexSort);
            var inversePerm = new int[source.DocCount];
            for (int i = 0; i < source.DocCount; i++)
                inversePerm[sortPerm[i]] = i;
            ApplySortPermutation(source, sortPerm, inversePerm);
        }

        var flushSw = System.Diagnostics.Stopwatch.StartNew();
        using var flushActivity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.Flush);

        int docCount = source.DocCount;

        var basePath = Path.Combine(directoryPath, segId);
        flushActivity?.SetTag("index.segment_id", segId);
        flushActivity?.SetTag("index.doc_count", docCount);

        var fieldNames = source.FieldNames.ToList();

        var segInfo = new SegmentInfo
        {
            SegmentId = segId,
            DocCount = docCount,
            LiveDocCount = docCount,
            CommitGeneration = commitGeneration,
            FieldNames = fieldNames,
            IndexSortFields = config.IndexSort?.SerialisedFields,
            MinSequenceNumber = config.TrackSequenceNumbers ? flushSeqNoStart : null,
            MaxSequenceNumber = config.TrackSequenceNumbers ? nextSequenceNumber - 1 : null
        };
        segInfo.WriteTo(basePath + ".seg");

        // Norms and field lengths (computed before postings so impact metadata can carry norms).
        var normFields = new HashSet<string>(source.DocTokenCounts.Keys, StringComparer.Ordinal);
        foreach (var fieldName in source.FieldBoosts.Keys)
            normFields.Add(fieldName);

        var fieldNorms = new Dictionary<string, float[]>(normFields.Count, StringComparer.Ordinal);
        var quantisedNorms = new Dictionary<string, byte[]>(normFields.Count, StringComparer.Ordinal);
        var fieldLengths = new Dictionary<string, int[]>(source.DocTokenCounts.Count, StringComparer.Ordinal);
        var normsReturnList = new List<float[]>(normFields.Count);
        var lengthsReturnList = new List<int[]>(source.DocTokenCounts.Count);
        foreach (var fieldName in normFields)
        {
            source.DocTokenCounts.TryGetValue(fieldName, out var counts);
            var norms = ArrayPool<float>.Shared.Rent(docCount);
            var qNorms = new byte[docCount];
            int[]? lengths = counts is not null ? ArrayPool<int>.Shared.Rent(docCount) : null;
            int countsLen = counts?.Length ?? 0;
            for (int i = 0; i < docCount; i++)
            {
                int tokenCount = counts is not null
                    ? (i < countsLen ? counts[i] : 0)
                    : 1;
                if (lengths is not null)
                    lengths[i] = tokenCount;
                float norm = 1.0f / (1.0f + Math.Max(1, tokenCount));
                norms[i] = norm;
                qNorms[i] = NormsWriter.QuantiseNorm(norm);
            }
            fieldNorms[fieldName] = norms;
            quantisedNorms[fieldName] = qNorms;
            if (lengths is not null)
                fieldLengths[fieldName] = lengths;
            normsReturnList.Add(norms);
            if (lengths is not null)
                lengthsReturnList.Add(lengths);
        }

        // Sort by UTF-8 byte order and build the dictionary without re-encoding.
        int postingsCount = source.PostingsCount;
        var byteTerms = new (byte[] TermUtf8, PostingAccumulator Acc)[postingsCount];
        source.CopySortedPostingsUtf8(byteTerms);
        Array.Sort(byteTerms, static (a, b) => a.TermUtf8.AsSpan().SequenceCompareTo(b.TermUtf8));

        // Convert to string once for WritePostingsBody (needs strings for field name extraction).
        var stringTerms = new (string Term, PostingAccumulator Acc)[postingsCount];
        var utf8Terms = new (byte[] KeyUtf8, long Output)[postingsCount];
        for (int i = 0; i < postingsCount; i++)
        {
            stringTerms[i] = (System.Text.Encoding.UTF8.GetString(byteTerms[i].TermUtf8), byteTerms[i].Acc);
            utf8Terms[i] = (byteTerms[i].TermUtf8, 0);
        }

        var (postingsOffsets, sortedTermsList) = WritePostingsBody(stringTerms, basePath, quantisedNorms);

        // Map offsets back to byte-sorted order for FST construction.
        for (int i = 0; i < postingsCount; i++)
            utf8Terms[i].Output = postingsOffsets[sortedTermsList[i]];
        var fstBlob = TermDictionaryWriter.BuildFstUtf8(utf8Terms);
        TermDictionaryWriter.WriteBlob(basePath + ".dic", fstBlob);

        NormsWriter.Write(basePath + ".nrm", fieldNorms, docCount: docCount, sparseFieldBoosts: source.FieldBoosts);
        foreach (var arr in normsReturnList) ArrayPool<float>.Shared.Return(arr, clearArray: false);

        FieldLengthWriter.Write(basePath + ".fln", fieldLengths, docCount);
        SegmentStats.FromFieldLengths(docCount, docCount, fieldNames, fieldLengths)
            .WriteTo(SegmentStats.GetStatsPath(directoryPath, segId));
        foreach (var arr in lengthsReturnList) ArrayPool<int>.Shared.Return(arr, clearArray: false);

        // Stored fields
        StoredFieldsWriter.Write(basePath + ".fdt", basePath + ".fdx",
            source.StoredDocStarts, source.StoredFieldIds, source.StoredFieldValues, source.StoredFieldIdToName,
            config.StoredFieldBlockSize, config.CompressionPolicy);

        // Numeric field index
        WriteNumericIndex(source.NumericIndex, basePath + ".num");

        // 64-bit integer field index
        WriteInt64Index(source.Int64Index, basePath + ".numl");

        // Vectors
        if (source.Vectors.Count > 0)
        {
            foreach (var (fieldName, perField) in source.Vectors)
            {
                if (perField.Count == 0) continue;

                int dimension = 0;
                foreach (var v in perField.Values)
                {
                    if (v.Length > 0) { dimension = v.Length; break; }
                }
                if (dimension == 0) continue;

                if (config.NormaliseVectors)
                {
                    var keys = perField.Keys.ToArray();
                    foreach (var k in keys)
                    {
                        var v = perField[k];
                        if (v.Length != dimension) continue;
                        var copy = v.ToArray();
                        if (Search.Simd.SimdVectorOps.NormaliseInPlace(copy))
                            perField[k] = copy;
                    }
                }
                var quantisation = config.VectorQuantisation;
                float int8Min = 0f, int8Alpha = 0f;
                float[]? bbqCentroid = null;

                if (quantisation == VectorQuantisation.None)
                {
                    var vecPath = Codecs.Vectors.VectorFilePaths.VectorFile(basePath, fieldName);
                    Codecs.Vectors.VectorWriter.WriteField(vecPath, docCount, dimension, perField, quantisation);
                }
                else
                {
                    switch (quantisation)
                    {
                        case VectorQuantisation.Int8:
                            (int8Min, int8Alpha) = ComputeInt8Params(perField);
                            break;
                        case VectorQuantisation.BBQ:
                            bbqCentroid = ComputeBBQCentroid(perField, dimension);
                            break;
                    }
                }

                bool hasHnsw = false;
                if (config.BuildHnswOnFlush && perField.Count >= 2 && perField.Count >= minDocsForHnsw)
                {
                    var docIds = perField.Keys.ToArray();
                    var hnswSw = System.Diagnostics.Stopwatch.StartNew();
                    Codecs.Hnsw.HnswGraph graph;

                    if (quantisation == VectorQuantisation.Int8)
                    {
                        var int8Source = new Codecs.Vectors.Int8QuantisedMemoryVectorSource(perField, dimension, int8Min, int8Alpha);
                        var vqPath = Codecs.Vectors.VectorFilePaths.QuantisedVectorFile(basePath, fieldName);
                        Codecs.Vectors.QuantisedVectorWriter.WriteInt8(vqPath, docCount, dimension, perField);
                        graph = Codecs.Hnsw.HnswGraphBuilder.Build(int8Source, docIds, config.HnswBuildConfig, config.HnswSeed);
                    }
                    else if (quantisation == VectorQuantisation.BBQ)
                    {
                        var bbqSource = new Codecs.Vectors.BBQMemoryVectorSource(perField, dimension, bbqCentroid!);
                        var vqPath = Codecs.Vectors.VectorFilePaths.QuantisedVectorFile(basePath, fieldName);
                        Codecs.Vectors.QuantisedVectorWriter.WriteBBQ(vqPath, docCount, dimension, perField, bbqCentroid!);
                        graph = Codecs.Hnsw.HnswGraphBuilder.Build(bbqSource, docIds, config.HnswBuildConfig, config.HnswSeed);
                    }
                    else
                    {
                        var memSource = new Dictionary<int, ReadOnlyMemory<float>>(perField);
                        var vectorSource = new Codecs.Vectors.InMemoryVectorSource(memSource, dimension);
                        graph = Codecs.Hnsw.HnswGraphBuilder.Build(vectorSource, docIds, config.HnswBuildConfig, config.HnswSeed);
                    }
                    hnswSw.Stop();
                    config.Metrics.RecordHnswBuild(hnswSw.Elapsed, docIds.Length);
                    var hnswPath = Codecs.Vectors.VectorFilePaths.HnswFile(basePath, fieldName);
                    Codecs.Hnsw.HnswWriter.Write(hnswPath, graph, dimension, config.NormaliseVectors);
                    hasHnsw = true;
                }
                else if (quantisation != VectorQuantisation.None)
                {
                    var vqPath = Codecs.Vectors.VectorFilePaths.QuantisedVectorFile(basePath, fieldName);
                    switch (quantisation)
                    {
                        case VectorQuantisation.Int8:
                            Codecs.Vectors.QuantisedVectorWriter.WriteInt8(vqPath, docCount, dimension, perField);
                            break;
                        case VectorQuantisation.BBQ:
                            Codecs.Vectors.QuantisedVectorWriter.WriteBBQ(vqPath, docCount, dimension, perField, bbqCentroid!);
                            break;
                    }
                }

                segInfo.VectorFields.Add(new VectorFieldInfo
                {
                    FieldName = fieldName,
                    Dimension = dimension,
                    Normalised = config.NormaliseVectors,
                    Quantisation = quantisation,
                    HasHnsw = hasHnsw,
                });
            }

            segInfo.WriteTo(basePath + ".seg");
        }

        // DocValues
        if (source.NumericDocValues.Count > 0)
        {
            var dvn = new Dictionary<string, double[]>(source.NumericDocValues.Count, StringComparer.Ordinal);
            var dvnReturnList = new List<double[]>(source.NumericDocValues.Count);
            var dvnPresence = new Dictionary<string, IReadOnlySet<int>>(source.NumericIndex.Count, StringComparer.Ordinal);
            foreach (var (field, list) in source.NumericDocValues)
            {
                var arr = ArrayPool<double>.Shared.Rent(docCount);
                Array.Clear(arr, 0, docCount);
                for (int i = 0; i < Math.Min(list.Count, docCount); i++)
                    arr[i] = list[i];
                dvn[field] = arr;
                dvnReturnList.Add(arr);
                if (source.NumericIndex.TryGetValue(field, out var sparseMap))
                    dvnPresence[field] = sparseMap.Keys.ToHashSet();
            }
            NumericDocValuesWriter.Write(basePath + ".dvn", dvn, docCount, dvnPresence);
            foreach (var arr in dvnReturnList) ArrayPool<double>.Shared.Return(arr, clearArray: false);
        }

        if (source.Int64DocValues.Count > 0)
        {
            var dvnl = new Dictionary<string, long[]>(source.Int64DocValues.Count, StringComparer.Ordinal);
            var dvnlReturnList = new List<long[]>(source.Int64DocValues.Count);
            var dvnlPresence = new Dictionary<string, IReadOnlySet<int>>(source.Int64Index.Count, StringComparer.Ordinal);
            foreach (var (field, list) in source.Int64DocValues)
            {
                var arr = ArrayPool<long>.Shared.Rent(docCount);
                Array.Clear(arr, 0, docCount);
                for (int i = 0; i < Math.Min(list.Count, docCount); i++)
                    arr[i] = list[i];
                dvnl[field] = arr;
                dvnlReturnList.Add(arr);
                if (source.Int64Index.TryGetValue(field, out var sparseMap))
                    dvnlPresence[field] = sparseMap.Keys.ToHashSet();
            }
            Int64DocValuesWriter.Write(basePath + ".dvnl", dvnl, docCount, dvnlPresence);
            foreach (var arr in dvnlReturnList) ArrayPool<long>.Shared.Return(arr, clearArray: false);
        }

        if (source.SortedDocValues.Count > 0)
        {
            var dvs = new Dictionary<string, string?[]>(source.SortedDocValues.Count, StringComparer.Ordinal);
            foreach (var (field, list) in source.SortedDocValues)
            {
                var arr = new string?[docCount];
                for (int i = 0; i < Math.Min(list.Count, docCount); i++)
                    arr[i] = list[i];
                dvs[field] = arr;
            }
            SortedDocValuesWriter.Write(basePath + ".dvs", dvs, docCount);
        }

        if (source.SortedSetDocValues.Count > 0)
            SortedSetDocValuesWriter.Write(basePath + ".dss", ToDenseMultiValueColumns(source.SortedSetDocValues, docCount), docCount);

        if (source.SortedNumericDocValues.Count > 0)
            SortedNumericDocValuesWriter.Write(basePath + ".dsn", ToDenseMultiValueColumns(source.SortedNumericDocValues, docCount), docCount);

        if (source.Int64SortedDocValues.Count > 0)
            Int64SortedNumericDocValuesWriter.Write(basePath + ".dsnl", ToDenseMultiValueColumns(source.Int64SortedDocValues, docCount), docCount);

        if (source.BinaryDocValues.Count > 0)
            BinaryDocValuesWriter.Write(basePath + ".dvb", ToDenseMultiValueColumns(source.BinaryDocValues, docCount), docCount);

        // BKD tree
        if (source.NumericIndex.Count > 0)
        {
            var bkdData = new Dictionary<string, List<(double Value, int DocId)>>(source.NumericIndex.Count, StringComparer.Ordinal);
            foreach (var (field, docMap) in source.NumericIndex)
            {
                var points = new List<(double Value, int DocId)>(docMap.Count);
                foreach (var (docId, value) in docMap)
                {
                    if (docId < docCount)
                        points.Add((value, docId));
                }
                if (points.Count > 0)
                    bkdData[field] = points;
            }
            if (bkdData.Count > 0)
                BKDWriter.Write(basePath + ".bkd", bkdData, config.BKDMaxLeafSize);
        }

        // 64-bit integer BKD tree
        if (source.Int64Index.Count > 0)
        {
            var int64BkdData = new Dictionary<string, List<(long Value, int DocId)>>(source.Int64Index.Count, StringComparer.Ordinal);
            foreach (var (field, docMap) in source.Int64Index)
            {
                var points = new List<(long Value, int DocId)>(docMap.Count);
                foreach (var (docId, value) in docMap)
                {
                    if (docId < docCount)
                        points.Add((value, docId));
                }
                if (points.Count > 0)
                    int64BkdData[field] = points;
            }
            if (int64BkdData.Count > 0)
                Int64BKDWriter.Write(basePath + ".bkdl", int64BkdData, config.BKDMaxLeafSize);
        }

        flushSw.Stop();
        config.Metrics.RecordFlush(flushSw.Elapsed);
        long codecBytes = 0;
        foreach (var path in FileOpenRetry.GetFiles(directoryPath, segId + ".*"))
            codecBytes += FileOpenRetry.GetFileLength(path);
        foreach (var path in FileOpenRetry.GetFiles(directoryPath, segId + "_v_*.*"))
            codecBytes += FileOpenRetry.GetFileLength(path);
        config.Metrics.RecordCodecFlush("all", flushSw.Elapsed, codecBytes);

        return segInfo;
    }

    private static void WriteTermVectors(
        string basePath, int docCount, IEnumerable<(string Term, PostingAccumulator Acc)> terms)
    {
        var tvDocs = new Dictionary<string, List<TermVectorEntry>>?[docCount];

        foreach (var (qt, acc) in terms)
        {
            if (!acc.HasPositions) continue;
            int sep = qt.IndexOf('\x00');
            if (sep < 0) continue;
            string fld = qt[..sep];
            string trm = qt[(sep + 1)..];

            var ids = acc.DocIds;
            for (int i = 0; i < ids.Length; i++)
            {
                int docId = ids[i];
                if (docId >= docCount) continue;
                var perDoc = tvDocs[docId] ??= new Dictionary<string, List<TermVectorEntry>>(StringComparer.Ordinal);
                if (!perDoc.TryGetValue(fld, out var termsList))
                {
                    termsList = [];
                    perDoc[fld] = termsList;
                }
                int freq = acc.GetFreq(i);
                var posSpan = acc.GetPositions(i);
                var positions = posSpan.IsEmpty ? [] : posSpan.ToArray();
                byte[]?[]? payloads = null;
                if (acc.HasPayloads && positions.Length > 0)
                {
                    payloads = new byte[]?[positions.Length];
                    for (int p = 0; p < positions.Length; p++)
                        payloads[p] = acc.GetPayload(i, p);
                }
                var (starts, ends) = acc.GetOffsets(i);
                termsList.Add(new TermVectorEntry(trm, freq, positions, payloads, starts, ends));
            }
        }
        TermVectorsWriter.Write(basePath + ".tvd", basePath + ".tvx", tvDocs);
    }

    /// <summary>
    /// Writes a segment directly from a <see cref="DocumentsWriterPerThread"/> buffer
    /// without merging into the main <see cref="DocumentBufferState"/>. Each DWPT
    /// partition becomes its own segment; the <see cref="IMergePolicy"/> consolidates
    /// them later.
    /// </summary>
    public static SegmentInfo FlushFromDwpt(
        DocumentsWriterPerThread dwpt,
        IndexWriterConfig config,
        string directoryPath,
        int nextSegmentOrdinal,
        int commitGeneration,
        long flushSeqNoStart,
        long nextSequenceNumber,
        out int nextOrdinal)
    {
        var segId = $"seg_{nextSegmentOrdinal}";
        nextOrdinal = nextSegmentOrdinal + 1;
        var segInfo = FlushCore(new DwptFlushSource(dwpt), config, directoryPath, segId,
            commitGeneration, flushSeqNoStart, nextSequenceNumber, minDocsForHnsw: 0);

        var basePath = Path.Combine(directoryPath, segId);

        // Term vectors
        if (config.StoreTermVectors)
        {
            WriteTermVectors(basePath, dwpt.DocCount,
                dwpt.EnumeratePostings());
        }

        // Parent bitset: DWPT always has null ParentDocIds (not supported on concurrent path).
        if (dwpt.ParentDocIds is { Count: > 0 })
        {
            var pbs = new ParentBitSet(dwpt.DocCount);
            foreach (var pid in dwpt.ParentDocIds)
                pbs.Set(pid);
            pbs.WriteTo(basePath + ".pbs");
        }

        CompleteSegment(segInfo, config, directoryPath);

        return segInfo;
    }

    /// <summary>
    /// Writes a segment directly from a <see cref="DwptFlushSnapshot"/> captured earlier.
    /// This is the detached-flush entry point: flush I/O runs without holding
    /// <see cref="IndexWriter.WriteLock"/>, and the caller briefly acquires the lock
    /// afterwards to publish the returned <see cref="SegmentInfo"/>.
    /// </summary>
    public static SegmentInfo FlushFromSnapshot(
        DwptFlushSnapshot snapshot,
        IndexWriterConfig config,
        string directoryPath,
        int ordinal,
        int commitGeneration,
        long seqStart,
        long seqEnd)
    {
        var segId = $"seg_{ordinal}";
        var segInfo = FlushCore(new SnapshotFlushSource(snapshot), config, directoryPath, segId,
            commitGeneration, seqStart, seqEnd, minDocsForHnsw: 0);

        var basePath = Path.Combine(directoryPath, segId);

        // Term vectors
        if (config.StoreTermVectors)
        {
            WriteTermVectors(basePath, snapshot.DocCount,
                snapshot.EnumeratePostings());
        }

        // Parent bitset
        if (snapshot.ParentDocIds is { Count: > 0 })
        {
            var pbs = new ParentBitSet(snapshot.DocCount);
            foreach (var pid in snapshot.ParentDocIds)
                pbs.Set(pid);
            pbs.WriteTo(basePath + ".pbs");
        }

        CompleteSegment(segInfo, config, directoryPath);

        return segInfo;
    }

    internal static void RefreshSegmentSize(SegmentInfo segment, string directoryPath)
    {
        long totalBytes = 0;
        var codecBytes = new Dictionary<string, long>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string pattern in new[]
        {
            segment.SegmentId + ".*",
            segment.SegmentId + "_gen_*.del",
            segment.SegmentId + "_v_*.*"
        })
        {
            foreach (var path in FileOpenRetry.GetFiles(directoryPath, pattern))
                paths.Add(path);
        }

        foreach (var path in paths)
        {
            var metadata = FileOpenRetry.GetFileMetadata(path);
            totalBytes += metadata.Length;
            codecBytes[metadata.Extension] = codecBytes.GetValueOrDefault(metadata.Extension) + metadata.Length;
        }

        segment.TotalBytes = totalBytes;
        segment.CodecBytes = codecBytes;
        segment.WriteTo(Path.Combine(directoryPath, segment.SegmentId + ".seg"));
    }

    internal static void CompleteSegment(SegmentInfo segment, IndexWriterConfig config, string directoryPath)
    {
        if (config.UseCompoundFile && CompoundFileWriter.Pack(directoryPath, segment.SegmentId))
            segment.IsCompoundFile = true;
        RefreshSegmentSize(segment, directoryPath);
    }

    /// <summary>
    /// Writes the .pos postings body for a sorted array of (term, accumulator) pairs.
    /// Returns postings offsets (keyed by qualified term) and the sorted term list
    /// for the term dictionary. Uses the v4 sequential body-then-metadata layout.
    /// </summary>
    private static (Dictionary<string, long> PostingsOffsets, List<string> SortedTerms) WritePostingsBody(
        (string Term, PostingAccumulator Acc)[] accumulatorTerms,
        string basePath,
        IReadOnlyDictionary<string, byte[]> quantisedNorms)
    {
        int postingsCount = accumulatorTerms.Length;
        var postingsOffsets = new Dictionary<string, long>(postingsCount, StringComparer.Ordinal);
        var sortedTerms = new List<string>(postingsCount);

        string posPath = basePath + ".pos";
        using (var posOutput = new IndexOutput(posPath))
        {
            var descriptor = CodecCatalog.Default.GetFile("leancorpus.postings.data");
            using var frame = CodecFileWriter.Begin(posOutput, descriptor);
            var bodyOutput = frame.Output;

            using var blockWriter = new BlockPostingsWriter(bodyOutput);

            foreach (var (qt, acc) in accumulatorTerms)
            {
                sortedTerms.Add(qt);
                var ids = acc.DocIds;

                string fieldName = QualifiedTermHelpers.GetFieldName(qt).ToString();
                quantisedNorms.TryGetValue(fieldName, out var fieldNormBytes);

                bool hasFreqs = acc.HasFreqs;
                bool hasPositions = acc.HasPositions;
                bool hasPayloads = acc.HasPayloads;

                long bodyOffset = bodyOutput.Position;
                blockWriter.StartTerm();
                for (int i = 0; i < ids.Length; i++)
                {
                    int docId = ids[i];
                    byte norm = fieldNormBytes is not null && (uint)docId < (uint)fieldNormBytes.Length
                        ? fieldNormBytes[docId]
                        : (byte)0;
                    blockWriter.AddPosting(docId, hasFreqs ? acc.GetFreq(i) : 1, norm);
                }
                var meta = blockWriter.FinishTerm();

                if (hasPositions)
                {
                    for (int i = 0; i < ids.Length; i++)
                    {
                        acc.GetEncodedPositionDeltas(i, out var deltaBytes, out int firstPos, out int freq);
                        bodyOutput.WriteVarInt(freq);
                        if (freq == 0) continue;

                        bodyOutput.WriteVarInt(firstPos);
                        int prevPos = firstPos;

                        if (hasPayloads)
                        {
                            var payload0 = acc.GetPayload(i, 0);
                            if (payload0 is { Length: > 0 })
                            {
                                bodyOutput.WriteVarInt(payload0.Length);
                                bodyOutput.WriteBytes(payload0);
                            }
                            else
                            {
                                bodyOutput.WriteVarInt(0);
                            }
                        }

                        int deltaOffset = 0;
                        for (int pi = 1; pi < freq; pi++)
                        {
                            deltaOffset += PostingAccumulator.ReadVarInt(
                                deltaBytes.Slice(deltaOffset), out int delta);
                            int abs = firstPos + delta;
                            bodyOutput.WriteVarInt(abs - prevPos);
                            prevPos = abs;

                            if (hasPayloads)
                            {
                                var payload = acc.GetPayload(i, pi);
                                if (payload is { Length: > 0 })
                                {
                                    bodyOutput.WriteVarInt(payload.Length);
                                    bodyOutput.WriteBytes(payload);
                                }
                                else
                                {
                                    bodyOutput.WriteVarInt(0);
                                }
                            }
                        }
                    }
                }

                long metadataOffset = bodyOutput.Position;
                postingsOffsets[qt] = metadataOffset;
                bodyOutput.WriteInt64(bodyOffset);
                bodyOutput.WriteInt32(meta.DocFreq);
                bodyOutput.WriteInt64(meta.SkipOffset);
                bodyOutput.WriteBoolean(hasFreqs);
                bodyOutput.WriteBoolean(hasPositions);
                bodyOutput.WriteBoolean(hasPayloads);
            }

            frame.Complete();
        }

        // Metadata offsets are absolute file positions.
        return (postingsOffsets, sortedTerms);
    }



    private static int[] ComputeSortPermutation(IFlushSource buffer, IndexSort sort)
    {
        int n = buffer.DocCount;
        var perm = new int[n];
        for (int i = 0; i < n; i++) perm[i] = i;

        var fieldCount = sort.Fields.Count;
        var numericKeys = new double[fieldCount][];
        var int64Keys = new long[fieldCount][];
        var stringKeys = new string?[fieldCount][];
        var sortTypes = new SortFieldType[fieldCount];
        var descFlags = new bool[fieldCount];

        for (int f = 0; f < fieldCount; f++)
        {
            var field = sort.Fields[f];
            sortTypes[f] = field.Type;
            descFlags[f] = field.Descending;

            switch (field.Type)
            {
                case SortFieldType.Numeric:
                    var numArr = new double[n];
                    for (int i = 0; i < n; i++)
                        numArr[i] = ResolveNumericSortValue(buffer, field, i);
                    numericKeys[f] = numArr;
                    break;

                case SortFieldType.Int64:
                    var int64Arr = new long[n];
                    for (int i = 0; i < n; i++)
                        int64Arr[i] = ResolveInt64SortValue(buffer, field, i);
                    int64Keys[f] = int64Arr;
                    break;

                case SortFieldType.String:
                    var strArr = new string?[n];
                    for (int i = 0; i < n; i++)
                        strArr[i] = ResolveStringSortValue(buffer, field, i);
                    stringKeys[f] = strArr;
                    break;
            }
        }


        // Fast path: single numeric ascending sort — use keyed sort to avoid delegate per compare.
        if (fieldCount == 1 && sortTypes[0] == SortFieldType.Numeric && !descFlags[0])
        {
            Array.Sort(numericKeys[0], perm);
            return perm;
        }
        Array.Sort(perm, (a, b) =>
        {
            for (int f = 0; f < fieldCount; f++)
            {
                int cmp = sortTypes[f] switch
                {
                    SortFieldType.Numeric => numericKeys[f][a].CompareTo(numericKeys[f][b]),
                    SortFieldType.Int64 => int64Keys[f][a].CompareTo(int64Keys[f][b]),
                    SortFieldType.String => string.Compare(stringKeys[f][a], stringKeys[f][b], StringComparison.Ordinal),
                    SortFieldType.DocId => a.CompareTo(b),
                    _ => 0
                };
                if (descFlags[f]) cmp = -cmp;
                if (cmp != 0) return cmp;
            }
            return a.CompareTo(b);
        });

        return perm;
    }

    private static double ResolveNumericSortValue(IFlushSource source, SortField field, int docId)
    {
        if (source.SortedNumericDocValues.TryGetValue(field.FieldName, out var sortedValues)
            && sortedValues.TryGetValue(docId, out var multiValues)
            && multiValues.Count > 0)
        {
            return SelectNumericValue(multiValues, field.Selector);
        }

        if (source.NumericDocValues.TryGetValue(field.FieldName, out var values)
            && docId < values.Count)
            return values[docId];

        if (source.NumericIndex.TryGetValue(field.FieldName, out var indexedValues)
            && indexedValues.TryGetValue(docId, out var indexedValue))
            return indexedValue;

        return ResolveStoredDouble(source, field.FieldName, docId);
    }

    private static long ResolveInt64SortValue(IFlushSource source, SortField field, int docId)
    {
        if (source.Int64SortedDocValues.TryGetValue(field.FieldName, out var sortedValues)
            && sortedValues.TryGetValue(docId, out var multiValues)
            && multiValues.Count > 0)
        {
            return SelectInt64Value(multiValues, field.Selector);
        }

        if (source.Int64DocValues.TryGetValue(field.FieldName, out var values)
            && docId < values.Count)
            return values[docId];

        if (source.Int64Index.TryGetValue(field.FieldName, out var indexedValues)
            && indexedValues.TryGetValue(docId, out var indexedValue))
            return indexedValue;

        return ResolveStoredInt64(source, field.FieldName, docId);
    }

    private static string? ResolveStringSortValue(IFlushSource source, SortField field, int docId)
    {
        if (source.SortedDocValues.TryGetValue(field.FieldName, out var values)
            && docId < values.Count)
            return values[docId];

        if (source.SortedSetDocValues.TryGetValue(field.FieldName, out var sortedValues)
            && sortedValues.TryGetValue(docId, out var multiValues)
            && multiValues.Count > 0)
        {
            return field.Selector == SortValueSelector.Max
                ? multiValues.Max(StringComparer.Ordinal)
                : multiValues.Min(StringComparer.Ordinal);
        }

        if (source.BinaryDocValues.TryGetValue(field.FieldName, out var binaryValues)
            && binaryValues.TryGetValue(docId, out var binaryValuesForDoc)
            && binaryValuesForDoc.Count > 0)
            return System.Text.Encoding.UTF8.GetString(binaryValuesForDoc[0]);

        return ResolveStoredString(source, field.FieldName, docId);
    }

    private static double SelectNumericValue(IReadOnlyList<double> values, SortValueSelector selector)
    {
        double selected = values[0];
        for (int i = 1; i < values.Count; i++)
        {
            if (selector == SortValueSelector.Max ? values[i] > selected : values[i] < selected)
                selected = values[i];
        }
        return selected;
    }

    private static long SelectInt64Value(IReadOnlyList<long> values, SortValueSelector selector)
    {
        long selected = values[0];
        for (int i = 1; i < values.Count; i++)
        {
            if (selector == SortValueSelector.Max ? values[i] > selected : values[i] < selected)
                selected = values[i];
        }
        return selected;
    }

    private static double ResolveStoredDouble(IFlushSource source, string fieldName, int docId)
    {
        if (!TryGetStoredValue(source, fieldName, docId, out var value))
            return 0;
        if (value.IsLong)
            return value.LongValue;
        return value.StringValue is not null
            && double.TryParse(value.StringValue, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static long ResolveStoredInt64(IFlushSource source, string fieldName, int docId)
    {
        if (!TryGetStoredValue(source, fieldName, docId, out var value))
            return 0;
        if (value.IsLong)
            return value.LongValue;
        return value.StringValue is not null
            && long.TryParse(value.StringValue, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static string? ResolveStoredString(IFlushSource source, string fieldName, int docId)
    {
        return TryGetStoredValue(source, fieldName, docId, out var value)
            ? value.StringValue
            : null;
    }

    private static bool TryGetStoredValue(
        IFlushSource source,
        string fieldName,
        int docId,
        out Codecs.StoredFields.StoredFieldValue value)
    {
        value = default;
        if ((uint)docId >= (uint)source.StoredDocStarts.Count)
            return false;

        int start = source.StoredDocStarts[docId];
        int end = docId + 1 < source.StoredDocStarts.Count
            ? source.StoredDocStarts[docId + 1]
            : source.StoredFieldIds.Count;
        for (int i = start; i < end; i++)
        {
            int fieldId = source.StoredFieldIds[i];
            if ((uint)fieldId < (uint)source.StoredFieldIdToName.Count
                && string.Equals(source.StoredFieldIdToName[fieldId], fieldName, StringComparison.Ordinal))
            {
                value = source.StoredFieldValues[i];
                return true;
            }
        }

        return false;
    }

    private static void ApplySortPermutation(IFlushSource buffer, int[] sortPerm, int[] inversePerm)
    {
        int n = buffer.DocCount;

        RemapPostings(buffer, inversePerm);
        RemapStoredFields(buffer, sortPerm, n);
        RemapDocTokenCounts(buffer, sortPerm, n);

        foreach (var field in buffer.FieldBoosts.Keys.ToArray())
        {
            var docMap = buffer.FieldBoosts[field];
            var remapped = new Dictionary<int, float>(docMap.Count);
            foreach (var (oldDoc, boost) in docMap)
            {
                if (oldDoc < inversePerm.Length)
                    remapped[inversePerm[oldDoc]] = boost;
            }
            buffer.FieldBoosts[field] = remapped;
        }

        foreach (var field in buffer.NumericDocValues.Keys.ToArray())
        {
            var list = buffer.NumericDocValues[field];
            var reordered = new List<double>(n);
            for (int i = 0; i < n; i++)
            {
                int old = sortPerm[i];
                reordered.Add(old < list.Count ? list[old] : 0);
            }
            buffer.NumericDocValues[field] = reordered;
        }

        foreach (var field in buffer.Int64DocValues.Keys.ToArray())
        {
            var list = buffer.Int64DocValues[field];
            var reordered = new List<long>(n);
            for (int i = 0; i < n; i++)
            {
                int old = sortPerm[i];
                reordered.Add(old < list.Count ? list[old] : 0);
            }
            buffer.Int64DocValues[field] = reordered;
        }

        foreach (var field in buffer.SortedDocValues.Keys.ToArray())
        {
            var list = buffer.SortedDocValues[field];
            var reordered = new List<string?>(n);
            for (int i = 0; i < n; i++)
            {
                int old = sortPerm[i];
                reordered.Add(old < list.Count ? list[old] : null);
            }
            buffer.SortedDocValues[field] = reordered;
        }

        RemapMultiValuedDocValues(buffer.SortedSetDocValues, sortPerm, n);
        RemapMultiValuedDocValues(buffer.SortedNumericDocValues, sortPerm, n);
        RemapMultiValuedDocValues(buffer.Int64SortedDocValues, sortPerm, n);
        RemapMultiValuedDocValues(buffer.BinaryDocValues, sortPerm, n);

        foreach (var field in buffer.NumericIndex.Keys.ToArray())
        {
            var docMap = buffer.NumericIndex[field];
            var remapped = new Dictionary<int, double>(docMap.Count);
            foreach (var (oldDoc, val) in docMap)
            {
                if (oldDoc < inversePerm.Length)
                    remapped[inversePerm[oldDoc]] = val;
            }
            buffer.NumericIndex[field] = remapped;
        }

        foreach (var field in buffer.Int64Index.Keys.ToArray())
        {
            var docMap = buffer.Int64Index[field];
            var remapped = new Dictionary<int, long>(docMap.Count);
            foreach (var (oldDoc, val) in docMap)
            {
                if (oldDoc < inversePerm.Length)
                    remapped[inversePerm[oldDoc]] = val;
            }
            buffer.Int64Index[field] = remapped;
        }

        if (buffer.Vectors.Count > 0)
        {
            var newOuter = new Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>>(
                buffer.Vectors.Count, StringComparer.Ordinal);
            foreach (var (fieldName, docMap) in buffer.Vectors)
            {
                var remapped = new Dictionary<int, ReadOnlyMemory<float>>(docMap.Count);
                foreach (var (oldDoc, vec) in docMap)
                {
                    if (oldDoc < inversePerm.Length)
                        remapped[inversePerm[oldDoc]] = vec;
                }
                newOuter[fieldName] = remapped;
            }
            buffer.Vectors.Clear();
            foreach (var (fieldName, docMap) in newOuter)
                buffer.Vectors[fieldName] = docMap;
        }

        if (buffer.ParentDocIds is { Count: > 0 } parentDocIds)
        {
            var remapped = new HashSet<int>(parentDocIds.Count);
            foreach (var oldDocId in parentDocIds)
            {
                if ((uint)oldDocId < (uint)inversePerm.Length)
                    remapped.Add(inversePerm[oldDocId]);
            }
            parentDocIds.Clear();
            foreach (var newDocId in remapped)
                parentDocIds.Add(newDocId);
        }
    }

    private static void RemapPostings(IFlushSource buffer, int[] inversePerm)
    {
        foreach (var acc in buffer.PostingAccumulators)
            acc.RemapDocIds(inversePerm);
    }

    private static void RemapStoredFields(IFlushSource buffer, int[] sortPerm, int n)
    {
        int totalEntries = buffer.StoredFieldIds.Count;
        var newFieldIds = new List<int>(totalEntries);
        var newValues = new List<Codecs.StoredFields.StoredFieldValue>(totalEntries);
        var newDocStarts = new List<int>(n);

        for (int newDoc = 0; newDoc < n; newDoc++)
        {
            int oldDoc = sortPerm[newDoc];
            newDocStarts.Add(newFieldIds.Count);

            int start = oldDoc < buffer.StoredDocStarts.Count ? buffer.StoredDocStarts[oldDoc] : totalEntries;
            int end = (oldDoc + 1) < buffer.StoredDocStarts.Count ? buffer.StoredDocStarts[oldDoc + 1] : totalEntries;

            for (int j = start; j < end; j++)
            {
                newFieldIds.Add(buffer.StoredFieldIds[j]);
                newValues.Add(buffer.StoredFieldValues[j]);
            }
        }

        buffer.StoredFieldIds.Clear();
        buffer.StoredFieldIds.AddRange(newFieldIds);
        buffer.StoredFieldValues.Clear();
        buffer.StoredFieldValues.AddRange(newValues);
        buffer.StoredDocStarts.Clear();
        buffer.StoredDocStarts.AddRange(newDocStarts);
    }

    private static void RemapDocTokenCounts(IFlushSource buffer, int[] sortPerm, int n)
    {
        if (buffer.DocTokenCounts.Count == 0) return;
        var keysBuf = ArrayPool<string>.Shared.Rent(buffer.DocTokenCounts.Count);
        try
        {
            int k = 0;
            foreach (var key in buffer.DocTokenCounts.Keys) keysBuf[k++] = key;
            for (int idx = 0; idx < k; idx++)
            {
                var field = keysBuf[idx];
                var old = buffer.DocTokenCounts[field];
                var reordered = new int[old.Length];
                for (int i = 0; i < n; i++)
                {
                    int oldDoc = sortPerm[i];
                    reordered[i] = oldDoc < old.Length ? old[oldDoc] : 0;
                }
                buffer.DocTokenCounts[field] = reordered;
            }
        }
        finally
        {
            ArrayPool<string>.Shared.Return(keysBuf, clearArray: true);
        }
    }

    private static void RemapMultiValuedDocValues<T>(
        Dictionary<string, Dictionary<int, List<T>>> source,
        int[] sortPerm,
        int docCount)
    {
        foreach (var (field, docMap) in source)
        {
            var remapped = new Dictionary<int, List<T>>(docMap.Count);
            for (int newDocId = 0; newDocId < docCount; newDocId++)
            {
                int oldDocId = sortPerm[newDocId];
                if (docMap.TryGetValue(oldDocId, out var values))
                    remapped[newDocId] = values;
            }

            source[field] = remapped;
        }
    }

    private static Dictionary<string, IReadOnlyList<T>?[]> ToDenseMultiValueColumns<T>(
        Dictionary<string, Dictionary<int, List<T>>> source,
        int docCount)
    {
        var dense = new Dictionary<string, IReadOnlyList<T>?[]>(source.Count, StringComparer.Ordinal);
        foreach (var (field, sparseDocs) in source)
        {
            var values = new IReadOnlyList<T>?[docCount];
            bool hasAnyValue = false;
            foreach (var (docId, docValues) in sparseDocs)
            {
                if ((uint)docId >= (uint)docCount || docValues.Count == 0)
                    continue;

                values[docId] = docValues;
                hasAnyValue = true;
            }

            if (hasAnyValue)
                dense[field] = values;
        }

        return dense;
    }

    private static void WriteNumericIndex(Dictionary<string, Dictionary<int, double>> numericIndex, string filePath)
    {
        if (numericIndex.Count == 0) return;
        NumericIndexCodec.WriteDouble(filePath, numericIndex);
    }

    private static void WriteInt64Index(Dictionary<string, Dictionary<int, long>> int64Index, string filePath)
    {
        if (int64Index.Count == 0) return;
        NumericIndexCodec.WriteInt64(filePath, int64Index);
    }

    private static (float min, float alpha) ComputeInt8Params(
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> perField)
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        foreach (var v in perField.Values)
        {
            var span = v.Span;
            for (int j = 0; j < span.Length; j++)
            {
                float val = span[j];
                if (val < min) min = val;
                if (val > max) max = val;
            }
        }
        if (MathF.Abs(max - min) < 1e-8f) max = min + 1f;
        return (min, (max - min) / 255f);
    }

    private static float[] ComputeBBQCentroid(
        IReadOnlyDictionary<int, ReadOnlyMemory<float>> perField,
        int dimension)
    {
        float[] centroid = new float[dimension];
        int count = 0;
        foreach (var v in perField.Values)
        {
            var span = v.Span;
            for (int j = 0; j < dimension; j++)
                centroid[j] += span[j];
            count++;
        }
        if (count > 0)
        {
            for (int j = 0; j < dimension; j++)
                centroid[j] /= count;
        }
        return centroid;
    }
}
