using System.Buffers;
using System.Buffers.Binary;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.PackedBkd.Internal;

/// <summary>Owns one bounded, query-local Packed BKD traversal.</summary>
internal sealed class PackedBkdFieldCursor : IDisposable
{
    private readonly IndexInput _field;
    private readonly string _fieldName;
    private readonly PackedBkdFieldMetadata _metadata;
    private readonly int _maximumTreeDepth;
    private int[]? _documentScratch;
    private bool _disposed;

    internal PackedBkdFieldCursor(
        IndexInput body,
        string fieldName,
        PackedBkdFieldMetadata metadata,
        int maximumTreeDepth)
    {
        _field = body.OpenSharedSlice(metadata.SectionOffset, metadata.SectionLength);
        _fieldName = fieldName;
        _metadata = metadata;
        _maximumTreeDepth = maximumTreeDepth;
    }

    internal void Intersect<TVisitor>(ref TVisitor visitor, ref PackedBkdTraversalStats stats)
        where TVisitor : struct, IPackedBkdIntersectVisitor
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] minimum = _metadata.RootMinimum.ToArray();
        byte[] maximum = _metadata.RootMaximum.ToArray();
        Traverse(
            ref visitor,
            minimum,
            maximum,
            leavesOffset: 0,
            _metadata.LeafCount,
            depth: 0,
            ref stats);
    }

    internal void DeepValidate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] minimum = _metadata.RootMinimum.ToArray();
        byte[] maximum = _metadata.RootMaximum.ToArray();
        // Explicit offline validation tracks all document IDs; normal queries do not allocate this set.
        var documents = new HashSet<int>();
        long pointCount = ValidateNode(
            minimum,
            maximum,
            leavesOffset: 0,
            _metadata.LeafCount,
            depth: 0,
            documents);
        if (pointCount != _metadata.PointCount)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' tree metadata is inconsistent.");
        if (documents.Count != _metadata.DocumentCount)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' document count is inconsistent.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        var scratch = Interlocked.Exchange(ref _documentScratch, null);
        if (scratch is not null)
            ArrayPool<int>.Shared.Return(scratch, clearArray: false);
        _field.Dispose();
    }

    private void Traverse<TVisitor>(
        ref TVisitor visitor,
        byte[] minimum,
        byte[] maximum,
        int leavesOffset,
        int leafCount,
        int depth,
        ref PackedBkdTraversalStats stats)
        where TVisitor : struct, IPackedBkdIntersectVisitor
    {
        stats.CellsVisited++;
        if (depth > _maximumTreeDepth)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' exceeds the maximum tree depth.");

        PackedBkdCellRelation relation = visitor.Compare(minimum, maximum);
        if (relation == PackedBkdCellRelation.Outside)
        {
            stats.CellsPruned++;
            return;
        }

        if (leafCount == 1)
        {
            stats.LeavesVisited++;
            VisitLeaf(ref visitor, leavesOffset, minimum, maximum, ref stats);
            return;
        }

        int leftLeaves = PackedBkdTreeMath.GetLeftLeafCount(leafCount);
        int rightLeavesOffset = checked(leavesOffset + leftLeaves);
        int splitIndex = checked(rightLeavesOffset - 1);
        Span<byte> splitValue = stackalloc byte[PackedBkdConfig.FixedBytesPerDimension];
        ReadSplit(splitIndex, out int dimension, splitValue);
        ValidateSplit(dimension, splitValue, minimum, maximum);

        int offset = checked(dimension * _metadata.Config.BytesPerDimension);
        Span<byte> oldMaximum = stackalloc byte[PackedBkdConfig.FixedBytesPerDimension];
        maximum.AsSpan(offset, _metadata.Config.BytesPerDimension).CopyTo(oldMaximum);
        splitValue[.._metadata.Config.BytesPerDimension].CopyTo(maximum.AsSpan(offset, _metadata.Config.BytesPerDimension));
        ValidateCellBounds(minimum, maximum, "left child");
        Traverse(ref visitor, minimum, maximum, leavesOffset, leftLeaves, depth + 1, ref stats);
        oldMaximum[.._metadata.Config.BytesPerDimension].CopyTo(maximum.AsSpan(offset, _metadata.Config.BytesPerDimension));

        Span<byte> oldMinimum = stackalloc byte[PackedBkdConfig.FixedBytesPerDimension];
        minimum.AsSpan(offset, _metadata.Config.BytesPerDimension).CopyTo(oldMinimum);
        splitValue[.._metadata.Config.BytesPerDimension].CopyTo(minimum.AsSpan(offset, _metadata.Config.BytesPerDimension));
        ValidateCellBounds(minimum, maximum, "right child");
        Traverse(ref visitor, minimum, maximum, rightLeavesOffset, leafCount - leftLeaves, depth + 1, ref stats);
        oldMinimum[.._metadata.Config.BytesPerDimension].CopyTo(minimum.AsSpan(offset, _metadata.Config.BytesPerDimension));
    }

    private long ValidateNode(
        byte[] minimum,
        byte[] maximum,
        int leavesOffset,
        int leafCount,
        int depth,
        HashSet<int> documents)
    {
        if (depth > _maximumTreeDepth)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' exceeds the maximum tree depth.");
        ValidateCellBounds(minimum, maximum, "tree cell");
        if (leafCount == 1)
            return ValidateLeaf(leavesOffset, minimum, maximum, documents);

        int leftLeaves = PackedBkdTreeMath.GetLeftLeafCount(leafCount);
        int rightLeavesOffset = checked(leavesOffset + leftLeaves);
        int splitIndex = checked(rightLeavesOffset - 1);
        Span<byte> splitValue = stackalloc byte[PackedBkdConfig.FixedBytesPerDimension];
        ReadSplit(splitIndex, out int dimension, splitValue);
        ValidateSplit(dimension, splitValue, minimum, maximum);

        int offset = checked(dimension * _metadata.Config.BytesPerDimension);
        Span<byte> oldMaximum = stackalloc byte[PackedBkdConfig.FixedBytesPerDimension];
        maximum.AsSpan(offset, _metadata.Config.BytesPerDimension).CopyTo(oldMaximum);
        splitValue[.._metadata.Config.BytesPerDimension].CopyTo(maximum.AsSpan(offset, _metadata.Config.BytesPerDimension));
        long points = ValidateNode(minimum, maximum, leavesOffset, leftLeaves, depth + 1, documents);
        oldMaximum[.._metadata.Config.BytesPerDimension].CopyTo(maximum.AsSpan(offset, _metadata.Config.BytesPerDimension));

        Span<byte> oldMinimum = stackalloc byte[PackedBkdConfig.FixedBytesPerDimension];
        minimum.AsSpan(offset, _metadata.Config.BytesPerDimension).CopyTo(oldMinimum);
        splitValue[.._metadata.Config.BytesPerDimension].CopyTo(minimum.AsSpan(offset, _metadata.Config.BytesPerDimension));
        points += ValidateNode(minimum, maximum, rightLeavesOffset, leafCount - leftLeaves, depth + 1, documents);
        oldMinimum[.._metadata.Config.BytesPerDimension].CopyTo(minimum.AsSpan(offset, _metadata.Config.BytesPerDimension));
        return points;
    }

    private long ValidateLeaf(
        int leafIndex,
        byte[] cellMinimum,
        byte[] cellMaximum,
        HashSet<int> documents)
    {
        LeafLocation location = OpenLeaf(leafIndex);
        Span<byte> actualMinimum = stackalloc byte[PackedBkdConfig.MaxIndexedDimensions * PackedBkdConfig.FixedBytesPerDimension];
        Span<byte> actualMaximum = stackalloc byte[PackedBkdConfig.MaxIndexedDimensions * PackedBkdConfig.FixedBytesPerDimension];
        LeafHeader header = ReadLeafHeader(location, actualMinimum, actualMaximum);
        ValidateLeafBounds(cellMinimum, cellMaximum, actualMinimum, actualMaximum);
        Span<byte> prefixes = stackalloc byte[PackedBkdConfig.MaxDimensions];
        Span<byte> commonPrefixes = stackalloc byte[PackedBkdConfig.MaxDimensions * PackedBkdConfig.FixedBytesPerDimension];
        ReadPrefixData(location.End, header.Encoding, prefixes, commonPrefixes);
        EnsureDocumentScratch();

        int actualMinimumDocument = int.MaxValue;
        int actualMaximumDocument = int.MinValue;
        for (int i = 0; i < header.Count; i++)
        {
            int document = ReadDocument(location.End, header.MinimumDocument, header.DocumentWidth);
            _documentScratch![i] = document;
            actualMinimumDocument = Math.Min(actualMinimumDocument, document);
            actualMaximumDocument = Math.Max(actualMaximumDocument, document);
            documents.Add(document);
        }
        if (header.MinimumDocument != actualMinimumDocument
            || header.DocumentWidth != PackedBkdTreeMath.GetDocumentWidth(actualMinimumDocument, actualMaximumDocument))
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has a non-minimal document encoding.");

        Span<byte> packed = stackalloc byte[PackedBkdConfig.MaxDimensions * PackedBkdConfig.FixedBytesPerDimension];
        Span<byte> previous = stackalloc byte[PackedBkdConfig.MaxDimensions * PackedBkdConfig.FixedBytesPerDimension];
        Span<byte> observedMinimum = stackalloc byte[PackedBkdConfig.MaxIndexedDimensions * PackedBkdConfig.FixedBytesPerDimension];
        Span<byte> observedMaximum = stackalloc byte[PackedBkdConfig.MaxIndexedDimensions * PackedBkdConfig.FixedBytesPerDimension];
        bool first = true;
        int previousDocument = 0;
        for (int i = 0; i < header.Count; i++)
        {
            ReadPackedValue(location.End, header.Encoding, prefixes, commonPrefixes, packed);
            int comparison = packed[.._metadata.Config.PackedBytesLength]
                .SequenceCompareTo(previous[.._metadata.Config.PackedBytesLength]);
            if (!first && (comparison < 0 || (comparison == 0 && _documentScratch![i] < previousDocument)))
                throw new InvalidDataException($"Packed BKD field '{_fieldName}' has an unordered leaf.");
            packed[.._metadata.Config.PackedBytesLength].CopyTo(previous);
            for (int dimension = 0; dimension < _metadata.Config.IndexedDimensions; dimension++)
            {
                int offset = checked(dimension * _metadata.Config.BytesPerDimension);
                var value = packed.Slice(offset, _metadata.Config.BytesPerDimension);
                if (first || value.SequenceCompareTo(observedMinimum.Slice(offset, _metadata.Config.BytesPerDimension)) < 0)
                    value.CopyTo(observedMinimum.Slice(offset, _metadata.Config.BytesPerDimension));
                if (first || value.SequenceCompareTo(observedMaximum.Slice(offset, _metadata.Config.BytesPerDimension)) > 0)
                    value.CopyTo(observedMaximum.Slice(offset, _metadata.Config.BytesPerDimension));
            }
            previousDocument = _documentScratch![i];
            first = false;
        }

        if (_field.Position != location.End)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has an invalid leaf payload length.");
        if (!observedMinimum[.._metadata.Config.IndexedBytesLength].SequenceEqual(actualMinimum[.._metadata.Config.IndexedBytesLength])
            || !observedMaximum[.._metadata.Config.IndexedBytesLength].SequenceEqual(actualMaximum[.._metadata.Config.IndexedBytesLength]))
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has bounds inconsistent with its leaf values.");
        return header.Count;
    }

    private void VisitLeaf<TVisitor>(
        ref TVisitor visitor,
        int leafIndex,
        byte[] cellMinimum,
        byte[] cellMaximum,
        ref PackedBkdTraversalStats stats)
        where TVisitor : struct, IPackedBkdIntersectVisitor
    {
        LeafLocation location = OpenLeaf(leafIndex);
        Span<byte> actualMinimum = stackalloc byte[PackedBkdConfig.MaxIndexedDimensions * PackedBkdConfig.FixedBytesPerDimension];
        Span<byte> actualMaximum = stackalloc byte[PackedBkdConfig.MaxIndexedDimensions * PackedBkdConfig.FixedBytesPerDimension];
        LeafHeader header = ReadLeafHeader(location, actualMinimum, actualMaximum);
        ValidateLeafBounds(cellMinimum, cellMaximum, actualMinimum, actualMaximum);
        stats.LeavesSemanticallyValidated++;

        PackedBkdCellRelation relation = visitor.Compare(
            actualMinimum[.._metadata.Config.IndexedBytesLength],
            actualMaximum[.._metadata.Config.IndexedBytesLength]);
        if (relation == PackedBkdCellRelation.Outside)
            return;

        Span<byte> prefixes = stackalloc byte[PackedBkdConfig.MaxDimensions];
        Span<byte> commonPrefixes = stackalloc byte[PackedBkdConfig.MaxDimensions * PackedBkdConfig.FixedBytesPerDimension];
        ReadPrefixData(location.End, header.Encoding, prefixes, commonPrefixes);
        EnsureDocumentScratch();
        stats.PeakLeafScratch = Math.Max(stats.PeakLeafScratch, _documentScratch!.Length);
        for (int i = 0; i < header.Count; i++)
            _documentScratch[i] = ReadDocument(location.End, header.MinimumDocument, header.DocumentWidth);

        if (relation == PackedBkdCellRelation.Inside)
        {
            SkipValues(location.End, header.Count, header.Encoding, prefixes);
            for (int i = 0; i < header.Count; i++)
            {
                visitor.Visit(_documentScratch[i]);
                stats.DocumentsVisited++;
            }
        }
        else
        {
            Span<byte> packed = stackalloc byte[PackedBkdConfig.MaxDimensions * PackedBkdConfig.FixedBytesPerDimension];
            for (int i = 0; i < header.Count; i++)
            {
                ReadPackedValue(location.End, header.Encoding, prefixes, commonPrefixes, packed);
                visitor.Visit(_documentScratch[i], packed[.._metadata.Config.PackedBytesLength]);
                stats.PackedValuesDecoded++;
                stats.DocumentsVisited++;
            }
        }

        if (_field.Position != location.End)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' decoding did not consume its payload.");
    }

    private LeafLocation OpenLeaf(int leafIndex)
    {
        if ((uint)leafIndex >= (uint)_metadata.LeafCount)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has an invalid leaf index.");
        long startOffset = ReadLeafOffset(leafIndex);
        long endOffset = ReadLeafOffset(leafIndex + 1);
        if (endOffset < startOffset)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has unsorted leaf offsets.");
        long start = checked(_metadata.LeafDataOffset + startOffset);
        long end = checked(_metadata.LeafDataOffset + endOffset);
        if (start < _metadata.LeafDataOffset
            || end > _metadata.LeafDataOffset + _metadata.LeafDataLength
            || end <= start)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has an out-of-range leaf offset.");
        return new LeafLocation(start, end);
    }

    private long ReadLeafOffset(int index)
    {
        long position = checked(_metadata.LeafOffsetsOffset + (long)index * sizeof(long));
        EnsurePosition(position, sizeof(long));
        _field.Seek(position);
        long offset = _field.ReadInt64();
        if (offset < 0 || offset > _metadata.LeafDataLength)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has an out-of-range leaf offset.");
        return offset;
    }

    private LeafHeader ReadLeafHeader(LeafLocation location, Span<byte> actualMinimum, Span<byte> actualMaximum)
    {
        _field.Seek(location.Start);
        int count = ReadUInt16(location.End);
        int documentWidth = ReadByte(location.End);
        byte encoding = checked((byte)ReadByte(location.End));
        int minimumDocument = ReadInt32(location.End);
        if (count <= 0 || count > _metadata.Config.MaxPointsPerLeaf
            || documentWidth is < 0 or > sizeof(int)
            || minimumDocument < 0
            || encoding > PackedBkdFormat.PrefixValues)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has invalid leaf metadata.");

        ReadBytes(location.End, actualMinimum[.._metadata.Config.IndexedBytesLength]);
        ReadBytes(location.End, actualMaximum[.._metadata.Config.IndexedBytesLength]);
        PackedBkdReader.ValidateBounds(_fieldName, "leaf", actualMinimum, actualMaximum, _metadata.Config);
        return new LeafHeader(count, documentWidth, encoding, minimumDocument);
    }

    private void ReadSplit(int splitIndex, out int dimension, Span<byte> splitValue)
    {
        if ((uint)splitIndex >= (uint)_metadata.SplitCount)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' is missing a split record.");
        EnsurePosition(checked(_metadata.SplitDimensionsOffset + splitIndex), 1);
        _field.Seek(checked(_metadata.SplitDimensionsOffset + splitIndex));
        dimension = _field.ReadByte();
        if (dimension >= _metadata.Config.IndexedDimensions)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has an invalid split dimension.");

        long valuePosition = checked(_metadata.SplitValuesOffset + (long)splitIndex * _metadata.Config.BytesPerDimension);
        EnsurePosition(valuePosition, _metadata.Config.BytesPerDimension);
        _field.Seek(valuePosition);
        ReadBytes(checked(valuePosition + _metadata.Config.BytesPerDimension), splitValue[.._metadata.Config.BytesPerDimension]);
    }

    private void ValidateSplit(int dimension, ReadOnlySpan<byte> splitValue, byte[] minimum, byte[] maximum)
    {
        int offset = checked(dimension * _metadata.Config.BytesPerDimension);
        int comparisonMinimum = splitValue[.._metadata.Config.BytesPerDimension]
            .SequenceCompareTo(minimum.AsSpan(offset, _metadata.Config.BytesPerDimension));
        int comparisonMaximum = splitValue[.._metadata.Config.BytesPerDimension]
            .SequenceCompareTo(maximum.AsSpan(offset, _metadata.Config.BytesPerDimension));
        if (comparisonMinimum < 0 || comparisonMaximum > 0)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has a split outside its cell bounds in dimension {dimension}.");
    }

    private void ValidateCellBounds(byte[] minimum, byte[] maximum, string name)
        => PackedBkdReader.ValidateBounds(_fieldName, name, minimum, maximum, _metadata.Config);

    private void ValidateLeafBounds(
        byte[] cellMinimum,
        byte[] cellMaximum,
        ReadOnlySpan<byte> actualMinimum,
        ReadOnlySpan<byte> actualMaximum)
    {
        PackedBkdReader.ValidateBounds(_fieldName, "leaf", actualMinimum, actualMaximum, _metadata.Config);
        for (int dimension = 0; dimension < _metadata.Config.IndexedDimensions; dimension++)
        {
            if (PackedBkdReader.CompareDimension(actualMinimum, cellMinimum, dimension, _metadata.Config.BytesPerDimension) < 0
                || PackedBkdReader.CompareDimension(actualMaximum, cellMaximum, dimension, _metadata.Config.BytesPerDimension) > 0)
                throw new InvalidDataException($"Packed BKD field '{_fieldName}' has leaf bounds outside its cell in dimension {dimension}.");
        }
    }

    private void ReadPrefixData(long end, byte encoding, Span<byte> prefixes, Span<byte> commonPrefixes)
    {
        prefixes.Clear();
        commonPrefixes.Clear();
        if (encoding != PackedBkdFormat.PrefixValues)
            return;
        for (int dimension = 0; dimension < _metadata.Config.Dimensions; dimension++)
        {
            prefixes[dimension] = checked((byte)ReadByte(end));
            if (prefixes[dimension] > _metadata.Config.BytesPerDimension)
                throw new InvalidDataException($"Packed BKD field '{_fieldName}' has an invalid value prefix.");
        }
        for (int dimension = 0; dimension < _metadata.Config.Dimensions; dimension++)
        {
            int offset = checked(dimension * _metadata.Config.BytesPerDimension);
            ReadBytes(end, commonPrefixes.Slice(offset, prefixes[dimension]));
        }
    }

    private void ReadPackedValue(
        long end,
        byte encoding,
        ReadOnlySpan<byte> prefixes,
        ReadOnlySpan<byte> commonPrefixes,
        Span<byte> packed)
    {
        if (encoding == PackedBkdFormat.RawValues)
        {
            ReadBytes(end, packed[.._metadata.Config.PackedBytesLength]);
            return;
        }
        for (int dimension = 0; dimension < _metadata.Config.Dimensions; dimension++)
        {
            int offset = checked(dimension * _metadata.Config.BytesPerDimension);
            int prefixLength = prefixes[dimension];
            commonPrefixes.Slice(offset, prefixLength).CopyTo(packed.Slice(offset, prefixLength));
            ReadBytes(end, packed.Slice(offset + prefixLength, _metadata.Config.BytesPerDimension - prefixLength));
        }
    }

    private void SkipValues(long end, int count, byte encoding, ReadOnlySpan<byte> prefixes)
    {
        long prefixBytes = 0;
        if (encoding == PackedBkdFormat.PrefixValues)
        {
            for (int dimension = 0; dimension < _metadata.Config.Dimensions; dimension++)
                prefixBytes += prefixes[dimension];
        }
        long bytesPerValue = encoding == PackedBkdFormat.RawValues
            ? _metadata.Config.PackedBytesLength
            : _metadata.Config.PackedBytesLength - prefixBytes;
        long bytes = checked(bytesPerValue * count);
        EnsurePosition(_field.Position, bytes);
        _field.Seek(checked(_field.Position + bytes));
        if (_field.Position > end)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has a truncated leaf payload.");
    }

    private int ReadDocument(long end, int minimumDocument, int documentWidth)
    {
        uint delta = 0;
        for (int b = 0; b < documentWidth; b++)
            delta |= (uint)ReadByte(end) << (8 * b);
        long document = (long)minimumDocument + delta;
        if (document > int.MaxValue)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has an overflowing document ID.");
        return (int)document;
    }

    private void EnsureDocumentScratch()
    {
        if (_documentScratch is not null)
            return;
        _documentScratch = ArrayPool<int>.Shared.Rent(_metadata.Config.MaxPointsPerLeaf);
    }

    private int ReadByte(long end)
    {
        if (_field.Position >= end)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' exceeds its bounded leaf.");
        return _field.ReadByte();
    }

    private int ReadUInt16(long end)
        => ReadByte(end) | (ReadByte(end) << 8);

    private int ReadInt32(long end)
    {
        EnsurePosition(_field.Position, sizeof(int), end);
        return _field.ReadInt32();
    }

    private void ReadBytes(long end, Span<byte> destination)
    {
        EnsurePosition(_field.Position, destination.Length, end);
        _field.ReadBytes(destination);
    }

    private void EnsurePosition(long position, long count, long? end = null)
    {
        long limit = end ?? _field.Length;
        if (position < 0 || count < 0 || position > limit || count > limit - position)
            throw new InvalidDataException($"Packed BKD field '{_fieldName}' has an out-of-range bounded read.");
    }

    private readonly record struct LeafLocation(long Start, long End);

    private readonly record struct LeafHeader(
        int Count,
        int DocumentWidth,
        byte Encoding,
        int MinimumDocument);
}
