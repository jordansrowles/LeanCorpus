using System.Buffers;
using Rowles.LeanLucene.Codecs;
using Rowles.LeanLucene.Codecs.Hnsw;
using Rowles.LeanLucene.Codecs.Bkd;
using Rowles.LeanLucene.Codecs.Postings;
using Rowles.LeanLucene.Codecs.Vectors;
using Rowles.LeanLucene.Codecs.TermVectors;
using Rowles.LeanLucene.Codecs.TermDictionary;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Index.Segment;

/// <summary>
/// Tiered merge policy. When the number of segments at a given size tier
/// exceeds a configurable threshold, the smallest segments in that tier
/// are merged into one. Old segments are removed only after the merged
/// segment is fully committed.
/// </summary>
public sealed class SegmentMerger
{
    private readonly MMapDirectory _directory;
    private readonly int _mergeThreshold;
    private readonly int _skipInterval;
    private readonly Diagnostics.IMetricsCollector _metrics;

    /// <summary>Default merge threshold: when this many segments exist, merge.</summary>
    public const int DefaultMergeThreshold = 10;

    /// <summary>Default postings skip interval.</summary>
    public const int DefaultSkipInterval = 128;

    /// <summary>Initialises a merger bound to the given directory.</summary>
    /// <param name="directory">The directory holding segment files.</param>
    /// <param name="mergeThreshold">Number of segments at one tier before a merge is triggered.</param>
    /// <param name="skipInterval">Postings skip interval used when writing the merged segment.</param>
    /// <param name="metrics">Optional metrics collector. Defaults to <see cref="Diagnostics.NullMetricsCollector.Instance"/>.</param>
    public SegmentMerger(
        MMapDirectory directory,
        int mergeThreshold = DefaultMergeThreshold,
        int skipInterval = DefaultSkipInterval,
        Diagnostics.IMetricsCollector? metrics = null)
    {
        _directory = directory;
        _mergeThreshold = mergeThreshold;
        _skipInterval = skipInterval;
        _metrics = metrics ?? Diagnostics.NullMetricsCollector.Instance;
    }

    /// <summary>
    /// Checks if a merge is needed and performs it. Returns the updated segment list.
    /// </summary>
    public List<SegmentInfo> MaybeMerge(List<SegmentInfo> segments, ref int nextSegmentOrdinal)
        => MaybeMerge(segments, ref nextSegmentOrdinal, new HashSet<string>(StringComparer.Ordinal));

    /// <summary>
    /// Checks if a merge is needed and performs it, excluding segments protected by held snapshots.
    /// </summary>
    /// <param name="segments">The committed segments currently visible to the writer.</param>
    /// <param name="nextSegmentOrdinal">The next segment ordinal to allocate if a merge is performed.</param>
    /// <param name="protectedSegmentIds">Segment IDs that must not be merged or deleted while snapshots are held.</param>
    /// <returns>The original list when no merge is needed; otherwise, a new list containing merged replacements.</returns>
    public List<SegmentInfo> MaybeMerge(
        List<SegmentInfo> segments,
        ref int nextSegmentOrdinal,
        IReadOnlySet<string> protectedSegmentIds)
    {
        if (segments.Count < _mergeThreshold)
            return segments;

        // Group segments by size tier without LINQ allocations
        var tierBuckets = new Dictionary<int, List<SegmentInfo>>();
        foreach (var s in segments)
        {
            int tier = GetSizeTier(s.DocCount);
            if (!tierBuckets.TryGetValue(tier, out var bucket))
            {
                bucket = new List<SegmentInfo>();
                tierBuckets[tier] = bucket;
            }
            bucket.Add(s);
        }

        // Collect tiers that meet the merge threshold, sorted by tier key
        var eligibleTiers = new List<int>();
        foreach (var (tier, bucket) in tierBuckets)
        {
            int mergeableCount = 0;
            foreach (var segment in bucket)
            {
                if (!protectedSegmentIds.Contains(segment.SegmentId))
                    mergeableCount++;
            }

            if (mergeableCount >= _mergeThreshold)
                eligibleTiers.Add(tier);
        }

        if (eligibleTiers.Count == 0)
            return segments;

        eligibleTiers.Sort();
        var result = new List<SegmentInfo>(segments);

        foreach (var tierKey in eligibleTiers)
        {
            var bucket = tierBuckets[tierKey];
            var mergeable = bucket
                .Where(segment => !protectedSegmentIds.Contains(segment.SegmentId))
                .ToList();

            // Take the smallest segments in this tier
            mergeable.Sort(static (a, b) => a.DocCount.CompareTo(b.DocCount));
            var toMerge = mergeable.Count <= _mergeThreshold ? mergeable : mergeable.GetRange(0, _mergeThreshold);

            if (toMerge.Count < 2)
                continue;

            var merged = MergeSegments(toMerge, ref nextSegmentOrdinal);
            if (merged == null)
                continue;

            // Remove merged segments, add the new one
            foreach (var seg in toMerge)
            {
                result.Remove(seg);
            }
            result.Add(merged);
        }

        return result;
    }

