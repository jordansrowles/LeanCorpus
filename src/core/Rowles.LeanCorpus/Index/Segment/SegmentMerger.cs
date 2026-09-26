using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.DocValues;
using Rowles.LeanCorpus.Codecs.Hnsw;
using Rowles.LeanCorpus.Codecs.Bkd;
using Rowles.LeanCorpus.Codecs.Postings;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Codecs.TermDictionary;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Index.Segment;

/// <summary>
/// Tiered merge policy. When the number of segments at a given size tier
/// exceeds a configurable threshold, the smallest segments in that tier
/// are merged into one. Old segments are removed only after the merged
/// segment is fully committed.
/// </summary>
public sealed class SegmentMerger
{
    private readonly MMapDirectory _directory;
    private readonly IMergePolicy _mergePolicy;
    private readonly int _skipInterval;
    private readonly double _softDeleteRetentionSeconds;
    private readonly Diagnostics.IMetricsCollector _metrics;
    private readonly HnswBuildConfig _hnswBuildConfig;
    private readonly bool _useCompoundFile;
    private readonly VectorQuantisation _destinationVectorQuantisation;

    internal CodecCatalog FileCatalog { get; set; } = CodecCatalog.Default;

    /// <summary>Default merge threshold: when this many segments exist, merge.</summary>
    public const int DefaultMergeThreshold = 10;

    /// <summary>Default postings skip interval.</summary>
    public const int DefaultSkipInterval = 128;

    /// <summary>Default soft-delete retention period in seconds (24 hours).</summary>
    public const double DefaultSoftDeleteRetentionSeconds = 86400.0;

    /// <summary>Initialises a merger bound to the given directory.</summary>
    /// <param name="directory">The directory holding segment files.</param>
    /// <param name="mergePolicy">The merge policy used to select segments for merging.</param>
    /// <param name="skipInterval">Postings skip interval used when writing the merged segment.</param>
    /// <param name="softDeleteRetentionSeconds">Minimum seconds to retain soft-deleted documents during merge.</param>
    /// <param name="hnswBuildConfig">HNSW build configuration used when rebuilding vector graphs during merge.</param>
    /// <param name="metrics">Optional metrics collector. Defaults to <see cref="Diagnostics.NullMetricsCollector.Instance"/>.</param>
    /// <param name="useCompoundFile">Whether merged immutable codec files should be packed into a compound file.</param>
    public SegmentMerger(
        MMapDirectory directory,
        IMergePolicy mergePolicy,
        int skipInterval = DefaultSkipInterval,
        double softDeleteRetentionSeconds = DefaultSoftDeleteRetentionSeconds,
        HnswBuildConfig? hnswBuildConfig = null,
        Diagnostics.IMetricsCollector? metrics = null,
        bool useCompoundFile = false)
        : this(directory, mergePolicy, skipInterval, softDeleteRetentionSeconds, hnswBuildConfig,
            metrics, useCompoundFile, VectorQuantisation.None)
    {
    }

    internal SegmentMerger(
        MMapDirectory directory,
        IMergePolicy mergePolicy,
        int skipInterval,
        double softDeleteRetentionSeconds,
        HnswBuildConfig? hnswBuildConfig,
        Diagnostics.IMetricsCollector? metrics,
        bool useCompoundFile,
        VectorQuantisation destinationVectorQuantisation)
    {
        _directory = directory;
        _mergePolicy = mergePolicy ?? new TieredMergePolicy(DefaultMergeThreshold);
        _skipInterval = skipInterval;
        _softDeleteRetentionSeconds = softDeleteRetentionSeconds;
        _hnswBuildConfig = hnswBuildConfig ?? new HnswBuildConfig();
        _metrics = metrics ?? Diagnostics.NullMetricsCollector.Instance;
        _useCompoundFile = useCompoundFile;
        _destinationVectorQuantisation = destinationVectorQuantisation;
    }

    internal SegmentMerger(
        MMapDirectory directory,
        IMergePolicy mergePolicy,
        int skipInterval,
        double softDeleteRetentionSeconds,
        HnswBuildConfig? hnswBuildConfig,
        bool useCompoundFile,
        VectorQuantisation destinationVectorQuantisation)
        : this(directory, mergePolicy, skipInterval, softDeleteRetentionSeconds, hnswBuildConfig,
            metrics: null,
            useCompoundFile: useCompoundFile,
            destinationVectorQuantisation: destinationVectorQuantisation)
    {
    }

    /// <summary>Initialises a merger bound to the given directory with the default tiered policy.</summary>
    /// <param name="directory">The directory holding segment files.</param>
    /// <param name="mergeThreshold">Number of segments at one tier before a merge is triggered.</param>
    /// <param name="skipInterval">Postings skip interval used when writing the merged segment.</param>
    /// <param name="softDeleteRetentionSeconds">Minimum seconds to retain soft-deleted documents during merge.</param>
    /// <param name="hnswBuildConfig">HNSW build configuration used when rebuilding vector graphs during merge.</param>
    /// <param name="metrics">Optional metrics collector. Defaults to <see cref="Diagnostics.NullMetricsCollector.Instance"/>.</param>
    public SegmentMerger(
        MMapDirectory directory,
        int mergeThreshold,
        int skipInterval = DefaultSkipInterval,
        double softDeleteRetentionSeconds = DefaultSoftDeleteRetentionSeconds,
        HnswBuildConfig? hnswBuildConfig = null,
        Diagnostics.IMetricsCollector? metrics = null)
        : this(directory, new TieredMergePolicy(mergeThreshold), skipInterval, softDeleteRetentionSeconds, hnswBuildConfig, metrics)
    {
    }

    /// <summary>
    /// Checks if a merge is needed and performs it. Returns the updated segment list.
    /// </summary>
    public List<SegmentInfo> MaybeMerge(List<SegmentInfo> segments, ref int nextSegmentOrdinal)
        => MaybeMerge(segments, ref nextSegmentOrdinal, new HashSet<string>(StringComparer.Ordinal), commitGeneration: 0);

    /// <summary>
    /// Checks if a merge is needed and performs it, excluding segments protected by held snapshots.
    /// </summary>
    /// <param name="segments">The committed segments currently visible to the writer.</param>
    /// <param name="nextSegmentOrdinal">The next segment ordinal to allocate if a merge is performed.</param>
    /// <param name="protectedSegmentIds">Segment IDs that must not be merged or deleted while snapshots are held.</param>
    /// <param name="commitGeneration">The commit generation to assign to the merged segment.</param>
    /// <returns>The original list when no merge is needed; otherwise, a new list containing merged replacements.</returns>
    public List<SegmentInfo> MaybeMerge(
        List<SegmentInfo> segments,
        ref int nextSegmentOrdinal,
        IReadOnlySet<string> protectedSegmentIds,
        int commitGeneration = 0)
    {
        var result = new List<SegmentInfo>(segments);
        bool anyMerged = false;

        while (true)
        {
            var toMerge = _mergePolicy.FindMerges(result, protectedSegmentIds);
            if (toMerge.Count < 2)
                break;

            // Merge policies select candidates by size and deletion density, but document IDs
            // must follow the committed segment order rather than an unstable policy sort.
            var selectedIds = new HashSet<string>(
                toMerge.Select(static segment => segment.SegmentId),
                StringComparer.Ordinal);
            var orderedForMerge = result.Where(segment => selectedIds.Contains(segment.SegmentId)).ToList();
            var merged = MergeSegments(
                orderedForMerge,
                ref nextSegmentOrdinal, commitGeneration);
            if (merged == null)
                break;

            foreach (var seg in toMerge)
                result.Remove(seg);
            result.Add(merged);
            anyMerged = true;
        }

        return anyMerged ? result : segments;
    }

    /// <summary>
    /// Forces a full merge of all given segments into a single new segment,
    /// bypassing tier-based merge policy. Used by <see cref="IndexWriter.Compact"/>.
    /// </summary>
    /// <param name="segments">All segments to merge into one.</param>
    /// <param name="nextSegmentOrdinal">Ordinal counter for naming the output segment.</param>
    /// <param name="commitGeneration">The commit generation to assign to the merged segment.</param>
    /// <returns>The merged segment, or <c>null</c> if no live documents remain.</returns>
    public SegmentInfo? MergeAll(List<SegmentInfo> segments, ref int nextSegmentOrdinal, int commitGeneration = 0)
    {
        if (segments.Count == 0)
            return null;

        return MergeSegments(segments, ref nextSegmentOrdinal, commitGeneration);
    }

