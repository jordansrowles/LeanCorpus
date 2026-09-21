using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.PackedBkd;

/// <summary>Describes the relationship between a query cell and a packed BKD cell.</summary>
internal enum PackedBkdCellRelation : byte
{
    Outside,
    Inside,
    Crosses,
}

/// <summary>Receives packed BKD hits without allocating a point object for each value.</summary>
internal interface IPackedBkdIntersectVisitor
{
    PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum);
    void Visit(int docId);
    void Visit(int docId, ReadOnlySpan<byte> packedValue);
}

/// <summary>Reads the bounded tail directory and field sections of a packed BKD file.</summary>
internal sealed class PackedBkdReader : IDisposable
{
    private const int FooterLength = 16;
    private const uint FieldMagic = 0x3146_4250; // PBF1
    private const byte RawValues = 0;
    private const byte PrefixValues = 1;
    private const int MaximumTreeDepth = 64;
    private const int MaximumFieldNameBytes = 1 << 20;

    private readonly CodecReadSession _session;
    private readonly IndexInput _body;
    private readonly Dictionary<string, FieldDirectoryEntry> _directory;
    private readonly Dictionary<string, PackedBkdFieldMetadata> _metadata = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private bool _disposed;

    private PackedBkdReader(CodecReadSession session, IndexInput body, Dictionary<string, FieldDirectoryEntry> directory)
    {
        _session = session;
        _body = body;
        _directory = directory;
    }