    private SegmentInfo? MergeSegments(List<SegmentInfo> segments, ref int nextSegmentOrdinal)
    {
        var newSegId = $"seg_{nextSegmentOrdinal++}";
        var basePath = Path.Combine(_directory.DirectoryPath, newSegId);

        // Open one SegmentReader per source segment up front and keep it open for the
        // whole merge. The merge has three passes (doc-id remap, field copy, norm copy)
        // and previously each opened its own SegmentReader, tripling mmap creation and
        // file-handle pressure.
        var readers = new Dictionary<string, SegmentReader>(StringComparer.Ordinal);
        try
        {
            foreach (var segInfo in segments)
                readers[segInfo.SegmentId] = new SegmentReader(_directory, segInfo);

            return MergeSegmentsCore(segments, readers, newSegId, basePath);
        }
        finally
        {
            foreach (var r in readers.Values)
                r.Dispose();
        }
    }

    private SegmentInfo? MergeSegmentsCore(
        List<SegmentInfo> segments,
        IReadOnlyDictionary<string, SegmentReader> readers,
        string newSegId,
        string basePath)
    {
        // Phase 1: build per-segment doc-id remap (live docs only).
        var perSegmentMaps = new List<(SegmentInfo Seg, Dictionary<int, int> DocIdMap)>(segments.Count);
        int newDocId = 0;
        foreach (var segInfo in segments)
        {
            var reader = readers[segInfo.SegmentId];
            var docIdMap = new Dictionary<int, int>(segInfo.DocCount);
            for (int oldDocId = 0; oldDocId < segInfo.DocCount; oldDocId++)
            {
                if (reader.IsLive(oldDocId))
                    docIdMap[oldDocId] = newDocId++;
            }
            perSegmentMaps.Add((segInfo, docIdMap));
        }
        int totalDocs = newDocId;
        if (totalDocs == 0) return null;

        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segInfo in segments)
            foreach (var field in segInfo.FieldNames)
                fieldNames.Add(field);

        // Phase 2: streaming postings + dictionary merge. Bounds RAM at one term's
        // worth of decoded postings rather than the whole inverted index.
        MergePostings(perSegmentMaps, basePath);

        // Phase 3: per-doc payloads. Stored fields and term vectors are streamed to
        // disk doc-by-doc; doc-values columns still buffer (codec format requires it).
        var ctx = new MergeContext(totalDocs, fieldNames);
        bool anyTermVectors = readers.Values.Any(r => r.HasTermVectors);
        using (var storedWriter = new StoredFieldsStreamWriter(basePath + ".fdt", basePath + ".fdx"))
        using (var tvWriter = anyTermVectors ? new TermVectorsStreamWriter(basePath + ".tvd", basePath + ".tvx") : null)
        {
            ctx.StoredWriter = storedWriter;
            ctx.TermVectorWriter = tvWriter;
            AccumulateDocPayloads(perSegmentMaps, readers, ctx);
        }

