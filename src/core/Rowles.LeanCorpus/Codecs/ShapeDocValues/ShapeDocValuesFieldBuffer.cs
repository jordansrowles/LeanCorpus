using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Spatial.Internal;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;

internal sealed class ShapeDocValuesFieldBuffer : IDisposable
{
    private readonly ShapePrimitiveByteBuffer _primitiveBytes = new();
    private readonly List<ShapeDocValuesRecord> _records = [];
    private List<byte[]>? _rawRecords;

    internal ShapeDocValuesFieldBuffer(string fieldName, SpatialFieldKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (kind is not (SpatialFieldKind.GeoShape or SpatialFieldKind.XYShape))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Shape DocValues require a GeoShape or XYShape field.");
        FieldName = fieldName;
        Kind = kind;
    }

    internal string FieldName { get; }
    internal SpatialFieldKind Kind { get; }
    internal IReadOnlyList<ShapeDocValuesRecord> Records => _records;
    internal bool UsesRawRecords => _rawRecords is not null;
    internal long AllocatedBytes => _primitiveBytes.AllocatedBytes
        + checked((long)_records.Capacity * 16)
        + (_rawRecords is null ? 0 : _rawRecords.Sum(static record => (long)record.Length));

    internal void AppendValue(int documentId, uint valueOrdinal, ReadOnlySpan<byte> packedPrimitives)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        if (_rawRecords is not null)
            throw new InvalidOperationException("Cannot append prepared primitives to a raw Shape DocValues merge buffer.");
        if (packedPrimitives.Length == 0 || packedPrimitives.Length % ShapePrimitiveCodec.PackedValueLength != 0)
            throw new ArgumentException("Shape DocValues primitives must be non-empty 28-byte values.", nameof(packedPrimitives));

        int recordIndex;
        if (_records.Count == 0 || _records[^1].DocumentId != documentId)
        {
            if (_records.Count > 0 && _records[^1].DocumentId >= documentId)
                throw new InvalidOperationException("Shape DocValues documents must be appended in strictly increasing order.");
            if (valueOrdinal != 0)
                throw new InvalidDataException("The first Shape DocValues value for a document must have ordinal zero.");
            recordIndex = _records.Count;
            _records.Add(new ShapeDocValuesRecord(documentId, _primitiveBytes.Length, 0, 0));
        }
        else
        {
            recordIndex = _records.Count - 1;
            if (_records[recordIndex].ValueCount != valueOrdinal)
                throw new InvalidDataException("Shape DocValues value ordinals are not contiguous within a document.");
        }

        ShapeDocValuesRecord record = _records[recordIndex];
        int primitiveCount = packedPrimitives.Length / ShapePrimitiveCodec.PackedValueLength;
        _primitiveBytes.AppendEncoded(packedPrimitives);
        _records[recordIndex] = record with
        {
            PrimitiveCount = checked(record.PrimitiveCount + primitiveCount),
            ValueCount = checked(record.ValueCount + 1),
        };
    }

    internal ReadOnlyMemory<byte> GetPrimitives(ShapeDocValuesRecord record)
    {
        if (_rawRecords is not null)
            throw new InvalidOperationException("A raw Shape DocValues merge buffer has no prepared primitive stream.");
        return _primitiveBytes.GetMemory(
            record.ByteOffset,
            checked(record.PrimitiveCount * ShapePrimitiveCodec.PackedValueLength));
    }

    internal ReadOnlyMemory<byte> GetRawRecord(int index)
    {
        if (_rawRecords is null || (uint)index >= (uint)_rawRecords.Count)
            throw new InvalidOperationException("Shape DocValues raw record is not available.");
        return _rawRecords[index];
    }

    internal void AppendRawRecord(
        int documentId,
        uint valueCount,
        uint primitiveCount,
        ReadOnlySpan<byte> rawRecord)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(documentId);
        if (valueCount == 0 || primitiveCount == 0 || rawRecord.Length < 72)
            throw new ArgumentException("A raw Shape DocValues record must contain positive counts and a complete header.", nameof(rawRecord));
        if (_primitiveBytes.Length != 0)
            throw new InvalidOperationException("Cannot append raw records to a prepared Shape DocValues field buffer.");

        _rawRecords ??= [];
        if (_records.Count > 0 && _records[^1].DocumentId >= documentId)
            throw new InvalidOperationException("Raw Shape DocValues records must be appended in increasing document order.");

        _records.Add(new ShapeDocValuesRecord(documentId, 0, checked((int)primitiveCount), valueCount));
        _rawRecords.Add(rawRecord.ToArray());
    }

    internal void RemapDocumentIds(ReadOnlySpan<int> oldToNew)
    {
        if (_rawRecords is not null)
        {
            var remapped = new List<(ShapeDocValuesRecord Record, byte[] Bytes)>(_records.Count);
            for (int i = 0; i < _records.Count; i++)
            {
                ShapeDocValuesRecord record = _records[i];
                if ((uint)record.DocumentId >= (uint)oldToNew.Length)
                    throw new InvalidDataException("A Shape DocValues document ID is outside the index-sort permutation.");
                remapped.Add((record with { DocumentId = oldToNew[record.DocumentId] }, _rawRecords[i]));
            }
            remapped.Sort(static (left, right) => left.Record.DocumentId.CompareTo(right.Record.DocumentId));
            _records.Clear();
            _rawRecords.Clear();
            foreach ((ShapeDocValuesRecord record, byte[] bytes) in remapped)
            {
                _records.Add(record);
                _rawRecords.Add(bytes);
            }
            for (int i = 1; i < _records.Count; i++)
                if (_records[i - 1].DocumentId >= _records[i].DocumentId)
                    throw new InvalidDataException("Index sorting produced duplicate Shape DocValues document IDs.");
            return;
        }

        for (int i = 0; i < _records.Count; i++)
        {
            ShapeDocValuesRecord record = _records[i];
            if ((uint)record.DocumentId >= (uint)oldToNew.Length)
                throw new InvalidDataException("A Shape DocValues document ID is outside the index-sort permutation.");
            _records[i] = record with { DocumentId = oldToNew[record.DocumentId] };
        }
        _records.Sort(static (left, right) => left.DocumentId.CompareTo(right.DocumentId));
        for (int i = 1; i < _records.Count; i++)
            if (_records[i - 1].DocumentId >= _records[i].DocumentId)
                throw new InvalidDataException("Index sorting produced duplicate Shape DocValues document IDs.");
    }

    public void Dispose()
    {
        _primitiveBytes.Dispose();
        _records.Clear();
        _rawRecords?.Clear();
    }
}
