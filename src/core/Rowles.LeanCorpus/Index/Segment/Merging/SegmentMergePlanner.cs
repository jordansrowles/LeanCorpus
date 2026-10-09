using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Index.Indexer;

namespace Rowles.LeanCorpus.Index.Segment.Merging;

/// <summary>Owns pre-write merge compatibility and document ordering.</summary>
internal static class SegmentMergePlanner
{
    internal static SegmentMergePlan? Build(
        List<SegmentInfo> segments,
        IReadOnlyDictionary<string, SegmentReader> readers,
        string newSegId, string basePath, int commitGeneration,
        double softDeleteRetentionSeconds, VectorQuantisation destinationVectorQuantisation,
        List<SpatialFieldInfo> spatialFields)
    {
        long softDeleteCutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)(softDeleteRetentionSeconds * 1000);
        bool hasCompatibleIndexSort = TryGetCommonIndexSort(segments, out SortField[] sortFields);
        var sortedMaps = new List<SegmentMergeDocumentMap.Source>(segments.Count);
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
        List<SegmentMergeDocumentMap.Source> perSegmentMaps;
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

        var documentMap = new SegmentMergeDocumentMap(perSegmentMaps, totalDocs);

        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segInfo in segments)
            foreach (var field in segInfo.FieldNames)
                fieldNames.Add(field);

        ValidateSpatialPayloadMetadata(segments, readers);
        return new SegmentMergePlan(segments, readers, newSegId, basePath, commitGeneration,
            destinationVectorQuantisation, documentMap, retainedSoftDeletes, fieldNames,
            hasSortedOutputOrder && segments[0].IndexSortFields is { } sortMetadata ? [.. sortMetadata] : null,
            spatialFields, vectorContracts, ComputeMergedMinSeqNo(segments), ComputeMergedMaxSeqNo(segments));
    }

    internal static List<SpatialFieldInfo> ResolveSpatialFieldMetadata(
        List<SegmentInfo> segments,
        IReadOnlyDictionary<string, SegmentReader> readers)
    {
        var fields = new Dictionary<string, SpatialFieldKind>(StringComparer.Ordinal);
        string[] fieldNames = segments
            .SelectMany(static segment => segment.SpatialFields)
            .Select(static spatialField => spatialField.FieldName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static fieldName => fieldName, StringComparer.Ordinal)
            .ToArray();

        foreach (string fieldName in fieldNames)
        {
            SpatialFieldKind? mergedKind = null;
            string? firstSegmentId = null;
            foreach (SegmentInfo segment in segments)
            {
                segment.Validate();
                SegmentReader reader = readers[segment.SegmentId];
                SpatialPointFieldResolution resolution = SpatialPointFieldCompatibility.Resolve(reader, fieldName);
                if (resolution == SpatialPointFieldResolution.None)
                {
                    if (reader.TryGetPackedBkdFieldMetadata(fieldName, out PackedBkdFieldMetadata packedMetadata)
                        && SpatialPointFieldCompatibility.HasCompatiblePointLayout(packedMetadata))
                        throw new InvalidDataException(
                            $"Spatial field '{fieldName}' in segment '{segment.SegmentId}' has an unclassifiable metadata-less packed point field during merge.");

                    continue;
                }

                SpatialFieldKind sourceKind = resolution switch
                {
                    SpatialPointFieldResolution.GeoPoint or SpatialPointFieldResolution.LegacyGeo => SpatialFieldKind.GeoPoint,
                    SpatialPointFieldResolution.XYPoint => SpatialFieldKind.XYPoint,
                    SpatialPointFieldResolution.OtherSpatial => segment.SpatialFields
                        .First(spatialField => string.Equals(spatialField.FieldName, fieldName, StringComparison.Ordinal)).Kind,
                    _ => throw new InvalidOperationException($"Unexpected spatial field resolution '{resolution}'.")
                };

                if (mergedKind.HasValue && mergedKind.Value != sourceKind)
                    throw new InvalidDataException(
                        $"Spatial field '{fieldName}' has incompatible kinds '{mergedKind.Value}' in segment '{firstSegmentId}' and '{sourceKind}' in segment '{segment.SegmentId}' during merge.");

                mergedKind ??= sourceKind;
                firstSegmentId ??= segment.SegmentId;
            }

            if (mergedKind.HasValue)
                fields.Add(fieldName, mergedKind.Value);
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
            if (!IndexSort.TryParseSerialisedField(common[i], out SortField sortField)
                || sortField.Type is not (SortFieldType.DocId or SortFieldType.Numeric or SortFieldType.Int64 or SortFieldType.String))
                return false;

            // Legacy segments may persist DocId sort metadata, but the pre-flush key is not
            // retained after physical reordering. Keep it readable in SegmentInfo while
            // refusing to reuse it as a merge key.
            if (sortField.Type == SortFieldType.DocId)
                return false;
            parsed[i] = sortField;
        }

        sortFields = parsed;
        return true;
    }

    private static bool TryBuildSortedDocumentMaps(
        List<SegmentInfo> segments,
        IReadOnlyDictionary<string, SegmentReader> readers,
        SortField[] sortFields,
        long softDeleteCutoff,
        out List<SegmentMergeDocumentMap.Source> perSegmentMaps,
        out int totalDocs,
        out List<(int DocId, long Timestamp)> retainedSoftDeletes)
    {
        perSegmentMaps = new List<SegmentMergeDocumentMap.Source>(segments.Count);
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
            perSegmentMaps.Add(new SegmentMergeDocumentMap.Source(segment, docIdMap, reader));
            List<DocIdRange> droppedBlockRanges = BuildDroppedBlockRanges(
                reader, segment, softDeleteCutoff);

            var cursor = new MergeSortCursor(
                segment,
                reader,
                docIdMap,
                sortFields,
                i,
                ShouldRetainSoftDeletes(segment),
                softDeleteCutoff,
                droppedBlockRanges);
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

    private static List<SegmentMergeDocumentMap.Source> BuildSequentialDocumentMaps(
        List<SegmentInfo> segments,
        IReadOnlyDictionary<string, SegmentReader> readers,
        long softDeleteCutoff,
        out int totalDocs,
        out List<(int DocId, long Timestamp)> retainedSoftDeletes)
    {
        var perSegmentMaps = new List<SegmentMergeDocumentMap.Source>(segments.Count);
        retainedSoftDeletes = [];
        totalDocs = 0;
        foreach (SegmentInfo segment in segments)
        {
            SegmentReader reader = readers[segment.SegmentId];
            var docIdMap = new int[segment.DocCount];
            bool retainSoftDeletes = ShouldRetainSoftDeletes(segment);
            List<DocIdRange> droppedBlockRanges = BuildDroppedBlockRanges(
                reader, segment, softDeleteCutoff);
            int droppedBlockRangeIndex = 0;
            for (int oldDocId = 0; oldDocId < segment.DocCount; oldDocId++)
            {
                while (droppedBlockRangeIndex < droppedBlockRanges.Count
                    && oldDocId > droppedBlockRanges[droppedBlockRangeIndex].End)
                    droppedBlockRangeIndex++;

                if (droppedBlockRangeIndex < droppedBlockRanges.Count
                    && oldDocId >= droppedBlockRanges[droppedBlockRangeIndex].Start)
                {
                    docIdMap[oldDocId] = -1;
                    continue;
                }

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

            perSegmentMaps.Add(new SegmentMergeDocumentMap.Source(segment, docIdMap, reader));
        }

        return perSegmentMaps;
    }

    private static List<DocIdRange> BuildDroppedBlockRanges(
        SegmentReader reader,
        SegmentInfo segment,
        long softDeleteCutoff)
    {
        ParentBitSet? parentBitSet = reader.GetParentBitSet();
        if (parentBitSet is null)
            return [];

        var droppedBlocks = new List<DocIdRange>();
        int blockStart = 0;
        for (int parentDocId = parentBitSet.NextParent(0);
             parentDocId >= 0;
             parentDocId = parentBitSet.NextParent(parentDocId + 1))
        {
            if (!IsRetainedDuringMerge(reader, segment, parentDocId, softDeleteCutoff))
                droppedBlocks.Add(new DocIdRange(blockStart, parentDocId));

            blockStart = parentDocId + 1;
        }

        return droppedBlocks;
    }

    private static bool IsRetainedDuringMerge(
        SegmentReader reader,
        SegmentInfo segment,
        int docId,
        long softDeleteCutoff)
    {
        if (reader.IsLive(docId))
            return true;

        return ShouldRetainSoftDeletes(segment)
            && reader.IsSoftDeleted(docId, out long timestamp)
            && timestamp > softDeleteCutoff;
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

        internal MergeSortValueResolver(SegmentReader reader, SortField field)
        {
            _reader = reader;
            _field = field;
        }

        internal MergeSortValue Read(int oldDocId, ISet<string> storedFieldFilter)
        {
            switch (_field.Type)
            {
                case SortFieldType.Numeric:
                    if (_reader.TryGetSortedNumericDocValues(_field.FieldName, oldDocId, out var sortedNumericValues)
                        && sortedNumericValues.Count > 0)
                        return MergeSortValue.Numeric(SegmentFlusher.SelectNumericValue(
                            sortedNumericValues, _field.Selector));
                    if (_reader.HasNumericDocValues(_field.FieldName))
                    {
                        _reader.TryGetNumericValue(_field.FieldName, oldDocId, out double docValue);
                        return MergeSortValue.Numeric(docValue);
                    }
                    if (_reader.TryGetNumericValue(_field.FieldName, oldDocId, out double numericValue))
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
                    if (_reader.TryGetSortedInt64DocValues(_field.FieldName, oldDocId, out var sortedInt64Values)
                        && sortedInt64Values.Count > 0)
                        return MergeSortValue.Int64(SegmentFlusher.SelectInt64Value(
                            sortedInt64Values, _field.Selector));
                    if (_reader.HasInt64DocValues(_field.FieldName))
                    {
                        _reader.TryGetInt64Value(_field.FieldName, oldDocId, out long docValue);
                        return MergeSortValue.Int64(docValue);
                    }
                    if (_reader.TryGetInt64Value(_field.FieldName, oldDocId, out long int64Value))
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
                    if (_reader.HasSortedDocValues(_field.FieldName))
                    {
                        return MergeSortValue.String(
                            _reader.TryGetSortedDocValue(_field.FieldName, oldDocId, out var value)
                                ? value
                                : null);
                    }
                    if (_reader.TryGetSortedSetDocValues(_field.FieldName, oldDocId, out var sortedSetValues)
                        && sortedSetValues.Count > 0)
                        return MergeSortValue.String(_field.Selector == SortValueSelector.Max
                            ? sortedSetValues[^1]
                            : sortedSetValues[0]);
                    if (_reader.TryGetBinaryDocValues(_field.FieldName, oldDocId, out var binaryValues)
                        && binaryValues.Count > 0)
                        return MergeSortValue.String(System.Text.Encoding.UTF8.GetString(binaryValues[0]));
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

    private readonly record struct DocIdRange(int Start, int End);

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
        private readonly IReadOnlyList<DocIdRange> _droppedBlockRanges;
        private readonly MergeSortValue[] _previousKey;
        private readonly MergeSortValueResolver[] _sortValueResolvers;
        private readonly HashSet<string>[] _storedFieldFilters;
        private bool _hasPreviousKey;
        private int _nextOldDocId;
        private int _droppedBlockRangeIndex;

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
            long softDeleteCutoff,
            IReadOnlyList<DocIdRange> droppedBlockRanges)
        {
            Segment = segment;
            Reader = reader;
            DocIdMap = docIdMap;
            SourceOrdinal = sourceOrdinal;
            _sortFields = sortFields;
            _retainSoftDeletes = retainSoftDeletes;
            _softDeleteCutoff = softDeleteCutoff;
            _droppedBlockRanges = droppedBlockRanges;
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
                while (_droppedBlockRangeIndex < _droppedBlockRanges.Count
                    && oldDocId > _droppedBlockRanges[_droppedBlockRangeIndex].End)
                    _droppedBlockRangeIndex++;

                if (_droppedBlockRangeIndex < _droppedBlockRanges.Count
                    && oldDocId >= _droppedBlockRanges[_droppedBlockRangeIndex].Start)
                    continue;

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

    private static bool ShouldRetainSoftDeletes(SegmentInfo segInfo)
        => segInfo.EarliestSoftDeleteTimestamp.HasValue;

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

    private static void ValidateSpatialPayloadMetadata(
        IReadOnlyList<SegmentInfo> segments, IReadOnlyDictionary<string, SegmentReader> readers)
    {
        var configs = new Dictionary<string, PackedBkdConfig>(StringComparer.Ordinal);
        var shapeKinds = new Dictionary<string, SpatialFieldKind>(StringComparer.Ordinal);
        foreach (SegmentInfo segment in segments)
        {
            SegmentReader reader = readers[segment.SegmentId];
            foreach (string field in reader.GetPackedBkdFieldNames())
            {
                if (!reader.TryGetPackedBkdFieldMetadata(field, out var metadata))
                    throw new InvalidDataException($"Packed BKD field '{field}' disappeared during merge.");
                if (configs.TryGetValue(field, out var previous)
                    && (previous.Dimensions != metadata.Config.Dimensions
                        || previous.IndexedDimensions != metadata.Config.IndexedDimensions
                        || previous.BytesPerDimension != metadata.Config.BytesPerDimension))
                    throw new InvalidDataException($"Packed BKD field '{field}' has incompatible source dimensions during merge.");
                configs.TryAdd(field, metadata.Config);
            }
            foreach (string field in reader.GetShapeDocValuesFieldNames())
            {
                if (!reader.TryGetShapeDocValuesFieldMetadata(field, out var metadata))
                    throw new InvalidDataException($"Shape DocValues field '{field}' disappeared during merge.");
                SpatialFieldInfo? kind = segment.SpatialFields.FirstOrDefault(value =>
                    string.Equals(value.FieldName, field, StringComparison.Ordinal));
                if (kind is null || kind.Kind != metadata.Kind)
                    throw new InvalidDataException($"Shape DocValues field '{field}' conflicts with segment spatial-field metadata.");
                if (!reader.TryGetPackedBkdFieldMetadata(field, out var packed)
                    || packed.DocumentCount < metadata.RecordCount)
                    throw new InvalidDataException($"Shape DocValues field '{field}' has no compatible Packed BKD field coverage during merge.");
                if (shapeKinds.TryGetValue(field, out var previous) && previous != metadata.Kind)
                    throw new InvalidDataException($"Shape DocValues field '{field}' has incompatible coordinate systems during merge.");
                shapeKinds.TryAdd(field, metadata.Kind);
            }
        }
    }
}
