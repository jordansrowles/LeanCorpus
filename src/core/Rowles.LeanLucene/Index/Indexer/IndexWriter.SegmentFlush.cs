using System.Buffers;
using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Index.Indexer;

public sealed partial class IndexWriter
{
    private void FlushSegment()
    {
        if (_bufferedDocCount == 0) return;

        var flushSw = System.Diagnostics.Stopwatch.StartNew();
        using var flushActivity = Diagnostics.LeanLuceneActivitySource.Source
            .StartActivity(Diagnostics.LeanLuceneActivitySource.Flush);

        // Compute sort permutation if index-time sort is configured
        int[]? sortPerm = null;       // sortPerm[newDocId] = oldDocId
        int[]? inversePerm = null;    // inversePerm[oldDocId] = newDocId
        if (_config.IndexSort is not null)
        {
            sortPerm = ComputeSortPermutation(_config.IndexSort);
            inversePerm = new int[_bufferedDocCount];
            for (int i = 0; i < _bufferedDocCount; i++)
                inversePerm[sortPerm[i]] = i;

            // Apply permutation to all in-memory buffers
            ApplySortPermutation(sortPerm, inversePerm);
        }

        // Track how many documents we're flushing so we can release the corresponding semaphore slots.
        int docCountToFlush = _bufferedDocCount;

        var segId = $"seg_{_nextSegmentOrdinal++}";
        var basePath = Path.Combine(_directory.DirectoryPath, segId);
        flushActivity?.SetTag("index.segment_id", segId);
        flushActivity?.SetTag("index.doc_count", docCountToFlush);

        // Collect all field names
        var fieldNames = _fieldNames.ToList();

        // Write segment metadata (.seg)
        var segInfo = new SegmentInfo
        {
            SegmentId = segId,
            DocCount = _bufferedDocCount,
            LiveDocCount = _bufferedDocCount,
            CommitGeneration = _commitGeneration,
            FieldNames = fieldNames,
            IndexSortFields = _config.IndexSort is not null
                ? _config.IndexSort.Fields.Select(f => $"{f.Type}:{f.FieldName}:{f.Descending}").ToList()
                : null
        };
        segInfo.WriteTo(basePath + ".seg");

        // Write term dictionary and postings per field
        // Sort qualified terms for the dictionary
        _sortedTermsBuffer.Clear();
        _sortedTermsBuffer.AddRange(_postings.Keys);
        _sortedTermsBuffer.Sort(StringComparer.Ordinal);
        var postingsOffsets = new Dictionary<string, long>(_sortedTermsBuffer.Count);

        // Write all postings to a single .pos file using v3 block-packed format.
        // Two-pass approach: write all data forward-only (no seeks), then back-patch
        // per-term headers in a single sequential pass to avoid per-term buffer flushes.
        // Collect per-term header positions and metadata for back-patching
        var headerPatches = new List<(long HeaderPos, int DocFreq, long SkipOffset)>(_sortedTermsBuffer.Count);

        using (var posOutput = new IndexOutput(basePath + ".pos"))
        {
            // Write header at the start of the file
            CodecConstants.WriteHeader(posOutput, CodecConstants.PostingsVersion);

            using var blockWriter = new BlockPostingsWriter(posOutput);

            foreach (var qt in _sortedTermsBuffer)
            {
                var acc = _postings[qt];
                var ids = acc.DocIds;

                bool hasFreqs = acc.HasFreqs;
                bool hasPositions = acc.HasPositions;
                bool hasPayloads = acc.HasPayloads;

                // Reserve space for per-term header (written with placeholder values)
                long headerPos = posOutput.Position;
                posOutput.WriteInt32(0);          // placeholder docFreq
                posOutput.WriteInt64(0L);         // placeholder skipOffset
                posOutput.WriteBoolean(hasFreqs);
                posOutput.WriteBoolean(hasPositions);
                posOutput.WriteBoolean(hasPayloads);

                // Write block-packed doc IDs + frequencies
                blockWriter.StartTerm();
                for (int i = 0; i < ids.Length; i++)
                    blockWriter.AddPosting(ids[i], hasFreqs ? acc.GetFreq(i) : 1);
                var meta = blockWriter.FinishTerm();

                // Write positions in VarInt format (same as v2) after skip data
                if (hasPositions)
                {
                    for (int i = 0; i < ids.Length; i++)
                    {
                        var positions = acc.GetPositions(i);
                        posOutput.WriteVarInt(positions.Length);
                        int prevPos = 0;
                        for (int pi = 0; pi < positions.Length; pi++)
                        {
                            posOutput.WriteVarInt(positions[pi] - prevPos);
                            prevPos = positions[pi];

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

        // Back-patch all per-term headers using a raw file stream to avoid
        // the per-seek buffer flush overhead of IndexOutput.Seek().
        using (var patchStream = new FileStream(basePath + ".pos", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Span<byte> patch = stackalloc byte[12]; // int32 docFreq + int64 skipOffset
            for (int i = 0; i < headerPatches.Count; i++)
            {
                var (hpos, docFreq, skipOffset) = headerPatches[i];
                patchStream.Seek(hpos, SeekOrigin.Begin);
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(patch, docFreq);
                System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(patch[4..], skipOffset);
                patchStream.Write(patch);
            }
        }

        // Write term dictionary (.dic)
        TermDictionaryWriter.Write(basePath + ".dic", _sortedTermsBuffer, postingsOffsets);

        // Write per-field norms (.nrm) — use pre-tracked per-field token counts (O(1) per doc)
        var fieldNorms = new Dictionary<string, float[]>(_docTokenCounts.Count, StringComparer.Ordinal);
        var normsReturnList = new List<float[]>(_docTokenCounts.Count);
        foreach (var (fieldName, counts) in _docTokenCounts)
        {
            var norms = ArrayPool<float>.Shared.Rent(_bufferedDocCount);
            for (int i = 0; i < _bufferedDocCount; i++)
            {
                int tokenCount = i < counts.Length ? counts[i] : 0;
                norms[i] = 1.0f / (1.0f + Math.Max(1, tokenCount));
            }
            fieldNorms[fieldName] = norms;
            normsReturnList.Add(norms);
        }
        NormsWriter.Write(basePath + ".nrm", fieldNorms, _bufferedDocCount);
        foreach (var arr in normsReturnList) ArrayPool<float>.Shared.Return(arr, clearArray: false);

        // Write exact field lengths (.fln) for precise BM25 scoring
        var fieldLengths = new Dictionary<string, int[]>(_docTokenCounts.Count, StringComparer.Ordinal);
        var lengthsReturnList = new List<int[]>(_docTokenCounts.Count);
        foreach (var (fieldName, counts) in _docTokenCounts)
        {
            var lengths = ArrayPool<int>.Shared.Rent(_bufferedDocCount);
            for (int i = 0; i < _bufferedDocCount; i++)
                lengths[i] = i < counts.Length ? counts[i] : 0;
            fieldLengths[fieldName] = lengths;
            lengthsReturnList.Add(lengths);
        }
        FieldLengthWriter.Write(basePath + ".fln", fieldLengths, _bufferedDocCount);
        SegmentStats.FromFieldLengths(_bufferedDocCount, _bufferedDocCount, fieldNames, fieldLengths)
            .WriteTo(SegmentStats.GetStatsPath(_directory.DirectoryPath, segId));
        foreach (var arr in lengthsReturnList) ArrayPool<int>.Shared.Return(arr, clearArray: false);

        // Write stored fields (.fdt + .fdx) from flat buffer
        StoredFieldsWriter.Write(basePath + ".fdt", basePath + ".fdx",
            _sfDocStarts, _sfFieldIds, _sfValues, _sfFieldIdToName,
            _config.StoredFieldBlockSize, _config.CompressionPolicy);

        // Write numeric field index (.num)
        WriteNumericIndex(basePath + ".num");

        if (_bufferedVectors.Count > 0)
        {
            foreach (var (fieldName, perField) in _bufferedVectors)
            {
                if (perField.Count == 0) continue;

                int dimension = 0;
                foreach (var v in perField.Values)
                {
                    if (v.Length > 0) { dimension = v.Length; break; }
                }
                if (dimension == 0) continue;

                if (_config.NormaliseVectors)
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

                var vecPath = Codecs.Vectors.VectorFilePaths.VectorFile(basePath, fieldName);
                Codecs.Vectors.VectorWriter.WriteField(vecPath, _bufferedDocCount, dimension, perField);

                bool hasHnsw = false;
                if (_config.BuildHnswOnFlush && perField.Count >= 2)
                {
                    var memSource = new Dictionary<int, ReadOnlyMemory<float>>(perField);
                    var source = new Codecs.Vectors.InMemoryVectorSource(memSource, dimension);
                    var docIds = perField.Keys.ToArray();
                    var hnswSw = System.Diagnostics.Stopwatch.StartNew();
                    var graph = Codecs.Hnsw.HnswGraphBuilder.Build(source, docIds, _config.HnswBuildConfig, _config.HnswSeed);
                    hnswSw.Stop();
                    _config.Metrics.RecordHnswBuild(hnswSw.Elapsed, docIds.Length);
                    var hnswPath = Codecs.Vectors.VectorFilePaths.HnswFile(basePath, fieldName);
                    Codecs.Hnsw.HnswWriter.Write(hnswPath, graph, dimension, _config.NormaliseVectors);
                    hasHnsw = true;
                }

                segInfo.VectorFields.Add(new VectorFieldInfo
                {
                    FieldName = fieldName,
                    Dimension = dimension,
                    Normalised = _config.NormaliseVectors,
                    HasHnsw = hasHnsw,
                });
            }

            // Re-write the seg file now that VectorFields are populated.
            segInfo.WriteTo(basePath + ".seg");
        }

        // Write DocValues column-stride files
        if (_numericDocValues.Count > 0)
        {
            var dvn = new Dictionary<string, double[]>(_numericDocValues.Count, StringComparer.Ordinal);
            var dvnReturnList = new List<double[]>(_numericDocValues.Count);
            // Build per-field presence sets from the sparse numeric index so the codec can
            // distinguish genuinely absent docs from those with a sentinel zero value.
            var dvnPresence = new Dictionary<string, IReadOnlySet<int>>(_numericIndex.Count, StringComparer.Ordinal);
            foreach (var (field, list) in _numericDocValues)
            {
                var arr = ArrayPool<double>.Shared.Rent(_bufferedDocCount);
                Array.Clear(arr, 0, _bufferedDocCount);
                for (int i = 0; i < Math.Min(list.Count, _bufferedDocCount); i++)
                    arr[i] = list[i];
                dvn[field] = arr;
                dvnReturnList.Add(arr);
                if (_numericIndex.TryGetValue(field, out var sparseMap))
                    dvnPresence[field] = sparseMap.Keys.ToHashSet();
            }
            NumericDocValuesWriter.Write(basePath + ".dvn", dvn, _bufferedDocCount, dvnPresence);
            foreach (var arr in dvnReturnList) ArrayPool<double>.Shared.Return(arr, clearArray: false);
        }

        if (_sortedDocValues.Count > 0)
        {
            var dvs = new Dictionary<string, string?[]>(_sortedDocValues.Count, StringComparer.Ordinal);
            foreach (var (field, list) in _sortedDocValues)
            {
                var arr = new string?[_bufferedDocCount];
                for (int i = 0; i < Math.Min(list.Count, _bufferedDocCount); i++)
                    arr[i] = list[i];
                dvs[field] = arr;
            }
            SortedDocValuesWriter.Write(basePath + ".dvs", dvs, _bufferedDocCount);
        }

        if (_sortedSetDocValues.Count > 0)
            SortedSetDocValuesWriter.Write(basePath + ".dss", ToDenseMultiValueColumns(_sortedSetDocValues, _bufferedDocCount), _bufferedDocCount);

        if (_sortedNumericDocValues.Count > 0)
            SortedNumericDocValuesWriter.Write(basePath + ".dsn", ToDenseMultiValueColumns(_sortedNumericDocValues, _bufferedDocCount), _bufferedDocCount);

        if (_binaryDocValues.Count > 0)
            BinaryDocValuesWriter.Write(basePath + ".dvb", ToDenseMultiValueColumns(_binaryDocValues, _bufferedDocCount), _bufferedDocCount);

        // Write BKD tree for numeric fields (.bkd).
        // Source from the sparse numeric index so docs without a value are not padded
        // with a synthetic zero. This is what makes BKD safe to use on the read path.
        if (_numericIndex.Count > 0)
        {
            var bkdData = new Dictionary<string, List<(double Value, int DocId)>>(_numericIndex.Count, StringComparer.Ordinal);
            foreach (var (field, docMap) in _numericIndex)
            {
                var points = new List<(double Value, int DocId)>(docMap.Count);
                foreach (var (docId, value) in docMap)
                {
                    if (docId < _bufferedDocCount)
                        points.Add((value, docId));
                }
                if (points.Count > 0)
                    bkdData[field] = points;
            }
            if (bkdData.Count > 0)
                BKDWriter.Write(basePath + ".bkd", bkdData, _config.BKDMaxLeafSize);
        }

        // Write term vectors (.tvd + .tvx) when enabled
        if (_config.StoreTermVectors)
        {
            var tvDocs = new Dictionary<string, List<TermVectorEntry>>[_bufferedDocCount];
            for (int d = 0; d < _bufferedDocCount; d++)
                tvDocs[d] = new Dictionary<string, List<TermVectorEntry>>(StringComparer.Ordinal);

            foreach (var (qt, acc) in _postings)
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
                    if (docId >= _bufferedDocCount) continue;
                    if (!tvDocs[docId].TryGetValue(fld, out var terms))
                    {
                        terms = [];
                        tvDocs[docId][fld] = terms;
                    }
                    int freq = acc.GetFreq(i);
                    var posSpan = acc.GetPositions(i);
                    var positions = posSpan.IsEmpty ? [] : posSpan.ToArray();
                    terms.Add(new TermVectorEntry(trm, freq, positions));
                }
            }
            TermVectorsWriter.Write(basePath + ".tvd", basePath + ".tvx", tvDocs);
        }

        // Write parent bitset (.pbs) when block-join documents were indexed
        if (_parentDocIds is { Count: > 0 })
        {
            var pbs = new ParentBitSet(_bufferedDocCount);
            foreach (var pid in _parentDocIds)
                pbs.Set(pid);
            pbs.WriteTo(basePath + ".pbs");
        }

        _committedSegments.Add(segInfo);
        ResetBuffer();

        // Release semaphore slots AFTER the flush is complete and buffers are cleared.
        if (_backpressureSemaphore is not null && docCountToFlush > 0)
        {
            int toRelease = Math.Min(docCountToFlush, _semaphoreSlotsHeld);
            if (toRelease > 0)
            {
                _backpressureSemaphore.Release(toRelease);
                _semaphoreSlotsHeld -= toRelease;
            }
        }

        flushSw.Stop();
        _config.Metrics.RecordFlush(flushSw.Elapsed);
    }

    /// <summary>
    /// Computes a permutation array where perm[newDocId] = oldDocId,
    /// sorting buffered documents according to the given <see cref="IndexSort"/>.
    /// Pre-extracts sort key arrays to avoid per-comparison dictionary lookups.
    /// </summary>
    private int[] ComputeSortPermutation(IndexSort sort)
    {
        int n = _bufferedDocCount;
        var perm = new int[n];
        for (int i = 0; i < n; i++) perm[i] = i;

        // Pre-extract sort key arrays to avoid dictionary lookups in the comparator
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
                    if (_numericDocValues.TryGetValue(field.FieldName, out var dvList))
                    {
                        for (int i = 0; i < Math.Min(n, dvList.Count); i++)
                            numArr[i] = dvList[i];
                    }
                    numericKeys[f] = numArr;
                    break;

                case SortFieldType.String:
                    var strArr = new string?[n];
                    if (_sortedDocValues.TryGetValue(field.FieldName, out var sdvList))
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

    // No longer needed — sort keys are pre-extracted inline
    // private int CompareNumeric(int docA, int docB, string fieldName) { ... }
    // private int CompareString(int docA, int docB, string fieldName) { ... }

    /// <summary>
    /// Remaps all in-memory buffers so that doc IDs reflect the sorted order.
    /// After this call, doc 0 is the first doc in sorted order, etc.
    /// </summary>
    private void ApplySortPermutation(int[] sortPerm, int[] inversePerm)
    {
        int n = _bufferedDocCount;

        // 1. Remap postings: each PostingAccumulator references old doc IDs → translate to new
        RemapPostings(inversePerm);

        // 2. Remap stored fields (flat buffer)
        RemapStoredFields(sortPerm, n);

        // 3. Remap per-field token counts (norm source)
        RemapDocTokenCounts(sortPerm, n);

        // 4. Remap numeric doc values
        foreach (var (field, list) in _numericDocValues)
        {
            var reordered = new List<double>(n);
            for (int i = 0; i < n; i++)
            {
                int old = sortPerm[i];
                reordered.Add(old < list.Count ? list[old] : 0);
            }
            _numericDocValues[field] = reordered;
        }

        // 5. Remap sorted doc values
        foreach (var (field, list) in _sortedDocValues)
        {
            var reordered = new List<string?>(n);
            for (int i = 0; i < n; i++)
            {
                int old = sortPerm[i];
                reordered.Add(old < list.Count ? list[old] : null);
            }
            _sortedDocValues[field] = reordered;
        }

        // 5a. Remap richer multi-valued doc values.
        RemapMultiValuedDocValues(_sortedSetDocValues, sortPerm, n);
        RemapMultiValuedDocValues(_sortedNumericDocValues, sortPerm, n);
        RemapMultiValuedDocValues(_binaryDocValues, sortPerm, n);

        // 6. Remap numeric index (field → docId → value)
        foreach (var (field, docMap) in _numericIndex)
        {
            var remapped = new Dictionary<int, double>(docMap.Count);
            foreach (var (oldDoc, val) in docMap)
            {
                if (oldDoc < inversePerm.Length)
                    remapped[inversePerm[oldDoc]] = val;
            }
            _numericIndex[field] = remapped;
        }

        // 7. Remap vectors (per field)
        if (_bufferedVectors.Count > 0)
        {
            var newOuter = new Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>>(
                _bufferedVectors.Count, StringComparer.Ordinal);
            foreach (var (fieldName, docMap) in _bufferedVectors)
            {
                var remapped = new Dictionary<int, ReadOnlyMemory<float>>(docMap.Count);
                foreach (var (oldDoc, vec) in docMap)
                {
                    if (oldDoc < inversePerm.Length)
                        remapped[inversePerm[oldDoc]] = vec;
                }
                newOuter[fieldName] = remapped;
            }
            _bufferedVectors = newOuter;
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

    /// <summary>
    /// Rewrites all PostingAccumulator doc ID arrays using the inverse permutation,
    /// then re-sorts them so doc IDs remain in ascending order (required by the codec).
    /// </summary>
    private void RemapPostings(int[] inversePerm)
    {
        foreach (var (_, acc) in _postings)
            acc.RemapDocIds(inversePerm);
    }

    /// <summary>
    /// Reorders the flat stored fields buffer according to the sort permutation.
    /// </summary>
    private void RemapStoredFields(int[] sortPerm, int n)
    {
        int totalEntries = _sfFieldIds.Count;
        var newFieldIds = new List<int>(totalEntries);
        var newValues = new List<string>(totalEntries);
        var newDocStarts = new List<int>(n);

        for (int newDoc = 0; newDoc < n; newDoc++)
        {
            int oldDoc = sortPerm[newDoc];
            newDocStarts.Add(newFieldIds.Count);

            int start = oldDoc < _sfDocStarts.Count ? _sfDocStarts[oldDoc] : totalEntries;
            int end = (oldDoc + 1) < _sfDocStarts.Count ? _sfDocStarts[oldDoc + 1] : totalEntries;

            for (int j = start; j < end; j++)
            {
                newFieldIds.Add(_sfFieldIds[j]);
                newValues.Add(_sfValues[j]);
            }
        }

        _sfFieldIds = newFieldIds;
        _sfValues = newValues;
        _sfDocStarts = newDocStarts;
    }

    /// <summary>
    /// Reorders per-field doc token count arrays according to the sort permutation.
    /// </summary>
    private void RemapDocTokenCounts(int[] sortPerm, int n)
    {
        foreach (var field in _docTokenCounts.Keys.ToList())
        {
            var old = _docTokenCounts[field];
            var reordered = new int[old.Length];
            for (int i = 0; i < n; i++)
            {
                int oldDoc = sortPerm[i];
                reordered[i] = oldDoc < old.Length ? old[oldDoc] : 0;
            }
            _docTokenCounts[field] = reordered;
        }
    }
}
