using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;

/// <summary>Reads bounded Shape DocValues metadata and component trees from a mapped codec body.</summary>
internal sealed class ShapeDocValuesReader : IDisposable
{
    private const int TopFooterLength = 16;
    private const int FieldHeaderLength = 16;
    private const int FieldFooterLength = 16;
    private const int RecordHeaderLength = 72;
    private const int RecordDirectoryEntryLength = 16;
    private const int NodeHeaderLength = 28;
    private const int MaximumTreeDepth = 64;
    private const int MaximumFieldNameBytes = 1 << 20;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly CodecReadSession _session;
    private readonly IndexInput _body;
    private readonly Dictionary<string, FieldDirectoryEntry> _directory;
    private readonly Dictionary<string, ShapeDocValuesFieldMetadata> _metadata = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly long _topDirectoryOffset;
    private bool _disposed;

    private ShapeDocValuesReader(
        CodecReadSession session,
        IndexInput body,
        Dictionary<string, FieldDirectoryEntry> directory,
        long topDirectoryOffset)
    {
        _session = session;
        _body = body;
        _directory = directory;
        _topDirectoryOffset = topDirectoryOffset;
    }

    internal IReadOnlyCollection<string> FieldNames => _directory.Keys;

    internal static ShapeDocValuesReader Open(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return OpenCore(new IndexInput(filePath));
    }

    internal static ShapeDocValuesReader Open(IndexInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return OpenCore(input);
    }

    internal bool HasField(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _directory.ContainsKey(fieldName);
    }

