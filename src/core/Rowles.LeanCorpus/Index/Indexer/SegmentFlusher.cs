using System.Buffers;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Codecs.Fst;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Index.Indexer.Postings;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Store;
namespace Rowles.LeanCorpus.Index.Indexer;

/// <summary>
/// Writes a segment from an owned detached DWPT snapshot. All helpers are static,
/// operating only on the snapshot, configuration, and path state passed in.
/// </summary>
internal static class SegmentFlusher
{
    private static SegmentInfo FlushCore(
        DwptFlushSnapshot source,
        IndexWriterConfig config,
        string directoryPath,
        string segId,
        int commitGeneration,
        long flushSeqNoStart,
        long nextSequenceNumber,
        int minDocsForHnsw,
        out int[]? inversePerm)
    {
        inversePerm = null;

        // The detached batch is exclusively owned here, so sorting can safely
        // reorder its metadata before physical publication.
        if (config.IndexSort is not null)
        {
            var sortPerm = ComputeSortPermutation(source, config.IndexSort);
            inversePerm = new int[source.DocCount];
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
            SpatialFields = source.SpatialFieldKinds
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => new SpatialFieldInfo { FieldName = pair.Key, Kind = pair.Value })
                .ToList(),
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
        int postingsCount = source.Postings.TermCount;
        TermVectorCollector? termVectors = config.StoreTermVectors
            ? new TermVectorCollector(docCount)
            : null;
        int[]? termIdsBuffer = null;
        long[]? postingsOffsetsBuffer = null;
        try
        {
            ReadOnlySpan<int> termIds = ReadOnlySpan<int>.Empty;
            Span<long> postingsOffsets = Span<long>.Empty;
            if (postingsCount > 0)
            {
                termIdsBuffer = ArrayPool<int>.Shared.Rent(postingsCount);
                for (int i = 0; i < postingsCount; i++)
                    termIdsBuffer[i] = i;
                Array.Sort(termIdsBuffer, 0, postingsCount,
                    Comparer<int>.Create(source.Postings.TermHash.CompareTerms));
                termIds = termIdsBuffer.AsSpan(0, postingsCount);

                postingsOffsetsBuffer = ArrayPool<long>.Shared.Rent(postingsCount);
                postingsOffsets = postingsOffsetsBuffer.AsSpan(0, postingsCount);
            }

            WritePostingsBody(termIds, postingsOffsets, source.Postings, basePath, quantisedNorms, inversePerm, termVectors);

            // The FST reads the owned UTF-8 term pool directly. No per-term byte
            // arrays are needed while the detached batch remains alive.
            var fstBuilder = new FstBuilder();
            fstBuilder.EnsureNodeCapacity(postingsCount);
            for (int i = 0; i < postingsCount; i++)
                fstBuilder.Add(source.Postings.TermHash.GetTerm(termIds[i]), postingsOffsets[i]);
            var fstBlob = fstBuilder.Finish();
            TermDictionaryWriter.WriteBlob(basePath + ".dic", fstBlob);
            termVectors?.Write(basePath);
        }
        finally
        {
            if (postingsOffsetsBuffer is not null)
                ArrayPool<long>.Shared.Return(postingsOffsetsBuffer, clearArray: false);
            if (termIdsBuffer is not null)
                ArrayPool<int>.Shared.Return(termIdsBuffer, clearArray: false);
        }

        NormsWriter.Write(basePath + ".nrm", fieldNorms, docCount: docCount, sparseFieldBoosts: source.FieldBoosts);
        foreach (var arr in normsReturnList) ArrayPool<float>.Shared.Return(arr, clearArray: false);

        FieldLengthWriter.Write(basePath + ".fln", fieldLengths, docCount);
        SegmentStats.FromFieldLengths(docCount, docCount, fieldNames, fieldLengths)
            .WriteTo(SegmentStats.GetStatsPath(directoryPath, segId));
        foreach (var arr in lengthsReturnList) ArrayPool<int>.Shared.Return(arr, clearArray: false);

        // Stored fields
        StoredFieldsWriter.Write(basePath + ".fdt", basePath + ".fdx",
            source.StoredDocStarts, source.StoredFieldIds, source.StoredValues, source.StoredFieldIdToName,
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

        if (source.PackedBkdFields.Count > 0)
        {
            PackedBkdWriter.Write(
                basePath + ".pbkd",
                source.PackedBkdFields,
                PackedBkdBuildOptions.Default with { SpillDirectory = directoryPath });
        }

        if (source.ShapeDocValuesFields.Count > 0)
            ShapeDocValuesWriter.Write(basePath + ".dvg", docCount, source.ShapeDocValuesFields);

        flushSw.Stop();
        config.Metrics.RecordFlush(flushSw.Elapsed);
        long codecBytes = 0;
        foreach (string fileName in SegmentFileSet.Enumerate(directoryPath, segId, config.CodecCatalog).FileNames)
            codecBytes += FileOpenRetry.GetFileLength(Path.Combine(directoryPath, fileName));
        config.Metrics.RecordCodecFlush("all", flushSw.Elapsed, codecBytes);

        return segInfo;
    }

    private sealed class TermVectorCollector
    {
        private readonly Dictionary<string, List<PendingTermVectorEntry>>?[] _docs;

        internal TermVectorCollector(int docCount)
        {
            _docs = new Dictionary<string, List<PendingTermVectorEntry>>?[docCount];
        }

        internal void Add(
            int docId,
            int termId,
            string field,
            string term,
            int freq,
            int[] positions,
            byte[]?[]? payloads,
            int[]? starts,
            int[]? ends)
        {
            if ((uint)docId >= (uint)_docs.Length)
                throw new InvalidDataException("A term-vector document ID is outside the segment.");

            var perDoc = _docs[docId] ??= new Dictionary<string, List<PendingTermVectorEntry>>(StringComparer.Ordinal);
            if (!perDoc.TryGetValue(field, out var entries))
            {
                entries = [];
                perDoc[field] = entries;
            }

            entries.Add(new PendingTermVectorEntry(
                termId,
                new TermVectorEntry(term, freq, positions, payloads, starts, ends)));
        }

        internal void Write(string basePath)
        {
            var docs = new Dictionary<string, List<TermVectorEntry>>?[_docs.Length];
            for (int docId = 0; docId < _docs.Length; docId++)
            {
                var pendingFields = _docs[docId];
                if (pendingFields is null)
                    continue;

                var fields = new Dictionary<string, List<TermVectorEntry>>(StringComparer.Ordinal);
                foreach (var (field, pendingEntries) in pendingFields)
                {
                    pendingEntries.Sort(static (left, right) => left.TermId.CompareTo(right.TermId));
                    var entries = new List<TermVectorEntry>(pendingEntries.Count);
                    foreach (var pending in pendingEntries)
                        entries.Add(pending.Entry);
                    fields[field] = entries;
                }
                docs[docId] = fields;
            }

            TermVectorsWriter.Write(basePath + ".tvd", basePath + ".tvx", docs);
        }
    }

    private readonly record struct PendingTermVectorEntry(int TermId, TermVectorEntry Entry);

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
        var segInfo = FlushCore(snapshot, config, directoryPath, segId,
            commitGeneration, seqStart, seqEnd, minDocsForHnsw: 0, out _);

        var basePath = Path.Combine(directoryPath, segId);

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

    internal static void RefreshSegmentSize(
        SegmentInfo segment,
        string directoryPath,
        CodecCatalog? catalog = null)
    {
        long totalBytes = 0;
        var codecBytes = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string fileName in SegmentFileSet.Enumerate(directoryPath, segment.SegmentId, catalog).FileNames)
        {
            string path = Path.Combine(directoryPath, fileName);
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
        if (config.UseCompoundFile && CompoundFileWriter.Pack(directoryPath, segment.SegmentId, config.CodecCatalog))
            segment.IsCompoundFile = true;
        RefreshSegmentSize(segment, directoryPath, config.CodecCatalog);
    }

    /// <summary>
    /// Writes the .pos postings body for byte-sorted qualified UTF-8 terms and
    /// returns metadata offsets in the same order for the term dictionary.
    /// Uses the v4 sequential body-then-metadata layout.
    /// </summary>
    private static void WritePostingsBody(
        ReadOnlySpan<int> termIds,
        Span<long> postingsOffsets,
        PostingsStore store,
        string basePath,
        IReadOnlyDictionary<string, byte[]> quantisedNorms,
        int[]? inversePerm,
        TermVectorCollector? termVectors)
    {
        int postingsCount = termIds.Length;
        if (postingsOffsets.Length != postingsCount)
            throw new ArgumentException("The postings offset buffer length must match the term ID count.", nameof(postingsOffsets));
        int currentFieldOrdinal = -1;
        byte[]? currentFieldNormBytes = null;
        IndexSortPostingScratch? scratch = inversePerm is null ? null : new IndexSortPostingScratch();

        try
        {
            string posPath = basePath + ".pos";
            using (var posOutput = new IndexOutput(posPath))
            {
                var descriptor = CodecCatalog.Default.GetFile("leancorpus.postings.data");
                using var frame = CodecFileWriter.Begin(posOutput, descriptor);
                var bodyOutput = frame.Output;

                using var blockWriter = new BlockPostingsWriter(bodyOutput);

                for (int termIndex = 0; termIndex < termIds.Length; termIndex++)
                {
                    int termId = termIds[termIndex];
                    ref readonly var state = ref store.GetTermState(termId);

                    string fieldName = store.GetFieldName(state.FieldOrdinal);
                    if (currentFieldOrdinal != state.FieldOrdinal)
                    {
                        currentFieldOrdinal = state.FieldOrdinal;
                        quantisedNorms.TryGetValue(fieldName, out currentFieldNormBytes);
                    }

                    bool hasFreqs = state.Flags.HasFlag(PostingFlags.HasFreqs);
                    bool hasPositions = state.Flags.HasFlag(PostingFlags.HasPositions);
                    bool hasPayloads = state.Flags.HasFlag(PostingFlags.HasPayloads);
                    string? termVectorTerm = null;
                    if (termVectors is not null && hasPositions)
                    {
                        ReadOnlySpan<byte> qualifiedTerm = store.TermHash.GetTerm(termId);
                        int separator = qualifiedTerm.IndexOf((byte)0);
                        if (separator >= 0)
                            termVectorTerm = System.Text.Encoding.UTF8.GetString(qualifiedTerm[(separator + 1)..]);
                    }

                    if (scratch is not null)
                        MaterialiseSortedTerm(store, termId, inversePerm!, scratch);

                    long bodyOffset = bodyOutput.Position;
                    blockWriter.StartTerm();
                    if (scratch is not null)
                    {
                        for (int docIndex = 0; docIndex < scratch.DocCount; docIndex++)
                        {
                            ref readonly var posting = ref scratch.Docs[docIndex];
                            byte norm = currentFieldNormBytes is not null &&
                                (uint)posting.NewDocId < (uint)currentFieldNormBytes.Length
                                ? currentFieldNormBytes[posting.NewDocId]
                                : (byte)0;
                            int frequency = hasFreqs ? Math.Max(1, posting.Freq) : 1;
                            blockWriter.AddPosting(posting.NewDocId, frequency, norm);
                        }
                    }
                    else
                    {
                        var docReader = store.OpenDocReader(termId);
                        while (docReader.MoveNext(out var posting))
                        {
                            byte norm = currentFieldNormBytes is not null &&
                                (uint)posting.DocId < (uint)currentFieldNormBytes.Length
                                ? currentFieldNormBytes[posting.DocId]
                                : (byte)0;
                            int frequency = hasFreqs ? Math.Max(1, posting.Freq) : 1;
                            blockWriter.AddPosting(posting.DocId, frequency, norm);
                        }
                    }
                    var meta = blockWriter.FinishTerm();

                    if (hasPositions)
                    {
                        if (scratch is not null)
                        {
                            for (int docIndex = 0; docIndex < scratch.DocCount; docIndex++)
                            {
                                ref readonly var posting = ref scratch.Docs[docIndex];
                                int[]? vectorPositions = termVectorTerm is not null
                                    ? new int[posting.PositionCount]
                                    : null;
                                byte[]?[]? vectorPayloads = termVectorTerm is not null && hasPayloads
                                    ? new byte[]?[posting.PositionCount]
                                    : null;
                                int[]? vectorStarts = termVectorTerm is not null &&
                                    state.Flags.HasFlag(PostingFlags.HasOffsets) && posting.PositionCount > 0
                                    ? new int[posting.PositionCount]
                                    : null;
                                int[]? vectorEnds = vectorStarts is not null ? new int[posting.PositionCount] : null;
                                bodyOutput.WriteVarInt(posting.PositionCount);
                                int previousPosition = 0;
                                for (int positionIndex = 0; positionIndex < posting.PositionCount; positionIndex++)
                                {
                                    ref readonly var position = ref scratch.Positions[posting.PositionStart + positionIndex];
                                    if (vectorPositions is not null)
                                        vectorPositions[positionIndex] = position.Position;
                                    if (vectorPayloads is not null)
                                    {
                                        vectorPayloads[positionIndex] = scratch.PayloadBytes
                                            .AsSpan(position.PayloadOffset, position.PayloadLength)
                                            .ToArray();
                                    }
                                    if (vectorStarts is not null)
                                    {
                                        vectorStarts[positionIndex] = position.StartOffset;
                                        vectorEnds![positionIndex] = position.EndOffset;
                                    }
                                    bodyOutput.WriteVarInt(position.Position - previousPosition);
                                    previousPosition = position.Position;
                                    if (hasPayloads)
                                    {
                                        bodyOutput.WriteVarInt(position.PayloadLength);
                                        if (position.PayloadLength > 0)
                                        {
                                            bodyOutput.WriteBytes(scratch.PayloadBytes.AsSpan(
                                                position.PayloadOffset, position.PayloadLength));
                                        }
                                    }
                                }

                                if (vectorPositions is not null)
                                {
                                    termVectors!.Add(posting.NewDocId, termId, fieldName, termVectorTerm!,
                                        posting.Freq, vectorPositions, vectorPayloads, vectorStarts, vectorEnds);
                                }
                            }
                        }
                        else
                        {
                            var positionDocReader = store.OpenDocReader(termId);
                            var proxReader = store.OpenProxReader(termId);
                            while (positionDocReader.MoveNext(out var posting))
                            {
                                int[]? vectorPositions = termVectorTerm is not null
                                    ? new int[posting.PositionCount]
                                    : null;
                                byte[]?[]? vectorPayloads = termVectorTerm is not null && hasPayloads
                                    ? new byte[]?[posting.PositionCount]
                                    : null;
                                int[]? vectorStarts = termVectorTerm is not null &&
                                    state.Flags.HasFlag(PostingFlags.HasOffsets) && posting.PositionCount > 0
                                    ? new int[posting.PositionCount]
                                    : null;
                                int[]? vectorEnds = vectorStarts is not null ? new int[posting.PositionCount] : null;
                                bodyOutput.WriteVarInt(posting.PositionCount);
                                proxReader.StartDocument();
                                int previousPosition = 0;
                                for (int positionIndex = 0; positionIndex < posting.PositionCount; positionIndex++)
                                {
                                    if (!proxReader.ReadNext(out var position))
                                        throw new InvalidDataException("The postings position stream ended before the document position count.");
                                    if (vectorPositions is not null)
                                        vectorPositions[positionIndex] = position.Position;
                                    if (vectorStarts is not null)
                                    {
                                        vectorStarts[positionIndex] = position.StartOffset;
                                        vectorEnds![positionIndex] = position.EndOffset;
                                    }
                                    bodyOutput.WriteVarInt(position.Position - previousPosition);
                                    previousPosition = position.Position;
                                    if (hasPayloads)
                                    {
                                        bodyOutput.WriteVarInt(position.PayloadLength);
                                        if (vectorPayloads is not null)
                                        {
                                            byte[] payload = position.PayloadLength > 0
                                                ? new byte[position.PayloadLength]
                                                : Array.Empty<byte>();
                                            if (position.PayloadLength > 0)
                                            {
                                                proxReader.CopyPayloadTo(payload);
                                                bodyOutput.WriteBytes(payload);
                                            }
                                            else
                                            {
                                                proxReader.SkipPayload();
                                            }
                                            vectorPayloads[positionIndex] = payload;
                                        }
                                        else
                                        {
                                            proxReader.CopyPayloadTo(bodyOutput);
                                        }
                                    }
                                    else
                                        proxReader.SkipPayload();
                                }

                                if (vectorPositions is not null)
                                {
                                    termVectors!.Add(posting.DocId, termId, fieldName, termVectorTerm!,
                                        posting.Freq, vectorPositions, vectorPayloads, vectorStarts, vectorEnds);
                                }
                            }

                            if (proxReader.ReadNext(out _))
                                throw new InvalidDataException("The postings position stream contained more entries than its document records.");
                        }
                    }

                    long metadataOffset = bodyOutput.Position;
                    postingsOffsets[termIndex] = metadataOffset;
                    bodyOutput.WriteInt64(bodyOffset);
                    bodyOutput.WriteInt32(meta.DocFreq);
                    bodyOutput.WriteInt64(meta.SkipOffset);
                    bodyOutput.WriteBoolean(hasFreqs);
                    bodyOutput.WriteBoolean(hasPositions);
                    bodyOutput.WriteBoolean(hasPayloads);
                }

                frame.Complete();
            }
        }
        finally
        {
            scratch?.Dispose();
        }
    }

    private static void MaterialiseSortedTerm(
        PostingsStore store,
        int termId,
        int[] inversePerm,
        IndexSortPostingScratch scratch)
    {
        scratch.Reset();
        ref readonly var state = ref store.GetTermState(termId);
        bool hasPositions = state.Flags.HasFlag(PostingFlags.HasPositions);
        var docReader = store.OpenDocReader(termId);
        var proxReader = store.OpenProxReader(termId);

        while (docReader.MoveNext(out var posting))
        {
            if ((uint)posting.DocId >= (uint)inversePerm.Length)
                throw new InvalidDataException("A postings document ID is outside the index-sort permutation.");

            ref var sortedDoc = ref scratch.AppendDoc();
            sortedDoc.NewDocId = inversePerm[posting.DocId];
            sortedDoc.Freq = posting.Freq;
            sortedDoc.PositionStart = scratch.PositionCount;
            sortedDoc.PositionCount = posting.PositionCount;

            if (!hasPositions)
                continue;

            proxReader.StartDocument();
            for (int positionIndex = 0; positionIndex < posting.PositionCount; positionIndex++)
            {
                if (!proxReader.ReadNext(out var position))
                    throw new InvalidDataException("The postings position stream ended before the document position count.");

                ref var sortedPosition = ref scratch.AppendPosition();
                sortedPosition.Position = position.Position;
                sortedPosition.HasOffsets = position.HasOffsets;
                sortedPosition.StartOffset = position.StartOffset;
                sortedPosition.EndOffset = position.EndOffset;
                sortedPosition.PayloadOffset = scratch.PayloadCount;
                sortedPosition.PayloadLength = position.PayloadLength;
                if (position.PayloadLength > 0)
                {
                    proxReader.CopyPayloadTo(scratch.GetPayloadDestination(position.PayloadLength));
                    scratch.AdvancePayload(position.PayloadLength);
                }
                else
                {
                    proxReader.SkipPayload();
                }
            }
        }

        if (hasPositions && proxReader.ReadNext(out _))
            throw new InvalidDataException("The postings position stream contained more entries than its document records.");

        Array.Sort(scratch.Docs, 0, scratch.DocCount, PostingSortDocComparer.Instance);
    }

    private sealed class PostingSortDocComparer : Comparer<PostingSortDoc>
    {
        internal static readonly PostingSortDocComparer Instance = new();

        public override int Compare(PostingSortDoc x, PostingSortDoc y)
            => x.NewDocId.CompareTo(y.NewDocId);
    }



    internal static int[] ComputeSortPermutation(DwptFlushSnapshot buffer, IndexSort sort)
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


        // Fast path: single numeric ascending sort; use keyed sort to avoid delegate per compare.
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

    private static double ResolveNumericSortValue(DwptFlushSnapshot source, SortField field, int docId)
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

    private static long ResolveInt64SortValue(DwptFlushSnapshot source, SortField field, int docId)
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

    private static string? ResolveStringSortValue(DwptFlushSnapshot source, SortField field, int docId)
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

    internal static double SelectNumericValue(IReadOnlyList<double> values, SortValueSelector selector)
    {
        double selected = values[0];
        for (int i = 1; i < values.Count; i++)
        {
            if (selector == SortValueSelector.Max ? values[i] > selected : values[i] < selected)
                selected = values[i];
        }
        return selected;
    }

    internal static long SelectInt64Value(IReadOnlyList<long> values, SortValueSelector selector)
    {
        long selected = values[0];
        for (int i = 1; i < values.Count; i++)
        {
            if (selector == SortValueSelector.Max ? values[i] > selected : values[i] < selected)
                selected = values[i];
        }
        return selected;
    }

    private static double ResolveStoredDouble(DwptFlushSnapshot source, string fieldName, int docId)
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

    private static long ResolveStoredInt64(DwptFlushSnapshot source, string fieldName, int docId)
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

    private static string? ResolveStoredString(DwptFlushSnapshot source, string fieldName, int docId)
    {
        return TryGetStoredValue(source, fieldName, docId, out var value)
            ? value.StringValue
            : null;
    }

    private static bool TryGetStoredValue(
        DwptFlushSnapshot source,
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
                value = source.StoredValues[i];
                return true;
            }
        }

