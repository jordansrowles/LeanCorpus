using System.Buffers;
using Rowles.LeanCorpus.Codecs;
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
        var flushSw = System.Diagnostics.Stopwatch.StartNew();
        using var flushActivity = Diagnostics.LeanCorpusActivitySource.Source
            .StartActivity(Diagnostics.LeanCorpusActivitySource.Flush);

        // Compute sort permutation if index-time sort is configured
        int[]? sortPerm = null;
        int[]? inversePerm = null;
        if (config.IndexSort is not null)
        {
            sortPerm = ComputeSortPermutation(buffer, config.IndexSort);
            inversePerm = new int[buffer.DocCount];
            for (int i = 0; i < buffer.DocCount; i++)
                inversePerm[sortPerm[i]] = i;

            ApplySortPermutation(buffer, sortPerm, inversePerm);
        }

        int docCount = buffer.DocCount;

        var segId = $"seg_{nextSegmentOrdinal++}";
        var basePath = Path.Combine(directoryPath, segId);
        flushActivity?.SetTag("index.segment_id", segId);
        flushActivity?.SetTag("index.doc_count", docCount);

        var fieldNames = buffer.FieldNames.ToList();

        var segInfo = new SegmentInfo
        {
            SegmentId = segId,
            DocCount = buffer.DocCount,
            LiveDocCount = buffer.DocCount,
            CommitGeneration = commitGeneration,
            FieldNames = fieldNames,
            IndexSortFields = config.IndexSort?.SerialisedFields,
            MinSequenceNumber = config.TrackSequenceNumbers ? flushSeqNoStart : null,
            MaxSequenceNumber = config.TrackSequenceNumbers ? nextSequenceNumber - 1 : null
        };
        segInfo.WriteTo(basePath + ".seg");

        // Sort qualified terms for the dictionary — build from term hash table
        buffer.SortedTermsBuffer.Clear();
        int postingsCount = buffer.PostingsCount;
        for (int i = 0; i < postingsCount; i++)
            buffer.SortedTermsBuffer.Add(buffer.GetTermString(i));
        buffer.SortedTermsBuffer.Sort(StringComparer.Ordinal);
        var postingsOffsets = new Dictionary<string, long>(buffer.SortedTermsBuffer.Count);

        var headerPatches = new List<(long HeaderPos, int DocFreq, long SkipOffset)>(buffer.SortedTermsBuffer.Count);

        // Build term-accumulator lookup dictionary for sorted iteration
        var termToAcc = new Dictionary<string, PostingAccumulator>(postingsCount, StringComparer.Ordinal);
        for (int i = 0; i < postingsCount; i++)
            termToAcc[buffer.GetTermString(i)] = buffer.PostingAccumulators[i];

        using (var posOutput = new IndexOutput(basePath + ".pos"))
        {
            CodecConstants.WriteHeader(posOutput, CodecConstants.PostingsVersion);

            using var blockWriter = new BlockPostingsWriter(posOutput);

            foreach (var qt in buffer.SortedTermsBuffer)
            {
                var acc = termToAcc[qt];
                var ids = acc.DocIds;

                bool hasFreqs = acc.HasFreqs;
                bool hasPositions = acc.HasPositions;
                bool hasPayloads = acc.HasPayloads;

                long headerPos = posOutput.Position;
                posOutput.WriteInt32(0);
                posOutput.WriteInt64(0L);
                posOutput.WriteBoolean(hasFreqs);
                posOutput.WriteBoolean(hasPositions);
                posOutput.WriteBoolean(hasPayloads);

                blockWriter.StartTerm();
                for (int i = 0; i < ids.Length; i++)
                    blockWriter.AddPosting(ids[i], hasFreqs ? acc.GetFreq(i) : 1);
                var meta = blockWriter.FinishTerm();

                if (hasPositions)
                {
                    for (int i = 0; i < ids.Length; i++)
                    {
                        acc.GetEncodedPositionDeltas(i, out var deltaBytes, out int firstPos, out int freq);
                        posOutput.WriteVarInt(freq);
                        if (freq == 0) continue;

                        posOutput.WriteVarInt(firstPos);
                        int prevPos = firstPos;

                        // Payload for position 0
                        if (hasPayloads)
                        {
                            var payload0 = acc.GetPayload(i, 0);
                            if (payload0 is { Length: > 0 })
                            {
                                posOutput.WriteVarInt(payload0.Length);
                                posOutput.WriteBytes(payload0);
                            }
                            else
                            {
                                posOutput.WriteVarInt(0);
                            }
                        }

                        int offset = 0;
                        for (int pi = 1; pi < freq; pi++)
                        {
                            offset += PostingAccumulator.ReadVarInt(deltaBytes.Slice(offset), out int delta);
                            int abs = firstPos + delta;
                            posOutput.WriteVarInt(abs - prevPos);
                            prevPos = abs;

                            if (hasPayloads)
                            {
                                var payload = acc.GetPayload(i, pi);
                                if (payload is { Length: > 0 })
                                {
                                    posOutput.WriteVarInt(payload.Length);
                                    posOutput.WriteBytes(payload);
                                }
                                else
                                {
                                    posOutput.WriteVarInt(0);
                                }
                            }
                        }
                    }
                }

                headerPatches.Add((headerPos, meta.DocFreq, meta.SkipOffset));
                postingsOffsets[qt] = headerPos;
            }
        }

        using (var patchStream = new FileStream(basePath + ".pos", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Span<byte> patch = stackalloc byte[12];
            for (int i = 0; i < headerPatches.Count; i++)
            {
                var (hpos, docFreq, skipOffset) = headerPatches[i];
                patchStream.Seek(hpos, SeekOrigin.Begin);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(patch, docFreq);
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(patch[4..], skipOffset);
                patchStream.Write(patch);
            }
        }

        TermDictionaryWriter.Write(basePath + ".dic", buffer.SortedTermsBuffer, postingsOffsets);

        // Norms and field lengths
        var normFields = new HashSet<string>(buffer.DocTokenCounts.Keys, StringComparer.Ordinal);
        foreach (var fieldName in buffer.FieldBoosts.Keys)
            normFields.Add(fieldName);

        var fieldNorms = new Dictionary<string, float[]>(normFields.Count, StringComparer.Ordinal);
        var fieldLengths = new Dictionary<string, int[]>(buffer.DocTokenCounts.Count, StringComparer.Ordinal);
        var normsReturnList = new List<float[]>(normFields.Count);
        var lengthsReturnList = new List<int[]>(buffer.DocTokenCounts.Count);
        foreach (var fieldName in normFields)
        {
            buffer.DocTokenCounts.TryGetValue(fieldName, out var counts);
            var norms = ArrayPool<float>.Shared.Rent(buffer.DocCount);
            int[]? lengths = counts is not null ? ArrayPool<int>.Shared.Rent(buffer.DocCount) : null;
            int countsLen = counts?.Length ?? 0;
            for (int i = 0; i < buffer.DocCount; i++)
            {
                int tokenCount = counts is not null
                    ? (i < countsLen ? counts[i] : 0)
                    : 1;
                if (lengths is not null)
                    lengths[i] = tokenCount;
                norms[i] = 1.0f / (1.0f + Math.Max(1, tokenCount));
            }
            fieldNorms[fieldName] = norms;
            if (lengths is not null)
                fieldLengths[fieldName] = lengths;
            normsReturnList.Add(norms);
            if (lengths is not null)
                lengthsReturnList.Add(lengths);
        }
        NormsWriter.Write(basePath + ".nrm", fieldNorms, docCount: buffer.DocCount, sparseFieldBoosts: buffer.FieldBoosts);
        foreach (var arr in normsReturnList) ArrayPool<float>.Shared.Return(arr, clearArray: false);

        FieldLengthWriter.Write(basePath + ".fln", fieldLengths, buffer.DocCount);
        SegmentStats.FromFieldLengths(buffer.DocCount, buffer.DocCount, fieldNames, fieldLengths)
            .WriteTo(SegmentStats.GetStatsPath(directoryPath, segId));
        foreach (var arr in lengthsReturnList) ArrayPool<int>.Shared.Return(arr, clearArray: false);

        // Stored fields
        StoredFieldsWriter.Write(basePath + ".fdt", basePath + ".fdx",
            buffer.StoredDocStarts, buffer.StoredFieldIds, buffer.StoredFieldValues, buffer.StoredFieldIdToName,
            config.StoredFieldBlockSize, config.CompressionPolicy);

        // Numeric field index
        WriteNumericIndex(buffer, basePath + ".num");

        // Vectors
        if (buffer.Vectors.Count > 0)
        {
            foreach (var (fieldName, perField) in buffer.Vectors)
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
                    Codecs.Vectors.VectorWriter.WriteField(vecPath, buffer.DocCount, dimension, perField, quantisation);
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
                    // Defer write until after HNSW build source creation.
                }

                bool hasHnsw = false;
                if (config.BuildHnswOnFlush && perField.Count >= 2)
                {
                    var docIds = perField.Keys.ToArray();
                    var hnswSw = System.Diagnostics.Stopwatch.StartNew();
                    Codecs.Hnsw.HnswGraph graph;

                    if (quantisation == VectorQuantisation.Int8)
                    {
                        var int8Source = new Codecs.Vectors.Int8QuantisedMemoryVectorSource(perField, dimension, int8Min, int8Alpha);
                        var vqPath = Codecs.Vectors.VectorFilePaths.QuantisedVectorFile(basePath, fieldName);
                        Codecs.Vectors.QuantisedVectorWriter.WriteInt8(vqPath, buffer.DocCount, dimension, perField);
                        graph = Codecs.Hnsw.HnswGraphBuilder.Build(int8Source, docIds, config.HnswBuildConfig, config.HnswSeed);
                    }
                    else if (quantisation == VectorQuantisation.BBQ)
                    {
                        var bbqSource = new Codecs.Vectors.BBQMemoryVectorSource(perField, dimension, bbqCentroid!);
                        var vqPath = Codecs.Vectors.VectorFilePaths.QuantisedVectorFile(basePath, fieldName);
                        Codecs.Vectors.QuantisedVectorWriter.WriteBBQ(vqPath, buffer.DocCount, dimension, perField, bbqCentroid!);
                        graph = Codecs.Hnsw.HnswGraphBuilder.Build(bbqSource, docIds, config.HnswBuildConfig, config.HnswSeed);
                    }
                    else
                    {
                        var memSource = new Dictionary<int, ReadOnlyMemory<float>>(perField);
                        var source = new Codecs.Vectors.InMemoryVectorSource(memSource, dimension);
                        graph = Codecs.Hnsw.HnswGraphBuilder.Build(source, docIds, config.HnswBuildConfig, config.HnswSeed);
                    }
                    hnswSw.Stop();
                    config.Metrics.RecordHnswBuild(hnswSw.Elapsed, docIds.Length);
                    var hnswPath = Codecs.Vectors.VectorFilePaths.HnswFile(basePath, fieldName);
                    Codecs.Hnsw.HnswWriter.Write(hnswPath, graph, dimension, config.NormaliseVectors);
                    hasHnsw = true;
                }
                else if (quantisation != VectorQuantisation.None)
                {
                    // Write .vq even when HNSW is not built, since the data was deferred.
                    var vqPath = Codecs.Vectors.VectorFilePaths.QuantisedVectorFile(basePath, fieldName);
                    switch (quantisation)
                    {
                        case VectorQuantisation.Int8:
                            Codecs.Vectors.QuantisedVectorWriter.WriteInt8(vqPath, buffer.DocCount, dimension, perField);
                            break;
                        case VectorQuantisation.BBQ:
                            Codecs.Vectors.QuantisedVectorWriter.WriteBBQ(vqPath, buffer.DocCount, dimension, perField, bbqCentroid!);
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
        if (buffer.NumericDocValues.Count > 0)
        {
            var dvn = new Dictionary<string, double[]>(buffer.NumericDocValues.Count, StringComparer.Ordinal);
            var dvnReturnList = new List<double[]>(buffer.NumericDocValues.Count);
            var dvnPresence = new Dictionary<string, IReadOnlySet<int>>(buffer.NumericIndex.Count, StringComparer.Ordinal);
            foreach (var (field, list) in buffer.NumericDocValues)
            {
                var arr = ArrayPool<double>.Shared.Rent(buffer.DocCount);
                Array.Clear(arr, 0, buffer.DocCount);
                for (int i = 0; i < Math.Min(list.Count, buffer.DocCount); i++)
                    arr[i] = list[i];
                dvn[field] = arr;
                dvnReturnList.Add(arr);
                if (buffer.NumericIndex.TryGetValue(field, out var sparseMap))
                    dvnPresence[field] = sparseMap.Keys.ToHashSet();
            }
            NumericDocValuesWriter.Write(basePath + ".dvn", dvn, buffer.DocCount, dvnPresence);
            foreach (var arr in dvnReturnList) ArrayPool<double>.Shared.Return(arr, clearArray: false);
        }

        if (buffer.SortedDocValues.Count > 0)
        {
            var dvs = new Dictionary<string, string?[]>(buffer.SortedDocValues.Count, StringComparer.Ordinal);
            foreach (var (field, list) in buffer.SortedDocValues)
            {
                var arr = new string?[buffer.DocCount];
                for (int i = 0; i < Math.Min(list.Count, buffer.DocCount); i++)
                    arr[i] = list[i];
                dvs[field] = arr;
            }
            SortedDocValuesWriter.Write(basePath + ".dvs", dvs, buffer.DocCount);
        }

        if (buffer.SortedSetDocValues.Count > 0)
            SortedSetDocValuesWriter.Write(basePath + ".dss", ToDenseMultiValueColumns(buffer.SortedSetDocValues, buffer.DocCount), buffer.DocCount);

        if (buffer.SortedNumericDocValues.Count > 0)
            SortedNumericDocValuesWriter.Write(basePath + ".dsn", ToDenseMultiValueColumns(buffer.SortedNumericDocValues, buffer.DocCount), buffer.DocCount);

        if (buffer.BinaryDocValues.Count > 0)
            BinaryDocValuesWriter.Write(basePath + ".dvb", ToDenseMultiValueColumns(buffer.BinaryDocValues, buffer.DocCount), buffer.DocCount);

        // BKD tree
        if (buffer.NumericIndex.Count > 0)
        {
            var bkdData = new Dictionary<string, List<(double Value, int DocId)>>(buffer.NumericIndex.Count, StringComparer.Ordinal);
            foreach (var (field, docMap) in buffer.NumericIndex)
            {
                var points = new List<(double Value, int DocId)>(docMap.Count);
                foreach (var (docId, value) in docMap)
                {
                    if (docId < buffer.DocCount)
                        points.Add((value, docId));
                }
                if (points.Count > 0)
                    bkdData[field] = points;
            }
            if (bkdData.Count > 0)
                BKDWriter.Write(basePath + ".bkd", bkdData, config.BKDMaxLeafSize);
        }

        // Term vectors
        if (config.StoreTermVectors)
        {
            var tvDocs = new Dictionary<string, List<TermVectorEntry>>?[buffer.DocCount];

            foreach (var (qt, acc) in buffer.EnumeratePostings())
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
                    if (docId >= buffer.DocCount) continue;
                    var perDoc = tvDocs[docId] ??= new Dictionary<string, List<TermVectorEntry>>(StringComparer.Ordinal);
                    if (!perDoc.TryGetValue(fld, out var terms))
                    {
                        terms = [];
                        perDoc[fld] = terms;
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
                    terms.Add(new TermVectorEntry(trm, freq, positions, payloads));
                }
            }
            TermVectorsWriter.Write(basePath + ".tvd", basePath + ".tvx", tvDocs);
        }

        // Parent bitset
        if (buffer.ParentDocIds is { Count: > 0 })
        {
            var pbs = new ParentBitSet(buffer.DocCount);
            foreach (var pid in buffer.ParentDocIds)
                pbs.Set(pid);
            pbs.WriteTo(basePath + ".pbs");
        }

        flushSw.Stop();
        config.Metrics.RecordFlush(flushSw.Elapsed);

        return segInfo;
    }

    private static int[] ComputeSortPermutation(DocumentBufferState buffer, IndexSort sort)
    {
        int n = buffer.DocCount;
        var perm = new int[n];
        for (int i = 0; i < n; i++) perm[i] = i;

        var fieldCount = sort.Fields.Count;
        var numericKeys = new double[fieldCount][];
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
                    if (buffer.NumericDocValues.TryGetValue(field.FieldName, out var dvList))
                    {
                        for (int i = 0; i < Math.Min(n, dvList.Count); i++)
                            numArr[i] = dvList[i];
                    }
                    numericKeys[f] = numArr;
                    break;

                case SortFieldType.String:
                    var strArr = new string?[n];
                    if (buffer.SortedDocValues.TryGetValue(field.FieldName, out var sdvList))
                    {
                        for (int i = 0; i < Math.Min(n, sdvList.Count); i++)
                            strArr[i] = sdvList[i];
                    }
                    stringKeys[f] = strArr;
                    break;
            }
        }

        Array.Sort(perm, (a, b) =>
        {
            for (int f = 0; f < fieldCount; f++)
            {
                int cmp = sortTypes[f] switch
                {
                    SortFieldType.Numeric => numericKeys[f][a].CompareTo(numericKeys[f][b]),
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

    private static void ApplySortPermutation(DocumentBufferState buffer, int[] sortPerm, int[] inversePerm)
    {
        int n = buffer.DocCount;

        RemapPostings(buffer, inversePerm);
        RemapStoredFields(buffer, sortPerm, n);
        RemapDocTokenCounts(buffer, sortPerm, n);

        foreach (var (field, docMap) in buffer.FieldBoosts)
        {
            var remapped = new Dictionary<int, float>(docMap.Count);
            foreach (var (oldDoc, boost) in docMap)
            {
                if (oldDoc < inversePerm.Length)
                    remapped[inversePerm[oldDoc]] = boost;
            }
            buffer.FieldBoosts[field] = remapped;
        }

        foreach (var (field, list) in buffer.NumericDocValues)
        {
            var reordered = new List<double>(n);
            for (int i = 0; i < n; i++)
            {
                int old = sortPerm[i];
                reordered.Add(old < list.Count ? list[old] : 0);
            }
            buffer.NumericDocValues[field] = reordered;
        }

        foreach (var (field, list) in buffer.SortedDocValues)
        {
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
        RemapMultiValuedDocValues(buffer.BinaryDocValues, sortPerm, n);

        foreach (var (field, docMap) in buffer.NumericIndex)
        {
            var remapped = new Dictionary<int, double>(docMap.Count);
            foreach (var (oldDoc, val) in docMap)
            {
                if (oldDoc < inversePerm.Length)
                    remapped[inversePerm[oldDoc]] = val;
            }
            buffer.NumericIndex[field] = remapped;
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
            buffer.Vectors = newOuter;
        }
    }

    private static void RemapPostings(DocumentBufferState buffer, int[] inversePerm)
    {
        foreach (var acc in buffer.PostingAccumulators)
            acc.RemapDocIds(inversePerm);
    }

    private static void RemapStoredFields(DocumentBufferState buffer, int[] sortPerm, int n)
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

        buffer.StoredFieldIds = newFieldIds;
        buffer.StoredFieldValues = newValues;
        buffer.StoredDocStarts = newDocStarts;
    }

    private static void RemapDocTokenCounts(DocumentBufferState buffer, int[] sortPerm, int n)
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

    private static void WriteNumericIndex(DocumentBufferState buffer, string filePath)
    {
        if (buffer.NumericIndex.Count == 0) return;

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        writer.Write(buffer.NumericIndex.Count);
        foreach (var (fieldName, docValues) in buffer.NumericIndex)
        {
            writer.Write(fieldName);
            writer.Write(docValues.Count);
            foreach (var (docId, value) in docValues)
            {
                writer.Write(docId);
                writer.Write(value);
            }
        }
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