    private SegmentInfo? MergeSegments(List<SegmentInfo> segments, ref int nextSegmentOrdinal, int commitGeneration)
    {
        List<SpatialFieldInfo> spatialFields = MergeSpatialFieldMetadata(segments);
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

            return MergeSegmentsCore(segments, readers, newSegId, basePath, commitGeneration, spatialFields,
                _destinationVectorQuantisation);
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
        string basePath,
        int commitGeneration,
        List<SpatialFieldInfo> spatialFields,
        VectorQuantisation destinationVectorQuantisation)
    {
        // Phase 1: build per-segment doc-id remaps. Compatible, physically sorted inputs
        // are merged by their complete index-sort key; all other inputs retain committed
        // segment order and must not claim index-sort metadata in the output.
        long softDeleteCutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)(_softDeleteRetentionSeconds * 1000);
        bool hasCompatibleIndexSort = TryGetCommonIndexSort(segments, out SortField[] sortFields);
        var sortedMaps = new List<(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader)>(segments.Count);
        var sortedSoftDeletes = new List<(int DocId, long Timestamp)>();
        int sortedDocCount = 0;
        bool hasSortedOutputOrder = hasCompatibleIndexSort
            && TryBuildSortedDocumentMaps(
                segments,
                readers,
                sortFields,
                softDeleteCutoff,
                out sortedMaps,
                out sortedDocCount,
                out sortedSoftDeletes);
        List<(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader)> perSegmentMaps;
        List<(int DocId, long Timestamp)> retainedSoftDeletes;
        int totalDocs;
        if (hasSortedOutputOrder)
        {
            perSegmentMaps = sortedMaps;
            retainedSoftDeletes = sortedSoftDeletes;
            totalDocs = sortedDocCount;
        }
        else
        {
            perSegmentMaps = BuildSequentialDocumentMaps(
                segments,
                readers,
                softDeleteCutoff,
                out totalDocs,
                out retainedSoftDeletes);
        }
        if (totalDocs == 0) return null;

        Dictionary<string, VectorFieldContract> vectorContracts = PreflightVectorContracts(segments);