        // Phase 4: emit per-codec output files.
        WriteNorms(segments, readers, fieldNames, basePath, totalDocs);
        var mergedVectorFields = MergeVectors(ctx, basePath);
        WriteNumericFiles(ctx, basePath);
        WriteFieldLengthsAndStats(ctx, fieldNames, basePath, newSegId, totalDocs);
        WriteDocValueColumns(ctx, basePath);
        WriteBkdTree(ctx, basePath);
        WriteParentBitSet(ctx, basePath);

        var mergedInfo = new SegmentInfo
        {
            SegmentId = newSegId,
            DocCount = totalDocs,
            LiveDocCount = totalDocs,
            CommitGeneration = 0,
            FieldNames = fieldNames.ToList(),
            IndexSortFields = segments[0].IndexSortFields,
            VectorFields = mergedVectorFields,
        };
        mergedInfo.WriteTo(basePath + ".seg");
        return mergedInfo;
    }

    /// <summary>
    /// Accumulator for per-doc data structures threaded through the merge phases.
    /// Owns nothing; lifetime is the merge call.
    /// </summary>
    private sealed class MergeContext
    {
        internal int TotalDocs { get; }
        internal HashSet<string> FieldNames { get; }
        internal StoredFieldsStreamWriter? StoredWriter { get; set; }
        internal TermVectorsStreamWriter? TermVectorWriter { get; set; }
        internal Dictionary<string, Dictionary<int, double>> NumericFields { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int[]> FieldLengths { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, double[]> NumericDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string?[]> SortedDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<string>?[]> SortedSetDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<double>?[]> SortedNumericDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<byte[]>?[]> BinaryDocValues { get; } = new(StringComparer.Ordinal);
        internal ParentBitSet? ParentBitSet { get; set; }
        internal Dictionary<string, Dictionary<int, ReadOnlyMemory<float>>> Vectors { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int> VectorFieldDims { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, bool> VectorFieldNormalised { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, bool> VectorFieldHadHnsw { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, List<(SegmentInfo Seg, Dictionary<int, int> OldToNew)>> VectorFieldRemaps { get; } = new(StringComparer.Ordinal);

        internal MergeContext(int totalDocs, HashSet<string> fieldNames)
        {
            TotalDocs = totalDocs;
            FieldNames = fieldNames;
        }
    }

    private void MergePostings(
        IReadOnlyList<(SegmentInfo Seg, Dictionary<int, int> DocIdMap)> sources,
        string basePath)
    {
        var merger = new List<StreamingPostingsMerger.Source>(sources.Count);
        foreach (var (seg, map) in sources)
        {
            var segBase = Path.Combine(_directory.DirectoryPath, seg.SegmentId);
            merger.Add(new StreamingPostingsMerger.Source
            {
                DicPath = segBase + ".dic",
                PosPath = segBase + ".pos",
                DocIdMap = map,
            });
        }
        StreamingPostingsMerger.Merge(merger, basePath + ".pos", basePath + ".dic");
    }

    private void AccumulateDocPayloads(
        IReadOnlyList<(SegmentInfo Seg, Dictionary<int, int> DocIdMap)> sources,
        IReadOnlyDictionary<string, SegmentReader> readers,
        MergeContext ctx)
    {
        foreach (var (segInfo, docIdMap) in sources)
        {
            var reader = readers[segInfo.SegmentId];
            bool segHasTermVectors = reader.HasTermVectors;
            var segParentBitSet = reader.GetParentBitSet();

            var flnPath = Path.Combine(_directory.DirectoryPath, segInfo.SegmentId + ".fln");
            var segFieldLengths = FieldLengthReader.TryRead(flnPath)
                ?? new Dictionary<string, int[]>(StringComparer.Ordinal);

            var segNumericDvs = NumericDocValuesReader.Read(
                Path.Combine(_directory.DirectoryPath, segInfo.SegmentId + ".dvn"));
            var segSortedDvs = SortedDocValuesReader.Read(
                Path.Combine(_directory.DirectoryPath, segInfo.SegmentId + ".dvs"));
            var segSortedSetDvs = SortedSetDocValuesReader.Read(
                Path.Combine(_directory.DirectoryPath, segInfo.SegmentId + ".dss"));
            var segSortedNumericDvs = SortedNumericDocValuesReader.Read(
                Path.Combine(_directory.DirectoryPath, segInfo.SegmentId + ".dsn"));
            var segBinaryDvs = BinaryDocValuesReader.Read(
                Path.Combine(_directory.DirectoryPath, segInfo.SegmentId + ".dvb"));
            var segNumericIndex = ReadNumericIndex(
                Path.Combine(_directory.DirectoryPath, segInfo.SegmentId + ".num"));

            for (int oldDocId = 0; oldDocId < segInfo.DocCount; oldDocId++)
            {
                if (!docIdMap.TryGetValue(oldDocId, out int remapDocId)) continue;

                ctx.StoredWriter!.AddDocument(reader.GetStoredFields(oldDocId));

                foreach (var (field, values) in segNumericIndex)
                {
                    if (!values.TryGetValue(oldDocId, out double numVal)) continue;
                    if (!ctx.NumericFields.TryGetValue(field, out var fieldMap))
                    {
                        fieldMap = new Dictionary<int, double>();
                        ctx.NumericFields[field] = fieldMap;
                    }
                    fieldMap[remapDocId] = numVal;
                }

                foreach (var (field, fl) in segFieldLengths)
                {
                    if ((uint)oldDocId >= (uint)fl.Length) continue;
                    if (!ctx.FieldLengths.TryGetValue(field, out var dst))
                    {
                        dst = new int[ctx.TotalDocs];
                        ctx.FieldLengths[field] = dst;
                    }
                    dst[remapDocId] = fl[oldDocId];
                }

                foreach (var (field, arr) in segNumericDvs.Values)
                {
                    if ((uint)oldDocId >= (uint)arr.Length) continue;
                    // Skip docs absent from this field according to the presence bitmap.
                    if (segNumericDvs.Presence.TryGetValue(field, out var presenceBitmap) &&
                        presenceBitmap is not null && !presenceBitmap.Contains(oldDocId))
                        continue;
                    if (!ctx.NumericDocValues.TryGetValue(field, out var dst))
                    {
                        dst = new double[ctx.TotalDocs];
                        ctx.NumericDocValues[field] = dst;
                    }
                    dst[remapDocId] = arr[oldDocId];
                }

                foreach (var (field, arr) in segSortedDvs.Values)
                {
                    if ((uint)oldDocId >= (uint)arr.Length) continue;
                    // Skip docs absent from this field according to the presence bitmap.
                    if (segSortedDvs.Presence.TryGetValue(field, out var presenceBitmap) &&
                        presenceBitmap is not null && !presenceBitmap.Contains(oldDocId))
                        continue;
                    if (!ctx.SortedDocValues.TryGetValue(field, out var dst))
                    {
                        dst = new string?[ctx.TotalDocs];
                        ctx.SortedDocValues[field] = dst;
                    }
                    dst[remapDocId] = arr[oldDocId];
                }

                CopyMergedMultiValues(segSortedSetDvs, ctx.SortedSetDocValues, oldDocId, remapDocId, ctx.TotalDocs);
                CopyMergedMultiValues(segSortedNumericDvs, ctx.SortedNumericDocValues, oldDocId, remapDocId, ctx.TotalDocs);
                CopyMergedMultiValues(segBinaryDvs, ctx.BinaryDocValues, oldDocId, remapDocId, ctx.TotalDocs);

                if (ctx.TermVectorWriter is not null)
                {
                    var tv = segHasTermVectors ? reader.GetTermVectors(oldDocId) : null;
                    ctx.TermVectorWriter.AddDocument(tv);
                }

                if (segParentBitSet is not null && segParentBitSet.IsParent(oldDocId))
                {
                    ctx.ParentBitSet ??= new ParentBitSet(ctx.TotalDocs);
                    ctx.ParentBitSet.Set(remapDocId);
                }

                if (reader.HasVectors)
                {
                    foreach (var vfName in reader.VectorFieldNames)
                    {
                        var vec = reader.GetVector(vfName, oldDocId);
                        if (vec is null || vec.Length == 0) continue;
                        if (!ctx.Vectors.TryGetValue(vfName, out var perField))
                        {
                            perField = new Dictionary<int, ReadOnlyMemory<float>>();
                            ctx.Vectors[vfName] = perField;
                        }
                        perField[remapDocId] = vec;
                        ctx.VectorFieldDims[vfName] = vec.Length;

                        if (!ctx.VectorFieldRemaps.TryGetValue(vfName, out var remapList))
                        {
                            remapList = new List<(SegmentInfo, Dictionary<int, int>)>();
                            ctx.VectorFieldRemaps[vfName] = remapList;
                        }
                        var entry = remapList.FirstOrDefault(t => ReferenceEquals(t.Seg, segInfo));
                        if (entry.OldToNew is null)
                        {
                            entry = (segInfo, new Dictionary<int, int>());
                            remapList.Add(entry);
                        }
                        entry.OldToNew[oldDocId] = remapDocId;

                        var match = reader.Info.VectorFields.FirstOrDefault(vf => vf.FieldName == vfName);
                        if (match is not null)
                        {
                            ctx.VectorFieldNormalised[vfName] = match.Normalised;
                            ctx.VectorFieldHadHnsw[vfName] = ctx.VectorFieldHadHnsw.GetValueOrDefault(vfName, false) || match.HasHnsw;
                        }
                    }
                }
            }
        }
    }

    private static void WriteNorms(
        IReadOnlyList<SegmentInfo> segments,
        IReadOnlyDictionary<string, SegmentReader> readers,
        IReadOnlyCollection<string> fieldNames,
        string basePath,
        int totalDocs)
    {
        var fieldNorms = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var fieldName in fieldNames)
        {
            var arr = new float[totalDocs];
            int idx = 0;
            foreach (var segInfo in segments)
            {
                var reader = readers[segInfo.SegmentId];
                for (int oldDocId = 0; oldDocId < segInfo.DocCount; oldDocId++)
                {
                    if (!reader.IsLive(oldDocId)) continue;
                    arr[idx++] = reader.GetNorm(oldDocId, fieldName);
                }
            }
            fieldNorms[fieldName] = arr;
        }
        NormsWriter.Write(basePath + ".nrm", fieldNorms);
    }

    private List<VectorFieldInfo> MergeVectors(MergeContext ctx, string basePath)
    {
        var merged = new List<VectorFieldInfo>();
        foreach (var (fieldName, perField) in ctx.Vectors)
        {
            if (perField.Count == 0) continue;
            int dimension = ctx.VectorFieldDims[fieldName];
            if (!ctx.VectorFieldNormalised.TryGetValue(fieldName, out var normalised))
                throw new InvalidOperationException(
                    $"Cannot determine Normalised flag for vector field '{fieldName}' during merge. Source segments must declare this flag.");

            var vecPath = Codecs.Vectors.VectorFilePaths.VectorFile(basePath, fieldName);
            VectorWriter.WriteField(vecPath, ctx.TotalDocs, dimension, perField);

            bool hasHnsw = false;
            if (ctx.VectorFieldHadHnsw.GetValueOrDefault(fieldName, false) && perField.Count >= 2)
            {
                var src = new InMemoryVectorSource(new Dictionary<int, ReadOnlyMemory<float>>(perField), dimension);
                var hnswSw = System.Diagnostics.Stopwatch.StartNew();

                HnswGraph? graph = null;
                if (ctx.VectorFieldRemaps.TryGetValue(fieldName, out var remapList) && remapList.Count > 0)
                {
                    var seed = remapList
                        .Where(t => t.Seg.VectorFields.Any(vf => vf.FieldName == fieldName && vf.HasHnsw))
                        .OrderByDescending(t => t.OldToNew.Count)
                        .FirstOrDefault();

                    if (seed.OldToNew is not null && seed.OldToNew.Count > 0)
                    {
                        var seedHnswPath = Codecs.Vectors.VectorFilePaths.HnswFile(
                            Path.Combine(_directory.DirectoryPath, seed.Seg.SegmentId), fieldName);
                        if (File.Exists(seedHnswPath))
                        {
                            try
                            {
                                graph = HnswReader.Read(seedHnswPath, src, normalised, seed.OldToNew);
                                graph.Thaw();
                                foreach (var docId in perField.Keys)
                                    if (!graph.ContainsNode(docId)) graph.Insert(docId);
                            }
                            catch
                            {
                                graph = null;
                            }
                        }
                    }
                }

                if (graph is null)
                {
                    var docIds = perField.Keys.ToArray();
                    graph = HnswGraphBuilder.Build(src, docIds, new HnswBuildConfig());
                }
                else
                {
                    graph.Freeze();
                }

                hnswSw.Stop();
                _metrics.RecordHnswBuild(hnswSw.Elapsed, perField.Count);
                var hnswPath = Codecs.Vectors.VectorFilePaths.HnswFile(basePath, fieldName);
                HnswWriter.Write(hnswPath, graph, dimension, normalised);
                hasHnsw = true;
            }

            merged.Add(new VectorFieldInfo
            {
                FieldName = fieldName,
                Dimension = dimension,
                Normalised = normalised,
                HasHnsw = hasHnsw,
            });
        }
        return merged;
    }

    private static void WriteNumericFiles(MergeContext ctx, string basePath)
    {
        if (ctx.NumericFields.Count > 0)
            WriteNumericIndex(basePath + ".num", ctx.NumericFields);
    }

    private static void WriteFieldLengthsAndStats(
        MergeContext ctx,
        IReadOnlyCollection<string> fieldNames,
        string basePath,
        string newSegId,
        int totalDocs)
    {
        if (ctx.FieldLengths.Count > 0)
            FieldLengthWriter.Write(basePath + ".fln", ctx.FieldLengths, totalDocs);

        var dirPath = Path.GetDirectoryName(basePath)!;
        SegmentStats.FromFieldLengths(totalDocs, totalDocs, fieldNames, ctx.FieldLengths)
            .WriteTo(SegmentStats.GetStatsPath(dirPath, newSegId));
    }

    private static void WriteDocValueColumns(MergeContext ctx, string basePath)
    {
        if (ctx.NumericDocValues.Count > 0)
        {
            using var output = new Store.IndexOutput(basePath + ".dvn");
            CodecConstants.WriteHeader(output, CodecConstants.NumericDocValuesVersion);
            output.WriteInt32(ctx.NumericDocValues.Count);
            var fieldKeys = ctx.NumericDocValues.Keys.ToList();
            foreach (var field in fieldKeys)
            {
                // Derive presence from the sparse numeric index (documents that truly have this field).
                ctx.NumericFields.TryGetValue(field, out var sparseMap);
                IReadOnlySet<int>? presenceSet = sparseMap is not null
                    ? (IReadOnlySet<int>)sparseMap.Keys.ToHashSet()
                    : null;
                NumericDocValuesWriter.WriteFieldBlock(output, field, ctx.NumericDocValues[field], ctx.TotalDocs, presenceSet);
                ctx.NumericDocValues.Remove(field);
            }
        }
        if (ctx.SortedDocValues.Count > 0)
        {
            using var output = new Store.IndexOutput(basePath + ".dvs");
            CodecConstants.WriteHeader(output, CodecConstants.SortedDocValuesVersion);
            output.WriteInt32(ctx.SortedDocValues.Count);
            var fieldKeys = ctx.SortedDocValues.Keys.ToList();
            foreach (var field in fieldKeys)
            {
                SortedDocValuesWriter.WriteFieldBlock(output, field, ctx.SortedDocValues[field], ctx.TotalDocs);
                ctx.SortedDocValues.Remove(field);
            }
        }
        if (ctx.SortedSetDocValues.Count > 0)
            SortedSetDocValuesWriter.Write(basePath + ".dss", ctx.SortedSetDocValues, ctx.TotalDocs);
        if (ctx.SortedNumericDocValues.Count > 0)
            SortedNumericDocValuesWriter.Write(basePath + ".dsn", ctx.SortedNumericDocValues, ctx.TotalDocs);
        if (ctx.BinaryDocValues.Count > 0)
            BinaryDocValuesWriter.Write(basePath + ".dvb", ctx.BinaryDocValues, ctx.TotalDocs);
    }

    private static void AddMergedMultiValue<T>(
        Dictionary<string, IReadOnlyList<T>?[]> destination,
        string field,
        int docId,
        int totalDocs,
        IReadOnlyList<T> values)
    {
        if (!destination.TryGetValue(field, out var perDoc))
        {
            perDoc = new IReadOnlyList<T>?[totalDocs];
            destination[field] = perDoc;
        }

        perDoc[docId] = values.ToArray();
    }

    private static void CopyMergedMultiValues<T>(
        Dictionary<string, T[][]> source,
        Dictionary<string, IReadOnlyList<T>?[]> destination,
        int oldDocId,
        int remapDocId,
        int totalDocs)
    {
        foreach (var (field, perDocValues) in source)
        {
            if ((uint)oldDocId >= (uint)perDocValues.Length || perDocValues[oldDocId].Length == 0)
                continue;

            AddMergedMultiValue(destination, field, remapDocId, totalDocs, perDocValues[oldDocId]);
        }
    }

    private static void WriteBkdTree(MergeContext ctx, string basePath)
    {
        if (ctx.NumericFields.Count == 0) return;
        var bkdData = new Dictionary<string, List<(double Value, int DocId)>>(StringComparer.Ordinal);
        foreach (var (field, values) in ctx.NumericFields)
        {
            var points = new List<(double Value, int DocId)>(values.Count);
            foreach (var (docId, value) in values)
                points.Add((value, docId));
            bkdData[field] = points;
        }
        if (bkdData.Count > 0)
            BKDWriter.Write(basePath + ".bkd", bkdData);
    }

    private static void WriteParentBitSet(MergeContext ctx, string basePath)
    {
        ctx.ParentBitSet?.WriteTo(basePath + ".pbs");
    }


    internal void CleanupSegmentFiles(SegmentInfo seg)
    {
        // Delete every file belonging to this segment (any extension).
        foreach (var filePath in Directory.GetFiles(_directory.DirectoryPath, $"{seg.SegmentId}.*"))
        {
            try { File.Delete(filePath); }
            catch { /* best-effort cleanup */ }
        }
        // Also sweep generation-versioned deletion files (e.g. seg_0_gen_3.del).
        foreach (var filePath in Directory.GetFiles(_directory.DirectoryPath, $"{seg.SegmentId}_gen_*.del"))
        {
            try { File.Delete(filePath); }
            catch { /* best-effort cleanup */ }
        }
    }

    private static int GetSizeTier(int docCount)
    {
        if (docCount <= 0) return 0;
        return (int)Math.Log10(Math.Max(1, docCount));
    }

    private static void WriteNumericIndex(string filePath, Dictionary<string, Dictionary<int, double>> numericIndex)
    {
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        writer.Write(numericIndex.Count);
        foreach (var (fieldName, docValues) in numericIndex)
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

    private static Dictionary<string, Dictionary<int, double>> ReadNumericIndex(string filePath)
    {
        var result = new Dictionary<string, Dictionary<int, double>>(StringComparer.Ordinal);
        if (!File.Exists(filePath))
            return result;

        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false);

        int fieldCount = reader.ReadInt32();
        for (int f = 0; f < fieldCount; f++)
        {
            string fieldName = reader.ReadString();
            int entryCount = reader.ReadInt32();
            var fieldMap = new Dictionary<int, double>(entryCount);
            for (int e = 0; e < entryCount; e++)
            {
                int docId = reader.ReadInt32();
                double value = reader.ReadDouble();
                fieldMap[docId] = value;
            }
            result[fieldName] = fieldMap;
        }

        return result;
    }
}