    internal static PackedBkdReader Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return OpenCore(new IndexInput(filePath));
    }

    internal static PackedBkdReader Open(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return OpenCore(input);
    }

    internal IReadOnlyCollection<string> FieldNames => _directory.Keys;

    internal bool HasField(string fieldName) => _directory.ContainsKey(fieldName);

    internal PackedBkdFieldMetadata GetFieldMetadata(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (!_directory.ContainsKey(fieldName))
                throw new KeyNotFoundException($"Packed BKD field '{fieldName}' is not present.");
            return _metadata.TryGetValue(fieldName, out var metadata)
                ? metadata
                : (_metadata[fieldName] = ParseField(fieldName, _directory[fieldName]));
        }
    }

    /// <summary>Intersects one field with a visitor-defined packed query cell.</summary>
    internal bool Intersect(string fieldName, IPackedBkdIntersectVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ArgumentNullException.ThrowIfNull(visitor);
        ObjectDisposedException.ThrowIf(_disposed, this);
        PackedBkdFieldMetadata metadata;
        lock (_gate)
        {
            if (!_directory.TryGetValue(fieldName, out var entry))
                return false;

            metadata = _metadata.TryGetValue(fieldName, out var cached)
                ? cached
                : (_metadata[fieldName] = ParseField(fieldName, entry));
        }

        using var queryBody = _body.OpenSharedSlice(0, _body.Length);
        byte[] minimum = metadata.RootMinimum.ToArray();
        byte[] maximum = metadata.RootMaximum.ToArray();
        Traverse(queryBody, metadata, visitor, minimum, maximum, leavesOffset: 0, metadata.LeafCount, depth: 0);
        return true;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _body.Dispose();
        _session.Dispose();
    }

    private static PackedBkdReader OpenCore(IndexInput input)
    {
        CodecReadSession? session = null;
        IndexInput? body = null;
        try
        {
            session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true);
            session.ValidateChecksum();
            body = session.OpenBodyInput();
            var directory = ReadDirectory(body);
            return new PackedBkdReader(session, body, directory);
        }
        catch
        {
            body?.Dispose();
            session?.Dispose();
            if (session is null)
                input.Dispose();
            throw;
        }
    }

    private static Dictionary<string, FieldDirectoryEntry> ReadDirectory(IndexInput body)
    {
        if (body.Length < FooterLength)
            throw new InvalidDataException("Packed BKD body is shorter than its footer.");

        body.Seek(body.Length - FooterLength);
        uint magic = unchecked((uint)body.ReadInt32());
        if (magic != PackedBkdWriter.FooterMagic)
            throw new InvalidDataException("Packed BKD footer magic is invalid.");
        int footerFieldCount = body.ReadInt32();
        long directoryOffset = body.ReadInt64();
        if (footerFieldCount < 0 || directoryOffset < 0 || directoryOffset > body.Length - FooterLength)
            throw new InvalidDataException("Packed BKD footer metadata is outside the body.");

        body.Seek(directoryOffset);
        int fieldCount = ReadInt32(body, body.Length - FooterLength);
        if (fieldCount != footerFieldCount || fieldCount < 0 || fieldCount > (body.Length - body.Position) / 17)
            throw new InvalidDataException("Packed BKD directory field count is invalid.");

        var result = new Dictionary<string, FieldDirectoryEntry>(fieldCount, StringComparer.Ordinal);
        string? previous = null;
        for (int i = 0; i < fieldCount; i++)
        {
            string name = ReadDirectoryString(body, body.Length - FooterLength);
            if (name.Length == 0 || (previous is not null && PackedBkdFieldNameComparer.Instance.Compare(previous, name) >= 0))
                throw new InvalidDataException("Packed BKD directory field names must be non-empty and strictly sorted.");
            long offset = ReadInt64(body, body.Length - FooterLength);
            long length = ReadInt64(body, body.Length - FooterLength);
            if (offset < 0 || length <= 0 || offset > directoryOffset || length > directoryOffset - offset)
                throw new InvalidDataException($"Packed BKD field '{name}' points outside its section area.");
            result.Add(name, new FieldDirectoryEntry(offset, length));
            previous = name;
        }

        if (body.Position != body.Length - FooterLength)
            throw new InvalidDataException("Packed BKD directory has trailing bytes.");

        var sections = result.Values.OrderBy(static entry => entry.Offset).ToArray();
        for (int i = 1; i < sections.Length; i++)
        {
            if (checked(sections[i - 1].Offset + sections[i - 1].Length) > sections[i].Offset)
                throw new InvalidDataException("Packed BKD field sections overlap.");
        }
        return result;
    }

    private PackedBkdFieldMetadata ParseField(string fieldName, FieldDirectoryEntry entry)
    {
        try
        {
            long sectionEnd = checked(entry.Offset + entry.Length);
            _body.Seek(entry.Offset);
            if (unchecked((uint)ReadInt32(_body, sectionEnd)) != FieldMagic)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has invalid section magic.");
            int dimensions = ReadByte(_body, sectionEnd);
            int indexedDimensions = ReadByte(_body, sectionEnd);
            int bytesPerDimension = ReadByte(_body, sectionEnd);
            if (ReadByte(_body, sectionEnd) != 0)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has unknown section flags.");
            int maxPointsPerLeaf = ReadUInt16(_body, sectionEnd);
            if (ReadUInt16(_body, sectionEnd) != 0)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has non-zero reserved metadata.");
            int leafCount = ReadInt32(_body, sectionEnd);
            long pointCount = ReadInt64(_body, sectionEnd);
            int documentCount = ReadInt32(_body, sectionEnd);
            int splitCount = ReadInt32(_body, sectionEnd);

            var config = new PackedBkdConfig(dimensions, indexedDimensions, bytesPerDimension, maxPointsPerLeaf);
            long maximumPointCount = checked((long)leafCount * maxPointsPerLeaf);
            if (leafCount <= 0
                || pointCount < leafCount
                || pointCount > maximumPointCount
                || pointCount > int.MaxValue
                || documentCount <= 0
                || documentCount > pointCount)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has invalid point or leaf counts.");
            if (splitCount != leafCount - 1)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has an inconsistent split count.");

            var rootMinimum = ReadBytes(_body, sectionEnd, config.IndexedBytesLength);
            var rootMaximum = ReadBytes(_body, sectionEnd, config.IndexedBytesLength);
            if (ComparePacked(rootMinimum, rootMaximum) > 0)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has inverted root bounds.");

            long splitBytes = checked((long)splitCount + (long)splitCount * config.BytesPerDimension);
            EnsureAvailable(_body, sectionEnd, splitBytes);
            var splitDimensions = ReadBytes(_body, sectionEnd, splitCount);
            var splitValues = ReadBytes(_body, sectionEnd, checked(splitCount * config.BytesPerDimension));

            long offsetBytesAvailable = sectionEnd - _body.Position;
            if (offsetBytesAvailable < 0 || offsetBytesAvailable / sizeof(long) < (long)leafCount + 1)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has too many leaves for its section.");
            var leafOffsets = new long[checked(leafCount + 1)];
            for (int i = 0; i < leafOffsets.Length; i++)
            {
                leafOffsets[i] = ReadInt64(_body, sectionEnd);
                if (leafOffsets[i] < 0 || leafOffsets[i] > entry.Length)
                    throw new InvalidDataException($"Packed BKD field '{fieldName}' has an out-of-range leaf offset.");
                if (i > 0 && leafOffsets[i] < leafOffsets[i - 1])
                    throw new InvalidDataException($"Packed BKD field '{fieldName}' has unsorted leaf offsets.");
            }

            long leafDataOffset = checked(_body.Position - entry.Offset);
            long leafDataLength = checked(entry.Length - leafDataOffset);
            if (leafOffsets[0] != 0 || leafOffsets[^1] != leafDataLength)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has invalid leaf data bounds.");

            var metadata = new PackedBkdFieldMetadata(
                config,
                pointCount,
                documentCount,
                leafCount,
                splitDimensions,
                splitValues,
                leafOffsets,
                leafDataOffset,
                rootMinimum,
                rootMaximum,
                entry.Offset,
                entry.Length);
            ValidateTree(fieldName, metadata);
            return metadata;
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException($"Packed BKD field '{fieldName}' is truncated.", ex);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new InvalidDataException($"Packed BKD field '{fieldName}' has invalid configuration.", ex);
        }
        catch (OverflowException ex)
        {
            throw new InvalidDataException($"Packed BKD field '{fieldName}' has overflowing metadata.", ex);
        }
    }

    private static byte[] ReadBytes(IndexInput input, long sectionEnd, int count)
    {
        if (count < 0)
            throw new InvalidDataException("Packed BKD metadata has a negative byte count.");
        var bytes = new byte[count];
        ReadBytes(input, sectionEnd, bytes);
        return bytes;
    }

    private static void ReadBytes(IndexInput input, long sectionEnd, Span<byte> destination)
    {
        if (input.Position > sectionEnd || destination.Length > sectionEnd - input.Position)
            throw new InvalidDataException("Packed BKD metadata exceeds its field section.");
        input.ReadBytes(destination);
    }

    private void ValidateTree(string fieldName, PackedBkdFieldMetadata metadata)
    {
        byte[] minimum = metadata.RootMinimum.ToArray();
        byte[] maximum = metadata.RootMaximum.ToArray();
        long pointCount = ValidateNode(fieldName, metadata, minimum, maximum, leavesOffset: 0, metadata.LeafCount, depth: 0);
        if (pointCount != metadata.PointCount)
            throw new InvalidDataException($"Packed BKD field '{fieldName}' tree metadata is inconsistent.");
    }

    private long ValidateNode(
        string fieldName,
        PackedBkdFieldMetadata metadata,
        byte[] minimum,
        byte[] maximum,
        int leavesOffset,
        int leafCount,
        int depth)
    {
        if (depth > MaximumTreeDepth)
            throw new InvalidDataException($"Packed BKD field '{fieldName}' exceeds the maximum tree depth.");
        if (leafCount == 1)
            return ValidateLeaf(fieldName, metadata, leavesOffset, minimum, maximum);

        int leftLeaves = PackedBkdWriterLeftLeafCount(leafCount);
        int rightLeavesOffset = checked(leavesOffset + leftLeaves);
        int split = checked(rightLeavesOffset - 1);
        if ((uint)split >= (uint)metadata.SplitCount)
            throw new InvalidDataException($"Packed BKD field '{fieldName}' is missing a split record.");
        int dimension = metadata.SplitDimensions[split];
        if (dimension >= metadata.Config.IndexedDimensions)
            throw new InvalidDataException($"Packed BKD field '{fieldName}' has an invalid split dimension.");
        int offset = dimension * metadata.Config.BytesPerDimension;
        var splitValue = metadata.SplitValues.AsSpan(split * metadata.Config.BytesPerDimension, metadata.Config.BytesPerDimension);
        if (splitValue.SequenceCompareTo(minimum.AsSpan(offset, metadata.Config.BytesPerDimension)) < 0
            || splitValue.SequenceCompareTo(maximum.AsSpan(offset, metadata.Config.BytesPerDimension)) > 0)
            throw new InvalidDataException($"Packed BKD field '{fieldName}' has a split outside its cell bounds.");

        byte[] oldMaximumBytes = maximum.AsSpan(offset, metadata.Config.BytesPerDimension).ToArray();
        splitValue.CopyTo(maximum.AsSpan(offset, metadata.Config.BytesPerDimension));
        long points = ValidateNode(fieldName, metadata, minimum, maximum, leavesOffset, leftLeaves, depth + 1);
        oldMaximumBytes.CopyTo(maximum, offset);
        byte[] oldMinimumBytes = minimum.AsSpan(offset, metadata.Config.BytesPerDimension).ToArray();
        splitValue.CopyTo(minimum.AsSpan(offset, metadata.Config.BytesPerDimension));
        points += ValidateNode(fieldName, metadata, minimum, maximum, rightLeavesOffset, leafCount - leftLeaves, depth + 1);
        oldMinimumBytes.CopyTo(minimum, offset);
        return points;
    }

    private long ValidateLeaf(string fieldName, PackedBkdFieldMetadata metadata, int leafIndex, byte[] cellMinimum, byte[] cellMaximum)
    {
        if ((uint)leafIndex >= (uint)metadata.LeafCount)
            throw new InvalidDataException($"Packed BKD field '{fieldName}' has an invalid leaf index.");
        long start = checked(metadata.SectionOffset + metadata.LeafDataOffset + metadata.LeafOffsets[leafIndex]);
        long end = checked(metadata.SectionOffset + metadata.LeafDataOffset + metadata.LeafOffsets[leafIndex + 1]);
        _body.Seek(start);
        int count = ReadUInt16(_body, end);
        int docWidth = ReadByte(_body, end);
        byte encoding = checked((byte)ReadByte(_body, end));
        int minimumDocument = ReadInt32(_body, end);
        if (count <= 0 || docWidth is < 0 or > sizeof(int) || minimumDocument < 0 || encoding > PrefixValues)
            throw new InvalidDataException($"Packed BKD field '{fieldName}' has invalid leaf metadata.");
        var actualMinimum = ReadBytes(_body, end, metadata.Config.IndexedBytesLength);
        var actualMaximum = ReadBytes(_body, end, metadata.Config.IndexedBytesLength);
        if (ComparePacked(actualMinimum, actualMaximum) > 0
            || !Contains(cellMinimum, cellMaximum, actualMinimum)
            || !Contains(cellMinimum, cellMaximum, actualMaximum))
            throw new InvalidDataException($"Packed BKD field '{fieldName}' has invalid leaf bounds.");

        if (count > metadata.Config.MaxPointsPerLeaf)
            throw new InvalidDataException($"Packed BKD field '{fieldName}' has too many points in one leaf.");

        Span<byte> prefixes = stackalloc byte[PackedBkdConfig.MaxDimensions];
        Span<byte> commonPrefixes = stackalloc byte[PackedBkdConfig.MaxDimensions * PackedBkdConfig.FixedBytesPerDimension];
        if (encoding == PrefixValues)
        {
            for (int dimension = 0; dimension < metadata.Config.Dimensions; dimension++)
            {
                prefixes[dimension] = checked((byte)ReadByte(_body, end));
                if (prefixes[dimension] > metadata.Config.BytesPerDimension)
                    throw new InvalidDataException($"Packed BKD field '{fieldName}' has an invalid value prefix.");
            }
            for (int dimension = 0; dimension < metadata.Config.Dimensions; dimension++)
            {
                int length = prefixes[dimension];
                ReadBytes(_body, end, commonPrefixes.Slice(dimension * metadata.Config.BytesPerDimension, length));
            }
        }

        int[] documentIds = ArrayPool<int>.Shared.Rent(count);
        try
        {
            int actualMinimumDocument = int.MaxValue;
            int actualMaximumDocument = int.MinValue;
            for (int i = 0; i < count; i++)
            {
                uint delta = 0;
                for (int b = 0; b < docWidth; b++)
                    delta |= (uint)ReadByte(_body, end) << (8 * b);
                long document = (long)minimumDocument + delta;
                if (document > int.MaxValue)
                    throw new InvalidDataException($"Packed BKD field '{fieldName}' has an overflowing document ID.");
                documentIds[i] = (int)document;
                actualMinimumDocument = Math.Min(actualMinimumDocument, documentIds[i]);
                actualMaximumDocument = Math.Max(actualMaximumDocument, documentIds[i]);
            }
            if (minimumDocument != actualMinimumDocument || docWidth != DocumentWidth(actualMinimumDocument, actualMaximumDocument))
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has a non-minimal document encoding.");

            Span<byte> packed = stackalloc byte[PackedBkdConfig.MaxDimensions * PackedBkdConfig.FixedBytesPerDimension];
            Span<byte> previous = stackalloc byte[PackedBkdConfig.MaxDimensions * PackedBkdConfig.FixedBytesPerDimension];
            Span<byte> observedMinimum = stackalloc byte[PackedBkdConfig.MaxIndexedDimensions * PackedBkdConfig.FixedBytesPerDimension];
            Span<byte> observedMaximum = stackalloc byte[PackedBkdConfig.MaxIndexedDimensions * PackedBkdConfig.FixedBytesPerDimension];
            bool first = true;
            int previousDocument = 0;
            for (int i = 0; i < count; i++)
            {
                if (encoding == RawValues)
                {
                    ReadBytes(_body, end, packed[..metadata.Config.PackedBytesLength]);
                }
                else
                {
                    for (int dimension = 0; dimension < metadata.Config.Dimensions; dimension++)
                    {
                        int offset = dimension * metadata.Config.BytesPerDimension;
                        int prefixLength = prefixes[dimension];
                        commonPrefixes.Slice(offset, prefixLength).CopyTo(packed.Slice(offset, prefixLength));
                        ReadBytes(_body, end, packed.Slice(offset + prefixLength, metadata.Config.BytesPerDimension - prefixLength));
                    }
                }

                if (!first)
                {
                    int comparison = packed[..metadata.Config.PackedBytesLength].SequenceCompareTo(previous[..metadata.Config.PackedBytesLength]);
                    if (comparison < 0 || (comparison == 0 && documentIds[i] < previousDocument))
                        throw new InvalidDataException($"Packed BKD field '{fieldName}' has an unordered leaf.");
                }
                packed[..metadata.Config.PackedBytesLength].CopyTo(previous);
                for (int dimension = 0; dimension < metadata.Config.IndexedDimensions; dimension++)
                {
                    int offset = dimension * metadata.Config.BytesPerDimension;
                    var value = packed.Slice(offset, metadata.Config.BytesPerDimension);
                    if (first || value.SequenceCompareTo(observedMinimum.Slice(offset, metadata.Config.BytesPerDimension)) < 0)
                        value.CopyTo(observedMinimum.Slice(offset, metadata.Config.BytesPerDimension));
                    if (first || value.SequenceCompareTo(observedMaximum.Slice(offset, metadata.Config.BytesPerDimension)) > 0)
                        value.CopyTo(observedMaximum.Slice(offset, metadata.Config.BytesPerDimension));
                }
                previousDocument = documentIds[i];
                first = false;
            }

            if (_body.Position != end)
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has an invalid leaf payload length.");
            if (!observedMinimum[..metadata.Config.IndexedBytesLength].SequenceEqual(actualMinimum)
                || !observedMaximum[..metadata.Config.IndexedBytesLength].SequenceEqual(actualMaximum))
                throw new InvalidDataException($"Packed BKD field '{fieldName}' has bounds inconsistent with its leaf values.");
        }
        finally
        {
            ArrayPool<int>.Shared.Return(documentIds, clearArray: false);
        }
        return count;
    }

    private void Traverse(
        IndexInput input,
        PackedBkdFieldMetadata metadata,
        IPackedBkdIntersectVisitor visitor,
        byte[] minimum,
        byte[] maximum,
        int leavesOffset,
        int leafCount,
        int depth)
    {
        if (depth > MaximumTreeDepth)
            throw new InvalidDataException("Packed BKD traversal exceeded the maximum tree depth.");
        PackedBkdCellRelation relation = visitor.Compare(minimum, maximum);
        if (relation == PackedBkdCellRelation.Outside)
            return;

        if (leafCount == 1)
        {
            VisitLeaf(input, metadata, visitor, leavesOffset, relation);
            return;
        }

        int leftLeaves = PackedBkdWriterLeftLeafCount(leafCount);
        int rightLeavesOffset = checked(leavesOffset + leftLeaves);
        int split = checked(rightLeavesOffset - 1);
        int dimension = metadata.SplitDimensions[split];
        int offset = dimension * metadata.Config.BytesPerDimension;
        var splitValue = metadata.SplitValues.AsSpan(split * metadata.Config.BytesPerDimension, metadata.Config.BytesPerDimension);
        Span<byte> oldMaximum = stackalloc byte[PackedBkdConfig.FixedBytesPerDimension];
        maximum.AsSpan(offset, metadata.Config.BytesPerDimension).CopyTo(oldMaximum);
        splitValue.CopyTo(maximum.AsSpan(offset, metadata.Config.BytesPerDimension));
        Traverse(input, metadata, visitor, minimum, maximum, leavesOffset, leftLeaves, depth + 1);
        oldMaximum[..metadata.Config.BytesPerDimension].CopyTo(maximum.AsSpan(offset, metadata.Config.BytesPerDimension));

        Span<byte> oldMinimum = stackalloc byte[PackedBkdConfig.FixedBytesPerDimension];
        minimum.AsSpan(offset, metadata.Config.BytesPerDimension).CopyTo(oldMinimum);
        splitValue.CopyTo(minimum.AsSpan(offset, metadata.Config.BytesPerDimension));
        Traverse(input, metadata, visitor, minimum, maximum, rightLeavesOffset, leafCount - leftLeaves, depth + 1);
        oldMinimum[..metadata.Config.BytesPerDimension].CopyTo(minimum.AsSpan(offset, metadata.Config.BytesPerDimension));
    }

    private void VisitLeaf(IndexInput input, PackedBkdFieldMetadata metadata, IPackedBkdIntersectVisitor visitor, int leafIndex, PackedBkdCellRelation cellRelation)
    {
        long start = checked(metadata.SectionOffset + metadata.LeafDataOffset + metadata.LeafOffsets[leafIndex]);
        long end = checked(metadata.SectionOffset + metadata.LeafDataOffset + metadata.LeafOffsets[leafIndex + 1]);
        input.Seek(start);
        int count = ReadUInt16(input, end);
        int docWidth = ReadByte(input, end);
        byte encoding = checked((byte)ReadByte(input, end));
        int minimumDocument = ReadInt32(input, end);
        Span<byte> actualMinimum = stackalloc byte[PackedBkdConfig.MaxIndexedDimensions * PackedBkdConfig.FixedBytesPerDimension];
        Span<byte> actualMaximum = stackalloc byte[PackedBkdConfig.MaxIndexedDimensions * PackedBkdConfig.FixedBytesPerDimension];
        ReadBytes(input, end, actualMinimum[..metadata.Config.IndexedBytesLength]);
        ReadBytes(input, end, actualMaximum[..metadata.Config.IndexedBytesLength]);
        PackedBkdCellRelation relation = cellRelation == PackedBkdCellRelation.Inside
            ? PackedBkdCellRelation.Inside
            : visitor.Compare(actualMinimum[..metadata.Config.IndexedBytesLength], actualMaximum[..metadata.Config.IndexedBytesLength]);
        Span<byte> prefixes = stackalloc byte[PackedBkdConfig.MaxDimensions];
        Span<byte> commonPrefixes = stackalloc byte[PackedBkdConfig.MaxDimensions * PackedBkdConfig.FixedBytesPerDimension];
        int prefixBytes = 0;
        if (encoding == PrefixValues)
        {
            for (int dimension = 0; dimension < metadata.Config.Dimensions; dimension++)
            {
                prefixes[dimension] = checked((byte)ReadByte(input, end));
                prefixBytes += prefixes[dimension];
            }
            for (int dimension = 0; dimension < metadata.Config.Dimensions; dimension++)
            {
                int length = prefixes[dimension];
                ReadBytes(input, end, commonPrefixes.Slice(dimension * metadata.Config.BytesPerDimension, length));
            }
        }

        int[] documentIds = ArrayPool<int>.Shared.Rent(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                uint delta = 0;
                for (int b = 0; b < docWidth; b++)
                    delta |= (uint)ReadByte(input, end) << (8 * b);
                long document = (long)minimumDocument + delta;
                if (document > int.MaxValue)
                    throw new InvalidDataException("Packed BKD leaf document ID overflows Int32.");
                documentIds[i] = (int)document;
            }

            if (relation == PackedBkdCellRelation.Outside)
            {
                input.Seek(end);
                return;
            }
            if (relation == PackedBkdCellRelation.Inside)
            {
                for (int i = 0; i < count; i++) visitor.Visit(documentIds[i]);
                input.Seek(end);
                return;
            }

            byte[] packed = ArrayPool<byte>.Shared.Rent(metadata.Config.PackedBytesLength);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (encoding == RawValues)
                        ReadBytes(input, end, packed.AsSpan(0, metadata.Config.PackedBytesLength));
                    else
                    {
                        for (int dimension = 0; dimension < metadata.Config.Dimensions; dimension++)
                        {
                            int offset = dimension * metadata.Config.BytesPerDimension;
                            int prefixLength = prefixes[dimension];
                            commonPrefixes.Slice(offset, prefixLength).CopyTo(packed.AsSpan(offset, prefixLength));
                            ReadBytes(input, end, packed.AsSpan(offset + prefixLength, metadata.Config.BytesPerDimension - prefixLength));
                        }
                    }
                    visitor.Visit(documentIds[i], packed.AsSpan(0, metadata.Config.PackedBytesLength));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(packed, clearArray: false);
            }
        }
        finally
        {
            ArrayPool<int>.Shared.Return(documentIds, clearArray: false);
        }

        if (input.Position != end)
            throw new InvalidDataException("Packed BKD leaf decoding did not consume its payload.");
        _ = prefixBytes;
    }

    private static bool Contains(byte[] minimum, byte[] maximum, byte[] value)
    {
        return ComparePacked(value, minimum) >= 0 && ComparePacked(value, maximum) <= 0;
    }

    private static int ComparePacked(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
        => left.SequenceCompareTo(right);

    private static int ReadByte(IndexInput input, long end)
    {
        if (input.Position >= end)
            throw new InvalidDataException("Packed BKD metadata exceeds its bounded section.");
        return input.ReadByte();
    }

    private static int ReadUInt16(IndexInput input, long end)
        => ReadByte(input, end) | (ReadByte(input, end) << 8);

    private static int ReadInt32(IndexInput input, long end)
    {
        EnsureAvailable(input, end, sizeof(int));
        return input.ReadInt32();
    }

    private static long ReadInt64(IndexInput input, long end)
    {
        EnsureAvailable(input, end, sizeof(long));
        return input.ReadInt64();
    }

    private static void EnsureAvailable(IndexInput input, long end, long count)
    {
        if (count < 0 || input.Position > end || count > end - input.Position)
            throw new InvalidDataException("Packed BKD metadata exceeds its bounded section.");
    }

    private static string ReadDirectoryString(IndexInput input, long end)
    {
        uint length = ReadVarUInt(input, end);
        if (length > MaximumFieldNameBytes || length > (ulong)Math.Max(0, end - input.Position))
            throw new InvalidDataException("Packed BKD directory field name is too long or truncated.");
        var bytes = new byte[(int)length];
        ReadBytes(input, end, bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private static uint ReadVarUInt(IndexInput input, long end)
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            int next = ReadByte(input, end);
            if (shift == 28 && (next & 0xF0) != 0)
                throw new InvalidDataException("Packed BKD directory field name length overflows UInt32.");
            value |= (uint)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
                return value;
        }
        throw new InvalidDataException("Packed BKD directory field name length has an invalid varint.");
    }

    private static int PackedBkdWriterLeftLeafCount(int leafCount)
    {
        int highestPower = 1;
        while ((highestPower << 1) <= leafCount)
            highestPower <<= 1;
        int baseLeft = highestPower >> 1;
        int extra = leafCount - highestPower;
        return extra < baseLeft ? baseLeft + extra : highestPower;
    }

    private static int DocumentWidth(int minimumDocument, int maximumDocument)
    {
        long range = (long)maximumDocument - minimumDocument;
        if (range == 0) return 0;
        if (range <= byte.MaxValue) return 1;
        if (range <= ushort.MaxValue) return 2;
        if (range <= 0x00ff_ffff) return 3;
        return sizeof(int);
    }

    private readonly record struct FieldDirectoryEntry(long Offset, long Length);
}