        return false;
    }

    internal static void ApplySortPermutation(DwptFlushSnapshot buffer, int[] sortPerm, int[] inversePerm)
    {
        int n = buffer.DocCount;

        // The postings store is immutable after snapshot capture. Its document
        // IDs are remapped while each term is materialised for output.
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

        foreach (var packedField in buffer.PackedBkdFields.Values)
            packedField.RemapDocumentIds(inversePerm);

        foreach (ShapeDocValuesFieldBuffer field in buffer.ShapeDocValuesFields.Values)
            field.RemapDocumentIds(inversePerm);

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

    private static void RemapStoredFields(DwptFlushSnapshot buffer, int[] sortPerm, int n)
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
                newValues.Add(buffer.StoredValues[j]);
            }
        }

        buffer.StoredFieldIds.Clear();
        buffer.StoredFieldIds.AddRange(newFieldIds);
        buffer.StoredValues.Clear();
        buffer.StoredValues.AddRange(newValues);
        buffer.StoredDocStarts.Clear();
        buffer.StoredDocStarts.AddRange(newDocStarts);
    }

    private static void RemapDocTokenCounts(DwptFlushSnapshot buffer, int[] sortPerm, int n)
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

    internal static Dictionary<string, IReadOnlyList<T>?[]> ToDenseMultiValueColumns<T>(
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

    internal static (float min, float alpha) ComputeInt8Params(
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

    internal static float[] ComputeBBQCentroid(
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