        MergeDocument[] documentOrder = BuildDestinationDocumentOrder(perSegmentMaps, totalDocs);

        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segInfo in segments)
            foreach (var field in segInfo.FieldNames)
                fieldNames.Add(field);

        // Phase 2: streaming postings + dictionary merge. Bounds RAM at one term's
        // worth of decoded postings rather than the whole inverted index.
        MergePostings(perSegmentMaps, basePath);

        // Phase 3: per-doc payloads. Stored fields and term vectors are streamed to
        // disk doc-by-doc; doc-values columns still buffer (codec format requires it).
        using var ctx = new MergeContext(totalDocs, fieldNames);
        bool anyTermVectors = readers.Values.Any(r => r.HasTermVectors);
        using (var storedWriter = new StoredFieldsStreamWriter(basePath + ".fdt", basePath + ".fdx"))
        using (var tvWriter = anyTermVectors ? new TermVectorsStreamWriter(basePath + ".tvd", basePath + ".tvx") : null)
        {
            ctx.StoredWriter = storedWriter;
            ctx.TermVectorWriter = tvWriter;
            AccumulateDocPayloads(perSegmentMaps, documentOrder, ctx);
        }

        // Phase 4: emit per-codec output files.
        WriteNorms(perSegmentMaps, readers, fieldNames, basePath, totalDocs);
        var mergedVectorFields = MergeVectors(
            ctx, documentOrder, basePath, vectorContracts, destinationVectorQuantisation);
        WriteNumericFiles(ctx, basePath);
        WriteFieldLengthsAndStats(ctx, fieldNames, basePath, newSegId, totalDocs);
        WriteDocValueColumns(ctx, basePath);
        WriteBkdTree(ctx, basePath);
        WritePackedBkdTree(ctx, basePath);
        WriteShapeDocValues(ctx, basePath);
        WriteParentBitSet(ctx, basePath);

        LiveDocs? mergedLiveDocs = null;
        if (retainedSoftDeletes.Count > 0)
        {
            mergedLiveDocs = new LiveDocs(totalDocs);
            foreach (var (docId, timestamp) in retainedSoftDeletes)
                mergedLiveDocs.SoftDelete(docId, timestamp);
            LiveDocs.Serialise(basePath + ".del", mergedLiveDocs);
        }

        var mergedInfo = new SegmentInfo
        {
            SegmentId = newSegId,
            DocCount = totalDocs,
            LiveDocCount = mergedLiveDocs?.LiveCount ?? totalDocs,
            CommitGeneration = commitGeneration,
            FieldNames = fieldNames.ToList(),
            IndexSortFields = hasSortedOutputOrder && segments[0].IndexSortFields is { } sortMetadata
                ? [.. sortMetadata]
                : null,
            VectorFields = mergedVectorFields,
            SpatialFields = spatialFields,
            MinSequenceNumber = ComputeMergedMinSeqNo(segments),
            MaxSequenceNumber = ComputeMergedMaxSeqNo(segments),
            EarliestSoftDeleteTimestamp = mergedLiveDocs?.EarliestSoftDeleteTimestamp,
        };
        if (_useCompoundFile && CompoundFileWriter.Pack(_directory.DirectoryPath, newSegId, FileCatalog))
            mergedInfo.IsCompoundFile = true;
        SegmentFlusher.RefreshSegmentSize(mergedInfo, _directory.DirectoryPath, FileCatalog);
        return mergedInfo;
    }

    private static List<SpatialFieldInfo> MergeSpatialFieldMetadata(List<SegmentInfo> segments)
    {
        var fields = new Dictionary<string, SpatialFieldKind>(StringComparer.Ordinal);
        foreach (SegmentInfo segment in segments)
        {
            segment.Validate();
            foreach (SpatialFieldInfo spatialField in segment.SpatialFields)
            {
                if (fields.TryGetValue(spatialField.FieldName, out SpatialFieldKind existing)
                    && existing != spatialField.Kind)
                    throw new InvalidDataException(
                        $"Spatial field '{spatialField.FieldName}' has incompatible kinds '{existing}' and '{spatialField.Kind}' during merge.");

                fields[spatialField.FieldName] = spatialField.Kind;
            }
        }

        return fields
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => new SpatialFieldInfo { FieldName = pair.Key, Kind = pair.Value })
            .ToList();
    }

    private static Dictionary<string, VectorFieldContract> PreflightVectorContracts(
        IReadOnlyList<SegmentInfo> segments)
    {
        var contracts = new Dictionary<string, VectorFieldContract>(StringComparer.Ordinal);
        var firstSourceSegments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (SegmentInfo segment in segments)
        {
            foreach (VectorFieldInfo field in segment.VectorFields)
            {
                var contract = new VectorFieldContract(field.Dimension, field.Normalised);
                if (!contracts.TryGetValue(field.FieldName, out VectorFieldContract existing))
                {
                    contracts.Add(field.FieldName, contract);
                    firstSourceSegments.Add(field.FieldName, segment.SegmentId);
                    continue;
                }

                if (existing.Dimension != contract.Dimension)
                    throw new InvalidDataException(
                        $"Cannot merge vector field '{field.FieldName}': segment '{segment.SegmentId}' has dimension {contract.Dimension}, " +
                        $"which differs from dimension {existing.Dimension} in segment '{firstSourceSegments[field.FieldName]}'.");

                if (existing.Normalised != contract.Normalised)
                    throw new InvalidDataException(
                        $"Cannot merge vector field '{field.FieldName}': segment '{segment.SegmentId}' has Normalised={contract.Normalised}, " +
                        $"which differs from Normalised={existing.Normalised} in segment '{firstSourceSegments[field.FieldName]}'.");
            }
        }

        return contracts;
    }

    private readonly record struct VectorFieldContract(int Dimension, bool Normalised);

    private static bool TryGetCommonIndexSort(List<SegmentInfo> segments, out SortField[] sortFields)
    {
        sortFields = [];
        if (segments.Count == 0 || segments[0].IndexSortFields is not { Count: > 0 } common)
            return false;

        foreach (SegmentInfo segment in segments)
        {
            if (segment.IndexSortFields is not { Count: > 0 } fields
                || !fields.SequenceEqual(common, StringComparer.Ordinal))
                return false;
        }

        var parsed = new SortField[common.Count];
        for (int i = 0; i < common.Count; i++)
        {
            string[] parts = common[i].Split(':');
            if (parts.Length is < 3 or > 4
                || !Enum.TryParse(parts[0], ignoreCase: false, out SortFieldType type)
                || !Enum.IsDefined(type)
                || type is not (SortFieldType.DocId or SortFieldType.Numeric or SortFieldType.Int64 or SortFieldType.String)
                || (type == SortFieldType.DocId ? parts[1].Length != 0 : string.IsNullOrWhiteSpace(parts[1]))
                || !bool.TryParse(parts[2], out bool descending))
                return false;

            SortValueSelector selector = SortValueSelector.Min;
            if (parts.Length == 4
                && (!Enum.TryParse(parts[3], ignoreCase: false, out selector)
                    || !Enum.IsDefined(selector)))
                return false;

            parsed[i] = new SortField(type, parts[1], descending, selector);
        }

        // A descending DocId key is the pre-flush document ID. That value is not
        // persisted after SegmentFlusher physically reorders a segment, so a merge
        // cannot reconstruct a globally correct key from the source segments.
        if (parsed.Any(static field => field.Type == SortFieldType.DocId && field.Descending))
            return false;

        sortFields = parsed;
        return true;
    }

    private bool TryBuildSortedDocumentMaps(
        List<SegmentInfo> segments,
        IReadOnlyDictionary<string, SegmentReader> readers,
        SortField[] sortFields,
        long softDeleteCutoff,
        out List<(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader)> perSegmentMaps,
        out int totalDocs,
        out List<(int DocId, long Timestamp)> retainedSoftDeletes)
    {
        perSegmentMaps = new List<(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader)>(segments.Count);
        retainedSoftDeletes = [];
        totalDocs = 0;

        var queue = new PriorityQueue<MergeSortCursor, MergeSortCursor>(
            segments.Count,
            new MergeSortCursorComparer(sortFields));

        for (int i = 0; i < segments.Count; i++)
        {
            SegmentInfo segment = segments[i];
            SegmentReader reader = readers[segment.SegmentId];
            var docIdMap = new int[segment.DocCount];
            Array.Fill(docIdMap, -1);
            perSegmentMaps.Add((segment, docIdMap, reader));

            var cursor = new MergeSortCursor(
                segment,
                reader,
                docIdMap,
                sortFields,
                i,
                ShouldRetainSoftDeletes(segment),
                softDeleteCutoff);
            if (cursor.TryAdvance())
                queue.Enqueue(cursor, cursor);
            else if (!cursor.IsInputSorted)
                return false;
        }

        while (queue.TryDequeue(out MergeSortCursor? cursor, out _))
        {
            int newDocId = totalDocs++;
            cursor.DocIdMap[cursor.CurrentOldDocId] = newDocId;
            if (cursor.CurrentIsRetainedSoftDelete)
                retainedSoftDeletes.Add((newDocId, cursor.CurrentSoftDeleteTimestamp));

            if (cursor.TryAdvance())
                queue.Enqueue(cursor, cursor);
            else if (!cursor.IsInputSorted)
                return false;
        }

        return true;
    }

    private static List<(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader)> BuildSequentialDocumentMaps(
        List<SegmentInfo> segments,
        IReadOnlyDictionary<string, SegmentReader> readers,
        long softDeleteCutoff,
        out int totalDocs,
        out List<(int DocId, long Timestamp)> retainedSoftDeletes)
    {
        var perSegmentMaps = new List<(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader)>(segments.Count);
        retainedSoftDeletes = [];
        totalDocs = 0;
        foreach (SegmentInfo segment in segments)
        {
            SegmentReader reader = readers[segment.SegmentId];
            var docIdMap = new int[segment.DocCount];
            bool retainSoftDeletes = ShouldRetainSoftDeletes(segment);
            for (int oldDocId = 0; oldDocId < segment.DocCount; oldDocId++)
            {
                if (reader.IsLive(oldDocId))
                {
                    docIdMap[oldDocId] = totalDocs++;
                    continue;
                }

                if (retainSoftDeletes
                    && reader.IsSoftDeleted(oldDocId, out long timestamp)
                    && timestamp > softDeleteCutoff)
                {
                    int retainedDocId = totalDocs++;
                    docIdMap[oldDocId] = retainedDocId;
                    retainedSoftDeletes.Add((retainedDocId, timestamp));
                    continue;
                }

                docIdMap[oldDocId] = -1;
            }

            perSegmentMaps.Add((segment, docIdMap, reader));
        }

        return perSegmentMaps;
    }

    private static MergeDocument[] BuildDestinationDocumentOrder(
        IReadOnlyList<(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader)> perSegmentMaps,
        int totalDocs)
    {
        var documentOrder = new MergeDocument[totalDocs];
        foreach ((SegmentInfo segment, int[] docIdMap, SegmentReader reader) in perSegmentMaps)
        {
            for (int oldDocId = 0; oldDocId < docIdMap.Length; oldDocId++)
            {
                int newDocId = docIdMap[oldDocId];
                if (newDocId < 0)
                    continue;
                if ((uint)newDocId >= (uint)documentOrder.Length || documentOrder[newDocId].Segment is not null)
                    throw new InvalidDataException("The merge document remap contains duplicate or out-of-range destination IDs.");

                documentOrder[newDocId] = new MergeDocument(segment, reader, oldDocId);
            }
        }

        for (int newDocId = 0; newDocId < documentOrder.Length; newDocId++)
            if (documentOrder[newDocId].Segment is null)
                throw new InvalidDataException("The merge document remap contains a gap in destination IDs.");

        return documentOrder;
    }

    private static int CompareSortValues(
        IReadOnlyList<SortField> sortFields,
        ReadOnlySpan<MergeSortValue> left,
        ReadOnlySpan<MergeSortValue> right)
    {
        for (int i = 0; i < sortFields.Count; i++)
        {
            SortField field = sortFields[i];
            int comparison = field.Type switch
            {
                SortFieldType.Numeric => left[i].NumericValue.CompareTo(right[i].NumericValue),
                SortFieldType.Int64 or SortFieldType.DocId => left[i].Int64Value.CompareTo(right[i].Int64Value),
                SortFieldType.String => string.Compare(left[i].StringValue, right[i].StringValue, StringComparison.Ordinal),
                _ => throw new InvalidDataException($"Index sort type '{field.Type}' is not supported during merge.")
            };

            if (field.Descending)
                comparison = comparison < 0 ? 1 : comparison > 0 ? -1 : 0;
            if (comparison != 0)
                return comparison;
        }

        return 0;
    }

    private sealed class MergeSortValueResolver
    {
        private readonly SegmentReader _reader;
        private readonly SortField _field;
        private readonly double[][]? _sortedNumericValues;
        private readonly double[]? _numericDocValues;
        private readonly Dictionary<int, double>? _numericIndex;
        private readonly long[][]? _sortedInt64Values;
        private readonly long[]? _int64DocValues;
        private readonly Dictionary<int, long>? _int64Index;
        private readonly string[]? _sortedDocValues;
        private readonly string[][]? _sortedSetDocValues;
        private readonly byte[][][]? _binaryDocValues;

        internal MergeSortValueResolver(SegmentReader reader, SortField field)
        {
            _reader = reader;
            _field = field;
            switch (field.Type)
            {
                case SortFieldType.Numeric:
                    _sortedNumericValues = reader.GetSortedNumericDocValues(field.FieldName);
                    _numericDocValues = reader.GetNumericDocValues(field.FieldName);
                    _numericIndex = ReadNumericIndex(reader).GetValueOrDefault(field.FieldName);
                    break;
                case SortFieldType.Int64:
                    _sortedInt64Values = reader.GetSortedInt64DocValues(field.FieldName);
                    _int64DocValues = reader.GetInt64DocValues(field.FieldName);
                    _int64Index = ReadInt64Index(reader).GetValueOrDefault(field.FieldName);
                    break;
                case SortFieldType.String:
                    _sortedDocValues = reader.GetSortedDocValues(field.FieldName);
                    _sortedSetDocValues = reader.GetSortedSetDocValues(field.FieldName);
                    _binaryDocValues = reader.GetBinaryDocValues(field.FieldName);
                    break;
            }
        }

        internal MergeSortValue Read(int oldDocId, ISet<string> storedFieldFilter)
        {
            switch (_field.Type)
            {
                case SortFieldType.Numeric:
                    if (_sortedNumericValues is not null
                        && (uint)oldDocId < (uint)_sortedNumericValues.Length
                        && _sortedNumericValues[oldDocId].Length > 0)
                        return MergeSortValue.Numeric(SegmentFlusher.SelectNumericValue(
                            _sortedNumericValues[oldDocId], _field.Selector));
                    if (_numericDocValues is not null && (uint)oldDocId < (uint)_numericDocValues.Length)
                        return MergeSortValue.Numeric(_numericDocValues[oldDocId]);
                    if (_numericIndex is not null && _numericIndex.TryGetValue(oldDocId, out double numericValue))
                        return MergeSortValue.Numeric(numericValue);
                    if (TryGetStoredSortValue(_reader, _field.FieldName, oldDocId, storedFieldFilter, out var numericStored))
                    {
                        if (numericStored.IsLong)
                            return MergeSortValue.Numeric(numericStored.LongValue);
                        if (numericStored.StringValue is { } numericText
                            && double.TryParse(numericText, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out numericValue))
                            return MergeSortValue.Numeric(numericValue);
                    }
                    return MergeSortValue.Numeric(0);

                case SortFieldType.Int64:
                    if (_sortedInt64Values is not null
                        && (uint)oldDocId < (uint)_sortedInt64Values.Length
                        && _sortedInt64Values[oldDocId].Length > 0)
                        return MergeSortValue.Int64(SegmentFlusher.SelectInt64Value(
                            _sortedInt64Values[oldDocId], _field.Selector));
                    if (_int64DocValues is not null && (uint)oldDocId < (uint)_int64DocValues.Length)
                        return MergeSortValue.Int64(_int64DocValues[oldDocId]);
                    if (_int64Index is not null && _int64Index.TryGetValue(oldDocId, out long int64Value))
                        return MergeSortValue.Int64(int64Value);
                    if (TryGetStoredSortValue(_reader, _field.FieldName, oldDocId, storedFieldFilter, out var int64Stored))
                    {
                        if (int64Stored.IsLong)
                            return MergeSortValue.Int64(int64Stored.LongValue);
                        if (int64Stored.StringValue is { } int64Text
                            && long.TryParse(int64Text, System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out int64Value))
                            return MergeSortValue.Int64(int64Value);
                    }
                    return MergeSortValue.Int64(0);

                case SortFieldType.String:
                    if (_sortedDocValues is not null && (uint)oldDocId < (uint)_sortedDocValues.Length)
                        return MergeSortValue.String(_sortedDocValues[oldDocId]);
                    if (_sortedSetDocValues is not null
                        && (uint)oldDocId < (uint)_sortedSetDocValues.Length
                        && _sortedSetDocValues[oldDocId].Length > 0)
                    {
                        string[] values = _sortedSetDocValues[oldDocId];
                        return MergeSortValue.String(_field.Selector == SortValueSelector.Max
                            ? values[^1]
                            : values[0]);
                    }
                    if (_binaryDocValues is not null
                        && (uint)oldDocId < (uint)_binaryDocValues.Length
                        && _binaryDocValues[oldDocId].Length > 0)
                        return MergeSortValue.String(System.Text.Encoding.UTF8.GetString(_binaryDocValues[oldDocId][0]));
                    if (TryGetStoredSortValue(_reader, _field.FieldName, oldDocId, storedFieldFilter, out var stringStored))
                        return MergeSortValue.String(stringStored.StringValue);
                    return MergeSortValue.String(null);

                case SortFieldType.DocId:
                    return MergeSortValue.Int64(oldDocId);

                default:
                    throw new InvalidDataException($"Index sort type '{_field.Type}' is not supported during merge.");
            }
        }
    }

    private static bool TryGetStoredSortValue(
        SegmentReader reader,
        string fieldName,
        int oldDocId,
        ISet<string> storedFieldFilter,
        out StoredFieldValue value)
    {
        var stored = reader.GetStoredFieldValues(oldDocId, storedFieldFilter);
        if (stored.TryGetValue(fieldName, out IReadOnlyList<StoredFieldValue>? values) && values.Count > 0)
        {
            value = values[0];
            return true;
        }

        value = default;
        return false;
    }

    private readonly record struct MergeDocument(SegmentInfo Segment, SegmentReader Reader, int OldDocId);

    private readonly record struct MergeSortValue(double NumericValue, long Int64Value, string? StringValue)
    {
        internal static MergeSortValue Numeric(double value) => new(value, 0, null);
        internal static MergeSortValue Int64(long value) => new(0, value, null);
        internal static MergeSortValue String(string? value) => new(0, 0, value);
    }

    private sealed class MergeSortCursor
    {
        private readonly SortField[] _sortFields;
        private readonly long _softDeleteCutoff;
        private readonly bool _retainSoftDeletes;
        private readonly MergeSortValue[] _previousKey;
        private readonly MergeSortValueResolver[] _sortValueResolvers;
        private readonly HashSet<string>[] _storedFieldFilters;
        private bool _hasPreviousKey;
        private int _nextOldDocId;

        internal SegmentInfo Segment { get; }
        internal SegmentReader Reader { get; }
        internal int[] DocIdMap { get; }
        internal int SourceOrdinal { get; }
        internal int CurrentOldDocId { get; private set; }
        internal MergeSortValue[] CurrentKey { get; }
        internal bool CurrentIsRetainedSoftDelete { get; private set; }
        internal long CurrentSoftDeleteTimestamp { get; private set; }
        internal bool IsInputSorted { get; private set; } = true;

        internal MergeSortCursor(
            SegmentInfo segment,
            SegmentReader reader,
            int[] docIdMap,
            SortField[] sortFields,
            int sourceOrdinal,
            bool retainSoftDeletes,
            long softDeleteCutoff)
        {
            Segment = segment;
            Reader = reader;
            DocIdMap = docIdMap;
            SourceOrdinal = sourceOrdinal;
            _sortFields = sortFields;
            _retainSoftDeletes = retainSoftDeletes;
            _softDeleteCutoff = softDeleteCutoff;
            CurrentKey = new MergeSortValue[sortFields.Length];
            _previousKey = new MergeSortValue[sortFields.Length];
            _sortValueResolvers = new MergeSortValueResolver[sortFields.Length];
            _storedFieldFilters = new HashSet<string>[sortFields.Length];
            for (int i = 0; i < sortFields.Length; i++)
            {
                _sortValueResolvers[i] = new MergeSortValueResolver(reader, sortFields[i]);
                _storedFieldFilters[i] = new HashSet<string>(StringComparer.Ordinal);
                if (sortFields[i].FieldName.Length > 0)
                    _storedFieldFilters[i].Add(sortFields[i].FieldName);
            }
        }

        internal bool TryAdvance()
        {
            while (_nextOldDocId < Segment.DocCount)
            {
                int oldDocId = _nextOldDocId++;
                bool isLive = Reader.IsLive(oldDocId);
                bool isRetainedSoftDelete = false;
                long softDeleteTimestamp = 0;
                if (!isLive)
                {
                    if (!_retainSoftDeletes
                        || !Reader.IsSoftDeleted(oldDocId, out softDeleteTimestamp)
                        || softDeleteTimestamp <= _softDeleteCutoff)
                        continue;
                    isRetainedSoftDelete = true;
                }

                for (int i = 0; i < _sortFields.Length; i++)
                    CurrentKey[i] = _sortValueResolvers[i].Read(oldDocId, _storedFieldFilters[i]);

                if (_hasPreviousKey
                    && CompareSortValues(_sortFields, _previousKey, CurrentKey) > 0)
                {
                    IsInputSorted = false;
                    return false;
                }

                Array.Copy(CurrentKey, _previousKey, CurrentKey.Length);
                _hasPreviousKey = true;
                CurrentOldDocId = oldDocId;
                CurrentIsRetainedSoftDelete = isRetainedSoftDelete;
                CurrentSoftDeleteTimestamp = softDeleteTimestamp;
                return true;
            }

            return false;
        }
    }

    private sealed class MergeSortCursorComparer(SortField[] sortFields) : IComparer<MergeSortCursor>
    {
        public int Compare(MergeSortCursor? left, MergeSortCursor? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            int comparison = CompareSortValues(sortFields, left.CurrentKey, right.CurrentKey);
            if (comparison != 0)
                return comparison;

            comparison = left.SourceOrdinal.CompareTo(right.SourceOrdinal);
            return comparison != 0
                ? comparison
                : left.CurrentOldDocId.CompareTo(right.CurrentOldDocId);
        }
    }

    /// <summary>
    /// Accumulator for per-doc data structures threaded through the merge phases.
    /// Owns nothing; lifetime is the merge call.
    /// </summary>
    private sealed class MergeContext : IDisposable
    {
        internal int TotalDocs { get; }
        internal HashSet<string> FieldNames { get; }
        internal StoredFieldsStreamWriter? StoredWriter { get; set; }
        internal TermVectorsStreamWriter? TermVectorWriter { get; set; }
        internal Dictionary<string, Dictionary<int, double>> NumericFields { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, Dictionary<int, long>> Int64Fields { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, int[]> FieldLengths { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, float[]> FieldBoosts { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, double[]> NumericDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, long[]> Int64DocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, string?[]> SortedDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<string>?[]> SortedSetDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<double>?[]> SortedNumericDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<long>?[]> Int64SortedDocValues { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, IReadOnlyList<byte[]>?[]> BinaryDocValues { get; } = new(StringComparer.Ordinal);
        internal ParentBitSet? ParentBitSet { get; set; }
        internal Dictionary<string, List<int>> VectorFieldDocIds { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, PackedBkdFieldBuffer> PackedBkdFields { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, ShapeDocValuesFieldBuffer> ShapeDocValuesFields { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, Dictionary<string, ShapeDocValuesFieldMetadata>> ShapeDocValuesMetadataBySegment { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, bool> VectorFieldHadHnsw { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, List<(SegmentInfo Seg, Dictionary<int, int> OldToNew, SegmentReader Reader)>> VectorFieldRemaps { get; } = new(StringComparer.Ordinal);

        internal MergeContext(int totalDocs, HashSet<string> fieldNames)
        {
            TotalDocs = totalDocs;
            FieldNames = fieldNames;
        }

        public void Dispose()
        {
            foreach (var buffer in PackedBkdFields.Values)
                buffer.Dispose();
            PackedBkdFields.Clear();
            foreach (ShapeDocValuesFieldBuffer buffer in ShapeDocValuesFields.Values)
                buffer.Dispose();
            ShapeDocValuesFields.Clear();
        }
    }

    private void MergePostings(
        IReadOnlyList<(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader)> sources,
        string basePath)
    {
        var merger = new List<StreamingPostingsMerger.Source>(sources.Count);
        foreach (var (_, map, reader) in sources)
        {
            merger.Add(new StreamingPostingsMerger.Source
            {
                OpenInput = reader.OpenInput,
                DocIdMap = map,
            });
        }
        StreamingPostingsMerger.Merge(merger, basePath + ".pos", basePath + ".dic");
    }

    private void AccumulateDocPayloads(
        IReadOnlyList<(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader)> sources,
        MergeDocument[] documentOrder,
        MergeContext ctx)
    {
        foreach (var (segInfo, docIdMap, reader) in sources)
        {
            var segParentBitSet = reader.GetParentBitSet();

            var segFieldLengths = reader.FileExists(".fln")
                ? FieldLengthReader.TryRead(reader.OpenInput(".fln"))
                    ?? new Dictionary<string, int[]>(StringComparer.Ordinal)
                : new Dictionary<string, int[]>(StringComparer.Ordinal);

            var segNumericDvs = ReadNumericDocValues(reader);
            var segSortedDvs = ReadSortedDocValues(reader);
            var segSortedSetDvs = ReadSortedSetDocValues(reader);
            var segSortedNumericDvs = ReadSortedNumericDocValues(reader);
            var segBinaryDvs = ReadBinaryDocValues(reader);
            var segNumericIndex = ReadNumericIndex(reader);
            var segInt64Index = ReadInt64Index(reader);
            var segInt64Dvs = ReadInt64DocValues(reader);
            var segInt64SortedDvs = ReadInt64SortedDocValues(reader);
            var packedFieldNames = reader.GetPackedBkdFieldNames();
            IReadOnlyList<string> shapeDocValuesFieldNames = reader.GetShapeDocValuesFieldNames();
            var shapeDocValuesFields = new Dictionary<string, ShapeDocValuesFieldMetadata>(
                shapeDocValuesFieldNames.Count, StringComparer.Ordinal);
            if (shapeDocValuesFieldNames.Count > 0)
                reader.ValidateShapeDocValuesChecksum();
            foreach (string shapeFieldName in shapeDocValuesFieldNames)
            {
                if (!reader.TryGetShapeDocValuesFieldMetadata(shapeFieldName, out ShapeDocValuesFieldMetadata shapeMetadata))
                    throw new InvalidDataException($"Shape DocValues field '{shapeFieldName}' disappeared during merge.");
                SpatialFieldInfo? segmentKind = segInfo.SpatialFields.FirstOrDefault(
                    field => string.Equals(field.FieldName, shapeFieldName, StringComparison.Ordinal));
                if (segmentKind is null || segmentKind.Kind != shapeMetadata.Kind)
                    throw new InvalidDataException($"Shape DocValues field '{shapeFieldName}' conflicts with segment spatial-field metadata.");
                if (!reader.TryGetPackedBkdFieldMetadata(shapeFieldName, out PackedBkdFieldMetadata packedMetadata)
                    || packedMetadata.DocumentCount < shapeMetadata.RecordCount)
                    throw new InvalidDataException($"Shape DocValues field '{shapeFieldName}' has no compatible Packed BKD field coverage during merge.");
                if (!ctx.ShapeDocValuesFields.TryGetValue(shapeFieldName, out ShapeDocValuesFieldBuffer? shapeBuffer))
                {
                    shapeBuffer = new ShapeDocValuesFieldBuffer(shapeFieldName, shapeMetadata.Kind);
                    ctx.ShapeDocValuesFields.Add(shapeFieldName, shapeBuffer);
                }
                else if (shapeBuffer.Kind != shapeMetadata.Kind)
                {
                    throw new InvalidDataException($"Shape DocValues field '{shapeFieldName}' has incompatible coordinate systems during merge.");
                }
                shapeDocValuesFields.Add(shapeFieldName, shapeMetadata);
            }
            ctx.ShapeDocValuesMetadataBySegment.Add(segInfo.SegmentId, shapeDocValuesFields);
            if (packedFieldNames.Count > 0)
                reader.ValidatePackedBkdChecksum();
            foreach (var packedFieldName in packedFieldNames)
            {
                if (!reader.TryGetPackedBkdFieldMetadata(packedFieldName, out var metadata))
                    throw new InvalidDataException($"Packed BKD field '{packedFieldName}' disappeared during merge.");
                if (!ctx.PackedBkdFields.TryGetValue(packedFieldName, out var packedBuffer))
                {
                    packedBuffer = new PackedBkdFieldBuffer(metadata.Config);
                    ctx.PackedBkdFields.Add(packedFieldName, packedBuffer);
                }
                else if (packedBuffer.Config.Dimensions != metadata.Config.Dimensions
                    || packedBuffer.Config.IndexedDimensions != metadata.Config.IndexedDimensions
                    || packedBuffer.Config.BytesPerDimension != metadata.Config.BytesPerDimension)
                {
                    throw new InvalidDataException($"Packed BKD field '{packedFieldName}' has incompatible source dimensions during merge.");
                }

                var collector = new PackedBkdMergeVisitor(docIdMap, packedBuffer);
                reader.IntersectPackedBkd(packedFieldName, ref collector);
            }

            // Pre-build a name->VectorFieldInfo dictionary so the per-doc/per-field
            // loop body avoids an O(N) LINQ scan for each posting.
            Dictionary<string, VectorFieldInfo>? vectorFieldByName = null;
            if (reader.HasVectors)
            {
                vectorFieldByName = new Dictionary<string, VectorFieldInfo>(reader.Info.VectorFields.Count, StringComparer.Ordinal);
                foreach (var vf in reader.Info.VectorFields)
                    vectorFieldByName[vf.FieldName] = vf;
            }

            for (int oldDocId = 0; oldDocId < segInfo.DocCount; oldDocId++)
            {
                int remapDocId = docIdMap[oldDocId];
                if (remapDocId < 0) continue;

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

                foreach (var (field, values) in segInt64Index)
                {
                    if (!values.TryGetValue(oldDocId, out long intVal)) continue;
                    if (!ctx.Int64Fields.TryGetValue(field, out var fieldMap))
                    {
                        fieldMap = new Dictionary<int, long>();
                        ctx.Int64Fields[field] = fieldMap;
                    }
                    fieldMap[remapDocId] = intVal;
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

                foreach (var (field, arr) in segInt64Dvs.Values)
                {
                    if ((uint)oldDocId >= (uint)arr.Length) continue;
                    if (segInt64Dvs.Presence.TryGetValue(field, out var presenceBitmap) &&
                        presenceBitmap is not null && !presenceBitmap.Contains(oldDocId))
                        continue;
                    if (!ctx.Int64DocValues.TryGetValue(field, out var dst))
                    {
                        dst = new long[ctx.TotalDocs];
                        ctx.Int64DocValues[field] = dst;
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
                CopyMergedMultiValues(segInt64SortedDvs, ctx.Int64SortedDocValues, oldDocId, remapDocId, ctx.TotalDocs);
                CopyMergedMultiValues(segBinaryDvs, ctx.BinaryDocValues, oldDocId, remapDocId, ctx.TotalDocs);

                if (segParentBitSet is not null && segParentBitSet.IsParent(oldDocId))
                {
                    ctx.ParentBitSet ??= new ParentBitSet(ctx.TotalDocs);
                    ctx.ParentBitSet.Set(remapDocId);
                }

                if (reader.HasVectors)
                {
                    foreach (var vfName in reader.VectorFieldNames)
                    {
                        if (vectorFieldByName is null || !vectorFieldByName.TryGetValue(vfName, out var match))
                            throw new InvalidDataException($"Vector field '{vfName}' has no segment metadata during merge.");

                        if (!ctx.VectorFieldDocIds.TryGetValue(vfName, out var vectorDocIds))
                        {
                            vectorDocIds = new List<int>();
                            ctx.VectorFieldDocIds[vfName] = vectorDocIds;
                        }
                        vectorDocIds.Add(remapDocId);
                        if (!ctx.VectorFieldRemaps.TryGetValue(vfName, out var remapList))
                        {
                            remapList = new List<(SegmentInfo, Dictionary<int, int>, SegmentReader)>();
                            ctx.VectorFieldRemaps[vfName] = remapList;
                        }
                        var entry = remapList.FirstOrDefault(t => ReferenceEquals(t.Seg, segInfo));
                        if (entry.OldToNew is null)
                        {
                            entry = (segInfo, new Dictionary<int, int>(), reader);
                            remapList.Add(entry);
                        }
                        entry.OldToNew[oldDocId] = remapDocId;

                        ctx.VectorFieldHadHnsw[vfName] = ctx.VectorFieldHadHnsw.GetValueOrDefault(vfName, false) || match.HasHnsw;
                    }
                }
            }
        }

        for (int newDocId = 0; newDocId < documentOrder.Length; newDocId++)
        {
            MergeDocument document = documentOrder[newDocId];
            ctx.StoredWriter!.AddDocument(document.Reader.GetStoredFieldValues(document.OldDocId));
            if (ctx.TermVectorWriter is not null)
            {
                var termVectors = document.Reader.HasTermVectors
                    ? document.Reader.GetTermVectors(document.OldDocId)
                    : null;
                ctx.TermVectorWriter.AddDocument(termVectors);
            }

            if (!ctx.ShapeDocValuesMetadataBySegment.TryGetValue(
                    document.Segment.SegmentId,
                    out Dictionary<string, ShapeDocValuesFieldMetadata>? shapeFields))
                continue;

            foreach ((string shapeFieldName, ShapeDocValuesFieldMetadata _) in shapeFields)
            {
                if (!document.Reader.TryGetShapeDocValuesRecordMetadata(
                        shapeFieldName,
                        document.OldDocId,
                        out ShapeDocValuesRecordMetadata record))
                    continue;
                document.Reader.ValidateShapeDocValuesRecord(shapeFieldName, document.OldDocId);
                byte[] rawRecord = document.Reader.ReadShapeDocValuesRecordBytes(shapeFieldName, document.OldDocId);
                ctx.ShapeDocValuesFields[shapeFieldName].AppendRawRecord(
                    newDocId,
                    record.ValueCount,
                    record.PrimitiveCount,
                    rawRecord);
            }
        }
    }

    private static void WriteNorms(
        IReadOnlyList<(SegmentInfo Seg, int[] DocIdMap, SegmentReader Reader)> perSegmentMaps,
        IReadOnlyDictionary<string, SegmentReader> readers,
        IReadOnlyCollection<string> fieldNames,
        string basePath,
        int totalDocs)
    {
        var fieldNorms = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var fieldBoosts = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var fieldName in fieldNames)
        {
            var norms = new float[totalDocs];
            var boosts = new float[totalDocs];
            Array.Fill(boosts, 1.0f);
            foreach (var (segInfo, docIdMap, _) in perSegmentMaps)
            {
                var reader = readers[segInfo.SegmentId];
                for (int oldDocId = 0; oldDocId < segInfo.DocCount; oldDocId++)
                {
                    int newDocId = docIdMap[oldDocId];
                    if (newDocId < 0) continue;
                    norms[newDocId] = reader.GetNorm(oldDocId, fieldName);
                    boosts[newDocId] = reader.GetFieldBoost(oldDocId, fieldName);
                }
            }
            fieldNorms[fieldName] = norms;
            fieldBoosts[fieldName] = boosts;
        }
        NormsWriter.Write(basePath + ".nrm", fieldNorms, fieldBoosts);
    }

    private List<VectorFieldInfo> MergeVectors(
        MergeContext ctx,
        MergeDocument[] documentOrder,
        string basePath,
        IReadOnlyDictionary<string, VectorFieldContract> vectorContracts,
        VectorQuantisation destinationVectorQuantisation)
    {
        var merged = new List<VectorFieldInfo>();
        foreach (var (fieldName, vectorDocIds) in ctx.VectorFieldDocIds)
        {
            if (vectorDocIds.Count == 0) continue;
            if (!vectorContracts.TryGetValue(fieldName, out VectorFieldContract contract))
                throw new InvalidOperationException(
                    $"Cannot determine the vector contract for field '{fieldName}' during merge. Source segments must declare this field.");

            int dimension = contract.Dimension;
            bool normalised = contract.Normalised;
            VectorQuantisation quantisation = destinationVectorQuantisation;
            bool shouldBuildHnsw = ctx.VectorFieldHadHnsw.GetValueOrDefault(fieldName, false)
                && vectorDocIds.Count >= 2;
            string vecPath = Codecs.Vectors.VectorFilePaths.VectorFile(basePath, fieldName);
            var mergedSource = new MergedDocumentVectorSource(documentOrder, fieldName, dimension);
            bool hasHnsw = false;

            if (quantisation == VectorQuantisation.None)
            {
                VectorWriter.WriteField(vecPath, ctx.TotalDocs, dimension, mergedSource);
                if (shouldBuildHnsw)
                {
                    using var vectorReader = VectorReader.Open(vecPath);
                    hasHnsw = BuildAndWriteMergedHnsw(
                        fieldName,
                        basePath,
                        dimension,
                        normalised,
                        quantisation,
                        new VectorReaderSource(vectorReader),
                        vectorDocIds,
                        ctx.VectorFieldRemaps);
                }
            }
            else
            {
                var vqPath = Codecs.Vectors.VectorFilePaths.QuantisedVectorFile(basePath, fieldName);
                string vecFileName = Path.GetFileName(vecPath);
                try
                {
                    VectorWriter.WriteField(vecPath, ctx.TotalDocs, dimension, mergedSource);
                    using (var vectorReader = VectorReader.Open(vecPath))
                    {
                        var vectorSource = new VectorReaderSource(vectorReader);
                        switch (quantisation)
                        {
                            case VectorQuantisation.Int8:
                                QuantisedVectorWriter.WriteInt8(
                                    vqPath, ctx.TotalDocs, dimension, vectorSource, vectorDocIds);
                                break;
                            case VectorQuantisation.BBQ:
                                QuantisedVectorWriter.WriteBBQ(
                                    vqPath, ctx.TotalDocs, dimension, vectorSource, vectorDocIds);
                                break;
                            default:
                                throw new InvalidDataException(
                                    $"Unsupported vector quantisation '{quantisation}' during merge of field '{fieldName}'.");
                        }
                    }

                    if (shouldBuildHnsw)
                    {
                        using var quantisedReader = QuantisedVectorReader.Open(vqPath);
                        hasHnsw = BuildAndWriteMergedHnsw(
                            fieldName,
                            basePath,
                            dimension,
                            normalised,
                            quantisation,
                            new QuantisedVectorSource(quantisedReader),
                            vectorDocIds,
                            ctx.VectorFieldRemaps);
                    }
                }
                finally
                {
                    if (_directory.FileExists(vecFileName))
                        _directory.DeleteFile(vecFileName);
                }
            }

            merged.Add(new VectorFieldInfo
            {
                FieldName = fieldName,
                Dimension = dimension,
                Normalised = normalised,
                Quantisation = quantisation,
                HasHnsw = hasHnsw,
            });
        }
        return merged;
    }

    private bool BuildAndWriteMergedHnsw(
        string fieldName,
        string basePath,
        int dimension,
        bool normalised,
        VectorQuantisation destinationVectorQuantisation,
        IVectorSource vectorSource,
        IReadOnlyList<int> vectorDocIds,
        IReadOnlyDictionary<string, List<(SegmentInfo Seg, Dictionary<int, int> OldToNew, SegmentReader Reader)>> vectorFieldRemaps)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        HnswGraph? graph = null;
        try
        {
            if (vectorFieldRemaps.TryGetValue(fieldName, out var remapList) && remapList.Count > 0)
            {
                var seed = remapList
                    .Where(entry => entry.Seg.VectorFields.Any(field =>
                        field.FieldName == fieldName
                        && field.HasHnsw
                        && field.Dimension == dimension
                        && field.Normalised == normalised
                        && field.Quantisation == destinationVectorQuantisation))
                    .OrderByDescending(static entry => entry.OldToNew.Count)
                    .FirstOrDefault();

                if (seed.OldToNew is not null && seed.OldToNew.Count > 0)
                {
                    string seedHnswExtension = VectorFilePaths.HnswFile(string.Empty, fieldName);
                    if (seed.Reader.FileExists(seedHnswExtension))
                    {
                        try
                        {
                            graph = HnswReader.Read(
                                seed.Reader.OpenInput(seedHnswExtension), vectorSource, normalised, seed.OldToNew);
                            graph.Thaw();
                            foreach (int docId in vectorDocIds)
                                if (!graph.ContainsNode(docId)) graph.Insert(docId);
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException)
                        {
                            graph?.Dispose();
                            graph = null;
                            Diagnostics.LeanCorpusActivitySource.TraceSwallowed(
                                ex, $"HNSW seed read failed for '{fieldName}'; rebuilding graph from scratch");
                        }
                    }
                }
            }

            if (graph is null)
            {
                graph = HnswGraphBuilder.Build(vectorSource, vectorDocIds, _hnswBuildConfig);
            }
            else
            {
                graph.Freeze();
            }

            stopwatch.Stop();
            _metrics.RecordHnswBuild(stopwatch.Elapsed, vectorDocIds.Count);
            string hnswPath = VectorFilePaths.HnswFile(basePath, fieldName);
            HnswWriter.Write(hnswPath, graph, dimension, normalised);
            return true;
        }
        finally
        {
            graph?.Dispose();
        }
    }

    private sealed class MergedDocumentVectorSource : IVectorSource
    {
        private readonly MergeDocument[] _documents;
        private readonly string _fieldName;
        private float[]? _zeroVector;

        internal MergedDocumentVectorSource(MergeDocument[] documents, string fieldName, int dimension)
        {
            _documents = documents;
            _fieldName = fieldName;
            Dimension = dimension;
        }

        public int Dimension { get; }
        public int Count => _documents.Length;

        public ReadOnlySpan<float> GetVector(int docId)
        {
            MergeDocument document = GetDocument(docId);
            return document.Reader.GetVector(_fieldName, document.OldDocId) ?? (_zeroVector ??= new float[Dimension]);
        }

        public void CopyVectorTo(int docId, Span<float> destination)
        {
            if (destination.Length != Dimension)
                throw new ArgumentException($"Destination length {destination.Length} != vector dimension {Dimension}.", nameof(destination));
            MergeDocument document = GetDocument(docId);
            if (!document.Reader.TryCopyVectorTo(_fieldName, document.OldDocId, destination))
                destination.Clear();
        }

        private MergeDocument GetDocument(int docId)
        {
            if ((uint)docId >= (uint)_documents.Length)
                throw new ArgumentOutOfRangeException(nameof(docId));
            return _documents[docId];
        }
    }

    private static void WriteNumericFiles(MergeContext ctx, string basePath)
    {
        if (ctx.NumericFields.Count > 0)
            WriteNumericIndex(basePath + ".num", ctx.NumericFields);
        if (ctx.Int64Fields.Count > 0)
            WriteInt64Index(basePath + ".numl", ctx.Int64Fields);
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
            CodecFileWriter.WriteAtomically(basePath + ".dvn", DocValuesCodecFiles.Numeric, durable: false, bodyOutput =>
            {
                bodyOutput.WriteInt32(ctx.NumericDocValues.Count);
                string[] fieldKeys = System.Buffers.ArrayPool<string>.Shared.Rent(ctx.NumericDocValues.Count);
                try
                {
                    int kn = 0;
                    foreach (var key in ctx.NumericDocValues.Keys) fieldKeys[kn++] = key;
                    for (int i = 0; i < kn; i++)
                    {
                        var field = fieldKeys[i];
                        ctx.NumericFields.TryGetValue(field, out var sparseMap);
                        IReadOnlySet<int>? presenceSet = sparseMap is not null
                            ? (IReadOnlySet<int>)sparseMap.Keys.ToHashSet()
                            : null;
                        NumericDocValuesWriter.WriteFieldBlock(bodyOutput, field, ctx.NumericDocValues[field], ctx.TotalDocs, presenceSet);
                        ctx.NumericDocValues.Remove(field);
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<string>.Shared.Return(fieldKeys, clearArray: true);
                }
            });
        }
        if (ctx.Int64DocValues.Count > 0)
        {
            var int64Presence = new Dictionary<string, IReadOnlySet<int>>(ctx.Int64DocValues.Count, StringComparer.Ordinal);
            foreach (var field in ctx.Int64DocValues.Keys)
            {
                if (ctx.Int64Fields.TryGetValue(field, out var sparseMap))
                    int64Presence[field] = sparseMap.Keys.ToHashSet();
            }
            Int64DocValuesWriter.Write(basePath + ".dvnl", ctx.Int64DocValues, ctx.TotalDocs, int64Presence);
        }

        if (ctx.SortedDocValues.Count > 0)
        {
            CodecFileWriter.WriteAtomically(basePath + ".dvs", DocValuesCodecFiles.Sorted, durable: false, bodyOutput =>
            {
                bodyOutput.WriteInt32(ctx.SortedDocValues.Count);
                string[] fieldKeys = System.Buffers.ArrayPool<string>.Shared.Rent(ctx.SortedDocValues.Count);
                try
                {
                    int kn = 0;
                    foreach (var key in ctx.SortedDocValues.Keys) fieldKeys[kn++] = key;
                    for (int i = 0; i < kn; i++)
                    {
                        var field = fieldKeys[i];
                        SortedDocValuesWriter.WriteFieldBlock(bodyOutput, field, ctx.SortedDocValues[field], ctx.TotalDocs);
                        ctx.SortedDocValues.Remove(field);
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<string>.Shared.Return(fieldKeys, clearArray: true);
                }
            });
        }
        if (ctx.SortedSetDocValues.Count > 0)
            SortedSetDocValuesWriter.Write(basePath + ".dss", ctx.SortedSetDocValues, ctx.TotalDocs);
        if (ctx.SortedNumericDocValues.Count > 0)
            SortedNumericDocValuesWriter.Write(basePath + ".dsn", ctx.SortedNumericDocValues, ctx.TotalDocs);
        if (ctx.Int64SortedDocValues.Count > 0)
            Int64SortedNumericDocValuesWriter.Write(basePath + ".dsnl", ctx.Int64SortedDocValues, ctx.TotalDocs);
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
        if (ctx.NumericFields.Count > 0)
        {
            var bkdData = new Dictionary<string, List<(double Value, int DocId)>>(StringComparer.Ordinal);
            foreach (var (field, values) in ctx.NumericFields)
            {
                var points = new List<(double Value, int DocId)>(values.Count);
                foreach (var (docId, value) in values)
                    points.Add((value, docId));
                bkdData[field] = points;
            }
            BKDWriter.Write(basePath + ".bkd", bkdData);
        }

        if (ctx.Int64Fields.Count > 0)
        {
            var int64BkdData = new Dictionary<string, List<(long Value, int DocId)>>(StringComparer.Ordinal);
            foreach (var (field, values) in ctx.Int64Fields)
            {
                var points = new List<(long Value, int DocId)>(values.Count);
                foreach (var (docId, value) in values)
                    points.Add((value, docId));
                int64BkdData[field] = points;
            }
            Int64BKDWriter.Write(basePath + ".bkdl", int64BkdData);
        }
    }

    private static void WritePackedBkdTree(MergeContext ctx, string basePath)
    {
        if (ctx.PackedBkdFields.Count > 0)
            PackedBkdWriter.Write(
                basePath + ".pbkd",
                ctx.PackedBkdFields,
                PackedBkdBuildOptions.Default with { SpillDirectory = Path.GetDirectoryName(basePath) });
    }

    private static void WriteShapeDocValues(MergeContext ctx, string basePath)
    {
        if (ctx.ShapeDocValuesFields.Count > 0)
            ShapeDocValuesWriter.Write(basePath + ".dvg", ctx.TotalDocs, ctx.ShapeDocValuesFields);
    }

    private readonly struct PackedBkdMergeVisitor(int[] docIdMap, PackedBkdFieldBuffer destination) : IPackedBkdIntersectVisitor
    {
        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Crosses;

        public void Visit(int docId)
            => throw new InvalidDataException("Packed BKD merge expected value payloads for every point.");

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
        {
            if ((uint)docId >= (uint)docIdMap.Length)
                throw new InvalidDataException("Packed BKD merge encountered an out-of-range document ID.");
            int remapped = docIdMap[docId];
            if (remapped >= 0)
                destination.Append(packedValue, remapped);
        }
    }

    private static void WriteParentBitSet(MergeContext ctx, string basePath)
    {
        ctx.ParentBitSet?.WriteTo(basePath + ".pbs");
    }


    internal void CleanupSegmentFiles(SegmentInfo seg)
    {
        SegmentFileSet.Enumerate(_directory.DirectoryPath, seg.SegmentId, FileCatalog)
            .DeleteAllOwnedFiles(_directory, "merge segment file cleanup");
    }

    private static int GetSizeTier(int docCount)
    {
        if (docCount <= 0) return 0;
        return (int)Math.Log10(Math.Max(1, docCount));
    }

    private static void WriteNumericIndex(string filePath, Dictionary<string, Dictionary<int, double>> numericIndex)
        => NumericIndexCodec.WriteDouble(filePath, numericIndex);

    private static void WriteInt64Index(string filePath, Dictionary<string, Dictionary<int, long>> int64Index)
        => NumericIndexCodec.WriteInt64(filePath, int64Index);

    private static Dictionary<string, Dictionary<int, double>> ReadNumericIndex(SegmentReader reader)
    {
        var result = new Dictionary<string, Dictionary<int, double>>(StringComparer.Ordinal);
        if (!reader.FileExists(".num"))
            return result;

        return NumericIndexCodec.ReadDouble(reader.OpenInput(".num"));
    }

    private static Dictionary<string, Dictionary<int, long>> ReadInt64Index(SegmentReader reader)
    {
        var result = new Dictionary<string, Dictionary<int, long>>(StringComparer.Ordinal);
        if (!reader.FileExists(".numl"))
            return result;

        return NumericIndexCodec.ReadInt64(reader.OpenInput(".numl"));
    }

    private static (Dictionary<string, double[]> Values,
        Dictionary<string, Util.RoaringBitmap?> Presence) ReadNumericDocValues(SegmentReader reader)
        => reader.FileExists(".dvn")
            ? NumericDocValuesReader.Read(reader.OpenInput(".dvn"))
            : (new Dictionary<string, double[]>(StringComparer.Ordinal),
                new Dictionary<string, Util.RoaringBitmap?>(StringComparer.Ordinal));

    private static (Dictionary<string, long[]> Values,
        Dictionary<string, Util.RoaringBitmap?> Presence) ReadInt64DocValues(SegmentReader reader)
        => reader.FileExists(".dvnl")
            ? Int64DocValuesReader.Read(reader.OpenInput(".dvnl"))
            : (new Dictionary<string, long[]>(StringComparer.Ordinal),
                new Dictionary<string, Util.RoaringBitmap?>(StringComparer.Ordinal));

    private static (Dictionary<string, string[]> Values,
        Dictionary<string, Util.RoaringBitmap?> Presence) ReadSortedDocValues(SegmentReader reader)
        => reader.FileExists(".dvs")
            ? SortedDocValuesReader.Read(reader.OpenInput(".dvs"))
            : (new Dictionary<string, string[]>(StringComparer.Ordinal),
                new Dictionary<string, Util.RoaringBitmap?>(StringComparer.Ordinal));

    private static Dictionary<string, string[][]> ReadSortedSetDocValues(SegmentReader reader)
        => reader.FileExists(".dss")
            ? SortedSetDocValuesReader.Read(reader.OpenInput(".dss"))
            : new Dictionary<string, string[][]>(StringComparer.Ordinal);

    private static Dictionary<string, double[][]> ReadSortedNumericDocValues(SegmentReader reader)
        => reader.FileExists(".dsn")
            ? SortedNumericDocValuesReader.Read(reader.OpenInput(".dsn"))
            : new Dictionary<string, double[][]>(StringComparer.Ordinal);

    private static Dictionary<string, long[][]> ReadInt64SortedDocValues(SegmentReader reader)
        => reader.FileExists(".dsnl")
            ? Int64SortedNumericDocValuesReader.Read(reader.OpenInput(".dsnl"))
            : new Dictionary<string, long[][]>(StringComparer.Ordinal);

    private static Dictionary<string, byte[][][]> ReadBinaryDocValues(SegmentReader reader)
        => reader.FileExists(".dvb")
            ? BinaryDocValuesReader.Read(reader.OpenInput(".dvb"))
            : new Dictionary<string, byte[][][]>(StringComparer.Ordinal);

    /// <summary>
    /// Returns <c>true</c> if this segment contains soft-deleted documents that may still
    /// be within the retention window and should be preserved during a merge.
    /// </summary>
    private static bool ShouldRetainSoftDeletes(SegmentInfo segInfo)
        => segInfo.EarliestSoftDeleteTimestamp.HasValue;

    /// <summary>
    /// Merges segments from a foreign directory into a single new segment in the target directory.
    /// Used by <see cref="IndexWriter.AddIndexes"/>.
    /// </summary>
    public SegmentInfo? MergeSegmentsFromDirectory(
        MMapDirectory sourceDirectory,
        List<SegmentInfo> sourceSegments,
        ref int nextSegmentOrdinal,
        IndexWriterConfig config,
        int commitGeneration = 0)
    {
        List<SpatialFieldInfo> spatialFields = MergeSpatialFieldMetadata(sourceSegments);
        var newSegId = $"seg_{nextSegmentOrdinal++}";
        var basePath = Path.Combine(_directory.DirectoryPath, newSegId);

        var readers = new Dictionary<string, SegmentReader>(StringComparer.Ordinal);
        try
        {
            foreach (var segInfo in sourceSegments)
                readers[segInfo.SegmentId] = new SegmentReader(sourceDirectory, segInfo);

            return MergeSegmentsCore(sourceSegments, readers, newSegId, basePath, commitGeneration, spatialFields,
                config.VectorQuantisation);
        }
        finally
        {
            foreach (var r in readers.Values)
                r.Dispose();
        }
    }

    private static long? ComputeMergedMinSeqNo(List<SegmentInfo> segments)
    {
        long? min = null;
        foreach (var seg in segments)
        {
            if (seg.MinSequenceNumber.HasValue)
            {
                if (!min.HasValue || seg.MinSequenceNumber.Value < min.Value)
                    min = seg.MinSequenceNumber.Value;
            }
        }
        return min;
    }

    private static long? ComputeMergedMaxSeqNo(List<SegmentInfo> segments)
    {
        long? max = null;
        foreach (var seg in segments)
        {
            if (seg.MaxSequenceNumber.HasValue)
            {
                if (!max.HasValue || seg.MaxSequenceNumber.Value > max.Value)
                    max = seg.MaxSequenceNumber.Value;
            }
        }
        return max;
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try { FileOpenRetry.Delete(path); } catch (Exception ex) { Diagnostics.LeanCorpusActivitySource.TraceSwallowed(ex, "merge file delete"); }
    }

}