    internal ShapeDocValuesFieldMetadata GetFieldMetadata(string fieldName)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_gate)
        {
            if (!_directory.TryGetValue(fieldName, out FieldDirectoryEntry entry))
                throw new KeyNotFoundException($"Shape DocValues field '{fieldName}' is not present.");
            return _metadata.TryGetValue(fieldName, out ShapeDocValuesFieldMetadata metadata)
                ? metadata
                : (_metadata[fieldName] = ParseField(fieldName, entry));
        }
    }

    internal bool TryGetRecordMetadata(
        string fieldName,
        int documentId,
        out ShapeDocValuesRecordMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(fieldName);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ShapeDocValuesFieldMetadata field = GetFieldMetadata(fieldName);
        if ((uint)documentId >= (uint)field.MaxDoc || field.RecordCount == 0)
        {
            metadata = default;
            return false;
        }

        using IndexInput section = OpenFieldInput(field);
        int low = 0;
        int high = field.RecordCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            RecordDirectoryEntry entry = ReadRecordDirectoryEntry(section, field, middle);
            if (entry.DocumentId == (uint)documentId)
            {
                metadata = ReadRecordMetadata(section, field, entry);
                return true;
            }
            if (entry.DocumentId < (uint)documentId)
                low = middle + 1;
            else
                high = middle - 1;
        }

        metadata = default;
        return false;
    }

    /// <summary>Visits one record's decoded primitives without retaining a node graph.</summary>
    internal int VisitPrimitives(string fieldName, int documentId, Action<ShapePrimitive> visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        if (!TryGetRecordMetadata(fieldName, documentId, out ShapeDocValuesRecordMetadata record))
            return 0;

        ShapeDocValuesFieldMetadata field = GetFieldMetadata(fieldName);
        using IndexInput section = OpenFieldInput(field);
        long treeStart = checked(record.RecordOffset + RecordHeaderLength);
        long treeEnd = checked(treeStart + record.TreeLength);
        NodeValidationSummary summary = TraverseNode(
            section,
            treeStart,
            treeEnd,
            depth: 0,
            field.Kind,
            record.ValueCount,
            visitor);
        if (summary.NodeLength != record.TreeLength || summary.PrimitiveCount != record.PrimitiveCount)
            throw new InvalidDataException("Shape DocValues component tree length or primitive count does not match its record header.");
        return summary.PrimitiveCount;
    }

    /// <summary>Verifies the checksum and every field record and component tree.</summary>
    internal void DeepValidate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            _session.ValidateChecksum();
        }
        catch (CodecFileException exception)
        {
            throw new InvalidDataException("Shape DocValues checksum validation failed.", exception);
        }

        ValidateCompleteSectionLayout();
        foreach ((string name, FieldDirectoryEntry entry) in _directory)
        {
            ShapeDocValuesFieldMetadata field;
            lock (_gate)
            {
                field = _metadata.TryGetValue(name, out ShapeDocValuesFieldMetadata cached)
                    ? cached
                    : (_metadata[name] = ParseField(name, entry));
            }

            using IndexInput section = OpenFieldInput(field);
            for (int i = 0; i < field.RecordCount; i++)
            {
                RecordDirectoryEntry directoryEntry = ReadRecordDirectoryEntry(section, field, i);
                ShapeDocValuesRecordMetadata record = ReadRecordMetadata(section, field, directoryEntry);
                long treeStart = checked(record.RecordOffset + RecordHeaderLength);
                long treeEnd = checked(treeStart + record.TreeLength);
                NodeValidationSummary summary = TraverseNode(
                    section,
                    treeStart,
                    treeEnd,
                    depth: 0,
                    field.Kind,
                    record.ValueCount,
                    static _ => { });
                if (summary.NodeLength != record.TreeLength || summary.PrimitiveCount != record.PrimitiveCount)
                    throw new InvalidDataException("Shape DocValues tree does not match its record metadata.");
            }
        }
    }

    /// <summary>Verifies the source checksum once before a merge copies any record bytes.</summary>
    internal void ValidateChecksum()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            _session.ValidateChecksum();
        }
        catch (CodecFileException exception)
        {
            throw new InvalidDataException("Shape DocValues source checksum validation failed during merge.", exception);
        }
    }

    internal byte[] ReadRecordBytes(string fieldName, int documentId)
    {
        if (!TryGetRecordMetadata(fieldName, documentId, out ShapeDocValuesRecordMetadata record))
            throw new KeyNotFoundException($"Shape DocValues record for field '{fieldName}' and document {documentId} is not present.");
        if (record.RecordLength > int.MaxValue)
            throw new InvalidDataException("Shape DocValues record exceeds the maximum supported merge copy length.");

        ShapeDocValuesFieldMetadata field = GetFieldMetadata(fieldName);
        using IndexInput section = OpenFieldInput(field);
        section.Seek(record.RecordOffset);
        byte[] rawRecord = new byte[checked((int)record.RecordLength)];
        section.ReadBytes(rawRecord);
        return rawRecord;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _body.Dispose();
        _session.Dispose();
    }

    private static ShapeDocValuesReader OpenCore(IndexInput input)
    {
        CodecReadSession? session = null;
        IndexInput? body = null;
        try
        {
            session = CodecFileReader.Open(input, ShapeDocValuesCodecFiles.Data, ownsInput: true);
            body = session.OpenBodyInput();
            DirectoryReadResult directory = ReadTopDirectory(body);
            return new ShapeDocValuesReader(session, body, directory.Entries, directory.DirectoryOffset);
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

    private static DirectoryReadResult ReadTopDirectory(IndexInput body)
    {
        if (body.Length < TopFooterLength)
            throw new InvalidDataException("Shape DocValues body is shorter than its top-level footer.");

        long footerStart = body.Length - TopFooterLength;
        body.Seek(footerStart);
        ReadMagic(body, footerStart + TopFooterLength, "SHDV"u8, "Shape DocValues top-level footer magic is invalid.");
        uint footerFieldCount = ReadUInt32(body, body.Length);
        long directoryOffset = body.ReadInt64();
        if (directoryOffset < 0 || directoryOffset > footerStart)
            throw new InvalidDataException("Shape DocValues top-level directory offset is outside the body.");

        body.Seek(directoryOffset);
        uint fieldCount = ReadUInt32(body, footerStart);
        if (fieldCount != footerFieldCount || fieldCount > int.MaxValue
            || fieldCount > (ulong)Math.Max(0, footerStart - body.Position) / 18)
            throw new InvalidDataException("Shape DocValues top-level field count is invalid.");

        var entries = new Dictionary<string, FieldDirectoryEntry>((int)fieldCount, StringComparer.Ordinal);
        string? previousName = null;
        for (int i = 0; i < (int)fieldCount; i++)
        {
            string name = ReadFieldName(body, footerStart);
            if (name.Length == 0
                || (previousName is not null && PackedBkdFieldNameComparer.Instance.Compare(previousName, name) >= 0))
                throw new InvalidDataException("Shape DocValues field names must be non-empty and strictly sorted.");
            long sectionOffset = ReadInt64(body, footerStart);
            long sectionLength = ReadInt64(body, footerStart);
            if (sectionOffset < 0 || sectionLength <= 0
                || sectionOffset > directoryOffset
                || sectionLength > directoryOffset - sectionOffset)
                throw new InvalidDataException($"Shape DocValues field '{name}' points outside its bounded section area.");
            entries.Add(name, new FieldDirectoryEntry(sectionOffset, sectionLength));
            previousName = name;
        }

        if (body.Position != footerStart)
            throw new InvalidDataException("Shape DocValues top-level directory has trailing bytes.");

        FieldDirectoryEntry[] sections = entries.Values.OrderBy(static value => value.Offset).ToArray();
        for (int i = 1; i < sections.Length; i++)
        {
            if (checked(sections[i - 1].Offset + sections[i - 1].Length) > sections[i].Offset)
                throw new InvalidDataException("Shape DocValues field sections overlap.");
        }
        return new DirectoryReadResult(entries, directoryOffset);
    }

    private ShapeDocValuesFieldMetadata ParseField(string fieldName, FieldDirectoryEntry entry)
    {
        try
        {
            using IndexInput section = _body.OpenSharedSlice(entry.Offset, entry.Length);
            if (section.Length < FieldHeaderLength + FieldFooterLength)
                throw new InvalidDataException($"Shape DocValues field '{fieldName}' is shorter than its header and footer.");

            section.Seek(0);
            ReadMagic(section, FieldHeaderLength, "SHF1"u8, $"Shape DocValues field '{fieldName}' has invalid header magic.");
            byte coordinateSystem = section.ReadByte();
            byte flags = section.ReadByte();
            ushort reserved = ReadUInt16(section, FieldHeaderLength);
            int maxDoc = ReadNonNegativeInt32(section, FieldHeaderLength, "maxDoc");
            int recordCount = ReadNonNegativeInt32(section, FieldHeaderLength, "recordCount");
            if (coordinateSystem > 1 || flags != 0 || reserved != 0)
                throw new InvalidDataException($"Shape DocValues field '{fieldName}' has an unknown coordinate system or non-zero reserved metadata.");
            if (recordCount > maxDoc)
                throw new InvalidDataException($"Shape DocValues field '{fieldName}' has more records than maxDoc.");

            SpatialFieldKind kind = coordinateSystem == 0 ? SpatialFieldKind.GeoShape : SpatialFieldKind.XYShape;
            long footerStart = section.Length - FieldFooterLength;
            section.Seek(footerStart);
            ReadMagic(section, section.Length, "SDFT"u8, $"Shape DocValues field '{fieldName}' has invalid footer magic.");
            int footerRecordCount = ReadNonNegativeInt32(section, section.Length, "footer recordCount");
            long recordDirectoryOffset = section.ReadInt64();
            long directoryByteLength = checked((long)recordCount * RecordDirectoryEntryLength);
            if (footerRecordCount != recordCount
                || recordDirectoryOffset < FieldHeaderLength
                || recordDirectoryOffset > footerStart
                || directoryByteLength != footerStart - recordDirectoryOffset)
                throw new InvalidDataException($"Shape DocValues field '{fieldName}' has inconsistent directory bounds or record counts.");

            var metadata = new ShapeDocValuesFieldMetadata(
                fieldName,
                kind,
                maxDoc,
                recordCount,
                entry.Offset,
                entry.Length,
                recordDirectoryOffset);
            ValidateRecordDirectory(section, metadata);
            return metadata;
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"Shape DocValues field '{fieldName}' is truncated.", exception);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException($"Shape DocValues field '{fieldName}' has overflowing metadata.", exception);
        }
    }

    private void ValidateRecordDirectory(IndexInput section, ShapeDocValuesFieldMetadata field)
    {
        long directoryStart = field.RecordDirectoryOffset;
        long previousRecordEnd = FieldHeaderLength;
        uint previousDocumentId = 0;
        for (int i = 0; i < field.RecordCount; i++)
        {
            RecordDirectoryEntry entry = ReadRecordDirectoryEntry(section, field, i);
            if (entry.DocumentId >= (uint)field.MaxDoc
                || (i > 0 && entry.DocumentId <= previousDocumentId))
                throw new InvalidDataException($"Shape DocValues field '{field.FieldName}' record document IDs are invalid or unordered.");
            long recordOffset = checked((long)entry.Offset);
            if (recordOffset < FieldHeaderLength || entry.Length < RecordHeaderLength
                || recordOffset > directoryStart || entry.Length > directoryStart - recordOffset
                || recordOffset < previousRecordEnd)
                throw new InvalidDataException($"Shape DocValues field '{field.FieldName}' has an overlapping or out-of-range record.");
            previousRecordEnd = checked(recordOffset + entry.Length);
            previousDocumentId = entry.DocumentId;
        }
    }

    private static ShapeDocValuesRecordMetadata ReadRecordMetadata(
        IndexInput section,
        ShapeDocValuesFieldMetadata field,
        RecordDirectoryEntry entry)
    {
        long sectionEnd = field.SectionLength;
        long recordOffset = checked((long)entry.Offset);
        long recordEnd = checked(recordOffset + entry.Length);
        if (recordEnd > field.RecordDirectoryOffset || recordEnd > sectionEnd - FieldFooterLength)
            throw new InvalidDataException("Shape DocValues record is outside its bounded field section.");

        section.Seek(recordOffset);
        Span<byte> header = stackalloc byte[RecordHeaderLength];
        EnsureAvailable(section, recordEnd, header.Length);
        section.ReadBytes(header);

        uint valueCount = BinaryPrimitives.ReadUInt32LittleEndian(header);
        uint primitiveCount = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        byte dimensionValue = header[8];
        byte flags = header[9];
        ushort reserved = BinaryPrimitives.ReadUInt16LittleEndian(header[10..]);
        if (valueCount == 0 || primitiveCount == 0 || dimensionValue > (byte)SpatialDimension.Area || reserved != 0)
            throw new InvalidDataException("Shape DocValues record has invalid counts, dimension or reserved metadata.");
        if (field.Kind == SpatialFieldKind.GeoShape ? (flags & 0xFE) != 0 : flags != 0)
            throw new InvalidDataException("Shape DocValues record has unsupported flags for its coordinate system.");

        uint bound0 = BinaryPrimitives.ReadUInt32BigEndian(header[12..]);
        uint bound1 = BinaryPrimitives.ReadUInt32BigEndian(header[16..]);
        uint bound2 = BinaryPrimitives.ReadUInt32BigEndian(header[20..]);
        uint bound3 = BinaryPrimitives.ReadUInt32BigEndian(header[24..]);
        uint bound4 = BinaryPrimitives.ReadUInt32BigEndian(header[28..]);
        uint bound5 = BinaryPrimitives.ReadUInt32BigEndian(header[32..]);
        double accumulator0 = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(header[36..]));
        double accumulator1 = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(header[44..]));
        double accumulator2 = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(header[52..]));
        double weight = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(header[60..]));
        uint treeLength = BinaryPrimitives.ReadUInt32LittleEndian(header[68..]);
        if (!double.IsFinite(accumulator0) || !double.IsFinite(accumulator1)
            || !double.IsFinite(accumulator2) || !double.IsFinite(weight) || weight <= 0
            || treeLength > int.MaxValue
            || checked((ulong)RecordHeaderLength + treeLength) != entry.Length)
            throw new InvalidDataException("Shape DocValues record contains invalid accumulators or a mismatched tree length.");

        if (field.Kind == SpatialFieldKind.GeoShape)
        {
            bool wraps = (flags & 1) != 0;
            if (bound0 > bound1 || bound4 > bound5 || wraps != (bound2 > bound3))
                throw new InvalidDataException("Geo Shape DocValues bounds are inverted or have an inconsistent wrap flag.");
        }
        else if (bound0 > bound1 || bound2 > bound3 || bound4 != 0 || bound5 != 0 || accumulator2 != 0)
        {
            throw new InvalidDataException("XY Shape DocValues bounds or reserved accumulator values are invalid.");
        }

        return new ShapeDocValuesRecordMetadata(
            entry.DocumentId,
            recordOffset,
            entry.Length,
            valueCount,
            primitiveCount,
            (SpatialDimension)dimensionValue,
            flags,
            bound0,
            bound1,
            bound2,
            bound3,
            bound4,
            bound5,
            accumulator0,
            accumulator1,
            accumulator2,
            weight,
            checked((int)treeLength));
    }

    private static NodeValidationSummary TraverseNode(
        IndexInput section,
        long nodeStart,
        long parentEnd,
        int depth,
        SpatialFieldKind fieldKind,
        uint valueCount,
        Action<ShapePrimitive> visitor)
    {
        if (depth > MaximumTreeDepth)
            throw new InvalidDataException("Shape DocValues component tree exceeds its maximum depth.");
        EnsureAvailable(section, parentEnd, NodeHeaderLength);
        section.Seek(nodeStart);
        uint nodeLength = ReadUInt32(section, parentEnd);
        byte nodeKind = ReadByte(section, parentEnd);
        byte flags = ReadByte(section, parentEnd);
        ushort reserved = ReadUInt16(section, parentEnd);
        Span<byte> encodedBounds = stackalloc byte[16];
        ReadBytes(section, parentEnd, encodedBounds);
        NodeBounds nodeBounds = new(
            BinaryPrimitives.ReadUInt32BigEndian(encodedBounds),
            BinaryPrimitives.ReadUInt32BigEndian(encodedBounds[4..]),
            BinaryPrimitives.ReadUInt32BigEndian(encodedBounds[8..]),
            BinaryPrimitives.ReadUInt32BigEndian(encodedBounds[12..]));
        long nodeEnd;
        try
        {
            nodeEnd = checked(nodeStart + nodeLength);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Shape DocValues node length overflows its bounded tree.", exception);
        }
        if (nodeLength < NodeHeaderLength || nodeEnd > parentEnd || flags != 0 || reserved != 0)
            throw new InvalidDataException("Shape DocValues node has invalid length, flags or reserved metadata.");

        if (nodeKind == 1)
        {
            ushort count = ReadUInt16(section, nodeEnd);
            ushort leafReserved = ReadUInt16(section, nodeEnd);
            if (count is 0 or > 16 || leafReserved != 0
                || checked((long)NodeHeaderLength + ((long)count * ShapePrimitiveCodec.PackedValueLength)) != nodeLength)
                throw new InvalidDataException("Shape DocValues leaf has invalid primitive count, reserved bytes or length.");

            uint minD0 = uint.MaxValue;
            uint minD1 = uint.MaxValue;
            uint maxD2 = uint.MinValue;
            uint maxD3 = uint.MinValue;
            Span<byte> primitiveBytes = stackalloc byte[ShapePrimitiveCodec.PackedValueLength];
            Span<byte> previousPrimitive = stackalloc byte[ShapePrimitiveCodec.PackedValueLength];
            bool hasPreviousPrimitive = false;
            for (int i = 0; i < count; i++)
            {
                ReadBytes(section, nodeEnd, primitiveBytes);
                if (hasPreviousPrimitive && previousPrimitive.SequenceCompareTo(primitiveBytes) > 0)
                    throw new InvalidDataException("Shape DocValues leaf primitives are not in canonical byte order.");
                minD0 = Math.Min(minD0, BinaryPrimitives.ReadUInt32BigEndian(primitiveBytes));
                minD1 = Math.Min(minD1, BinaryPrimitives.ReadUInt32BigEndian(primitiveBytes[4..]));
                maxD2 = Math.Max(maxD2, BinaryPrimitives.ReadUInt32BigEndian(primitiveBytes[8..]));
                maxD3 = Math.Max(maxD3, BinaryPrimitives.ReadUInt32BigEndian(primitiveBytes[12..]));
                ShapePrimitive primitive = ShapePrimitiveCodec.Decode(primitiveBytes, fieldKind);
                if (primitive.ValueOrdinal >= valueCount)
                    throw new InvalidDataException("Shape DocValues primitive value ordinal is outside its record.");
                visitor(primitive);
                primitiveBytes.CopyTo(previousPrimitive);
                hasPreviousPrimitive = true;
            }
            if (section.Position != nodeEnd
                || minD0 != nodeBounds.MinimumD0 || minD1 != nodeBounds.MinimumD1
                || maxD2 != nodeBounds.MaximumD2 || maxD3 != nodeBounds.MaximumD3)
                throw new InvalidDataException("Shape DocValues leaf bounds do not match its primitive bytes.");
            return new NodeValidationSummary(checked((int)nodeLength), count, nodeBounds);
        }

        if (nodeKind != 0)
            throw new InvalidDataException("Shape DocValues node kind is unknown.");
        uint leftLength = ReadUInt32(section, nodeEnd);
        long leftStart = section.Position;
        long childrenLength = nodeEnd - leftStart;
        if (leftLength < NodeHeaderLength || leftLength >= childrenLength)
            throw new InvalidDataException("Shape DocValues internal node has an invalid left-subtree length.");
        long rightStart = checked(leftStart + leftLength);
        NodeValidationSummary left = TraverseNode(section, leftStart, rightStart, depth + 1, fieldKind, valueCount, visitor);
        if (left.NodeLength != leftLength)
            throw new InvalidDataException("Shape DocValues left subtree does not match its declared length.");
        NodeValidationSummary right = TraverseNode(section, rightStart, nodeEnd, depth + 1, fieldKind, valueCount, visitor);
        if (checked(leftStart + leftLength + right.NodeLength) != nodeEnd)
            throw new InvalidDataException("Shape DocValues right subtree does not fill its parent node.");

        NodeBounds combined = new(
            Math.Min(left.Bounds.MinimumD0, right.Bounds.MinimumD0),
            Math.Min(left.Bounds.MinimumD1, right.Bounds.MinimumD1),
            Math.Max(left.Bounds.MaximumD2, right.Bounds.MaximumD2),
            Math.Max(left.Bounds.MaximumD3, right.Bounds.MaximumD3));
        if (combined != nodeBounds)
            throw new InvalidDataException("Shape DocValues internal bounds do not match its child bounds.");
        return new NodeValidationSummary(
            checked((int)nodeLength),
            checked(left.PrimitiveCount + right.PrimitiveCount),
            nodeBounds);
    }

    private RecordDirectoryEntry ReadRecordDirectoryEntry(
        IndexInput section,
        ShapeDocValuesFieldMetadata field,
        int index)
    {
        if ((uint)index >= (uint)field.RecordCount)
            throw new ArgumentOutOfRangeException(nameof(index));
        long position = checked(field.RecordDirectoryOffset + ((long)index * RecordDirectoryEntryLength));
        section.Seek(position);
        uint documentId = ReadUInt32(section, field.SectionLength);
        long offset = section.ReadInt64();
        uint length = ReadUInt32(section, field.SectionLength);
        if (offset < 0)
            throw new InvalidDataException("Shape DocValues record offset exceeds the supported signed range.");
        return new RecordDirectoryEntry(documentId, checked((ulong)offset), length);
    }

    private IndexInput OpenFieldInput(ShapeDocValuesFieldMetadata field)
        => _body.OpenSharedSlice(field.SectionOffset, field.SectionLength);

    private void ValidateCompleteSectionLayout()
    {
        FieldDirectoryEntry[] sections = _directory.Values.OrderBy(static entry => entry.Offset).ToArray();
        long next = 0;
        foreach (FieldDirectoryEntry section in sections)
        {
            if (section.Offset != next)
                throw new InvalidDataException("Shape DocValues field sections leave unparsed bytes in the body.");
            next = checked(section.Offset + section.Length);
        }
        if (next != _topDirectoryOffset)
            throw new InvalidDataException("Shape DocValues body has unparsed bytes before its top-level directory.");
    }

    private static string ReadFieldName(IndexInput input, long end)
    {
        int length = ReadBoundedVarInt(input, end);
        if (length <= 0 || length > MaximumFieldNameBytes || length > end - input.Position)
            throw new InvalidDataException("Shape DocValues field name length is outside its bounded directory.");

        byte[] bytes = new byte[length];
        input.ReadBytes(bytes);
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Shape DocValues field name is not valid strict UTF-8.", exception);
        }
    }

    private static int ReadBoundedVarInt(IndexInput input, long end)
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (input.Position >= end)
                throw new InvalidDataException("Shape DocValues field name length is truncated.");
            int next = input.ReadByte();
            if (shift == 28 && (next & 0xF0) != 0)
                throw new InvalidDataException("Shape DocValues field name length overflows UInt32.");
            value |= (uint)(next & 0x7F) << shift;
            if ((next & 0x80) == 0)
            {
                if (value > int.MaxValue)
                    throw new InvalidDataException("Shape DocValues field name length exceeds Int32.");
                return (int)value;
            }
        }
        throw new InvalidDataException("Shape DocValues field name length has an invalid varint.");
    }

    private static void ReadMagic(IndexInput input, long end, ReadOnlySpan<byte> magic, string message)
    {
        Span<byte> actual = stackalloc byte[4];
        ReadBytes(input, end, actual);
        if (!actual.SequenceEqual(magic))
            throw new InvalidDataException(message);
    }

    private static byte ReadByte(IndexInput input, long end)
    {
        EnsureAvailable(input, end, sizeof(byte));
        return input.ReadByte();
    }

    private static ushort ReadUInt16(IndexInput input, long end)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        ReadBytes(input, end, bytes);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    }

    private static uint ReadUInt32(IndexInput input, long end)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        ReadBytes(input, end, bytes);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    private static long ReadInt64(IndexInput input, long end)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        ReadBytes(input, end, bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    private static int ReadNonNegativeInt32(IndexInput input, long end, string name)
    {
        uint value = ReadUInt32(input, end);
        if (value > int.MaxValue)
            throw new InvalidDataException($"Shape DocValues {name} exceeds the supported range.");
        return (int)value;
    }

    private static void ReadBytes(IndexInput input, long end, Span<byte> destination)
    {
        EnsureAvailable(input, end, destination.Length);
        input.ReadBytes(destination);
    }

    private static void EnsureAvailable(IndexInput input, long end, long count)
    {
        if (count < 0 || input.Position > end || count > end - input.Position)
            throw new InvalidDataException("Shape DocValues metadata exceeds its bounded section.");
    }

    private readonly record struct FieldDirectoryEntry(long Offset, long Length);
    private readonly record struct DirectoryReadResult(Dictionary<string, FieldDirectoryEntry> Entries, long DirectoryOffset);
    private readonly record struct RecordDirectoryEntry(uint DocumentId, ulong Offset, uint Length);
    private readonly record struct NodeBounds(uint MinimumD0, uint MinimumD1, uint MaximumD2, uint MaximumD3);
    private readonly record struct NodeValidationSummary(int NodeLength, int PrimitiveCount, NodeBounds Bounds);
}

internal readonly record struct ShapeDocValuesFieldMetadata(
    string FieldName,
    SpatialFieldKind Kind,
    int MaxDoc,
    int RecordCount,
    long SectionOffset,
    long SectionLength,
    long RecordDirectoryOffset);

internal readonly record struct ShapeDocValuesRecordMetadata(
    uint DocumentId,
    long RecordOffset,
    uint RecordLength,
    uint ValueCount,
    uint PrimitiveCount,
    SpatialDimension HighestDimension,
    byte Flags,
    uint Bound0,
    uint Bound1,
    uint Bound2,
    uint Bound3,
    uint Bound4,
    uint Bound5,
    double Accumulator0,
    double Accumulator1,
    double Accumulator2,
    double Weight,
    int TreeLength);