/// <summary>Validated metadata for one packed BKD field section.</summary>
internal sealed class PackedBkdFieldMetadata
{
    internal PackedBkdFieldMetadata(
        PackedBkdConfig config,
        long pointCount,
        int documentCount,
        int leafCount,
        byte[] splitDimensions,
        byte[] splitValues,
        long[] leafOffsets,
        long leafDataOffset,
        byte[] rootMinimum,
        byte[] rootMaximum,
        long sectionOffset,
        long sectionLength)
    {
        Config = config;
        PointCount = pointCount;
        DocumentCount = documentCount;
        LeafCount = leafCount;
        SplitDimensions = splitDimensions;
        SplitValues = splitValues;
        LeafOffsets = leafOffsets;
        LeafDataOffset = leafDataOffset;
        RootMinimum = rootMinimum;
        RootMaximum = rootMaximum;
        SectionOffset = sectionOffset;
        SectionLength = sectionLength;
    }

    internal PackedBkdConfig Config { get; }
    internal long PointCount { get; }
    internal int DocumentCount { get; }
    internal int LeafCount { get; }
    internal int SplitCount => SplitDimensions.Length;
    internal byte[] SplitDimensions { get; }
    internal byte[] SplitValues { get; }
    internal long[] LeafOffsets { get; }
    internal long LeafDataOffset { get; }
    internal byte[] RootMinimum { get; }
    internal byte[] RootMaximum { get; }
    internal long SectionOffset { get; }
    internal long SectionLength { get; }
}
