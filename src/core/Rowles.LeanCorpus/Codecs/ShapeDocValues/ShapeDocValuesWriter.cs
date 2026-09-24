using System.Buffers.Binary;
using System.Text;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.PackedBkd.Internal;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Internal;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Codecs.ShapeDocValues;

internal static class ShapeDocValuesWriter
{
    private const int RecordHeaderLength = 72;
    private const int MaximumFieldNameBytes = 1 << 20;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static void Write(
        string filePath,
        int maxDoc,
        IReadOnlyDictionary<string, ShapeDocValuesFieldBuffer> fields)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDoc);
        ArgumentNullException.ThrowIfNull(fields);

        string[] names = fields
            .Where(static pair => pair.Value.Records.Count > 0)
            .Select(static pair => pair.Key)
            .ToArray();
        Array.Sort(names, PackedBkdFieldNameComparer.Instance);

        CodecFileWriter.WriteAtomically(filePath, ShapeDocValuesCodecFiles.Data, durable: false, output =>
        {
            long bodyStart = output.Position;
            var directory = new DirectoryEntry[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                ShapeDocValuesFieldBuffer field = fields[names[i]];
                if (!string.Equals(field.FieldName, names[i], StringComparison.Ordinal))
                    throw new InvalidDataException("Shape DocValues dictionary key does not match its field buffer name.");
                long sectionOffset = checked(output.Position - bodyStart);
                WriteFieldSection(output, field, maxDoc);
                directory[i] = new DirectoryEntry(
                    names[i],
                    sectionOffset,
                    checked(output.Position - bodyStart - sectionOffset));
            }

            long directoryOffset = checked(output.Position - bodyStart);
            output.WriteInt32(names.Length);
            foreach (DirectoryEntry entry in directory)
            {
                WriteFieldName(output, entry.Name);
                output.WriteInt64(entry.Offset);
                output.WriteInt64(entry.Length);
            }

            output.WriteBytes("SHDV"u8);
            output.WriteInt32(names.Length);
            output.WriteInt64(directoryOffset);
        });
    }

    private static void WriteFieldSection(
        CodecBodyOutput output,
        ShapeDocValuesFieldBuffer field,
        int maxDoc)
    {
        IReadOnlyList<ShapeDocValuesRecord> records = field.Records;
        if (records.Count > maxDoc)
            throw new InvalidDataException($"Shape DocValues field '{field.FieldName}' has more records than maxDoc.");

        long sectionStart = output.Position;
        output.WriteBytes("SHF1"u8);
        output.WriteByte(field.Kind == SpatialFieldKind.GeoShape ? (byte)0 : (byte)1);
        output.WriteByte(0);
        output.WriteByte(0);
        output.WriteByte(0);
        output.WriteInt32(maxDoc);
        output.WriteInt32(records.Count);

        var directory = new RecordDirectoryEntry[records.Count];
        int previousDocId = -1;
        for (int i = 0; i < records.Count; i++)
        {
            ShapeDocValuesRecord record = records[i];
            if (record.DocumentId <= previousDocId || (uint)record.DocumentId >= (uint)maxDoc)
                throw new InvalidDataException("Shape DocValues records must have increasing in-range document IDs.");
            previousDocId = record.DocumentId;

            long recordOffset = checked(output.Position - sectionStart);
            int recordLength;
            if (field.UsesRawRecords)
            {
                ReadOnlyMemory<byte> rawRecord = field.GetRawRecord(i);
                if (record.ValueCount == 0 || record.PrimitiveCount == 0 || rawRecord.Length < RecordHeaderLength)
                    throw new InvalidDataException("Raw Shape DocValues record has invalid counts or length.");
                output.WriteBytes(rawRecord.Span);
                recordLength = rawRecord.Length;
            }
            else
            {
                ReadOnlyMemory<byte> primitives = field.GetPrimitives(record);
                if (record.ValueCount == 0 || record.PrimitiveCount == 0
                    || primitives.Length != checked(record.PrimitiveCount * ShapePrimitiveCodec.PackedValueLength))
                    throw new InvalidDataException("Shape DocValues record has invalid value or primitive counts.");

                byte[] tree = ShapeDocValuesTreeWriter.Build(primitives);
                if (tree.Length <= 0)
                    throw new InvalidDataException("Shape DocValues component tree cannot be empty.");
                WriteRecordHeader(output, field.Kind, record, primitives, checked((uint)tree.Length));
                output.WriteBytes(tree);
                recordLength = checked(RecordHeaderLength + tree.Length);
            }
            directory[i] = new RecordDirectoryEntry(
                checked((uint)record.DocumentId),
                checked((ulong)recordOffset),
                checked((uint)recordLength));
        }

        long directoryOffset = checked(output.Position - sectionStart);
        foreach (RecordDirectoryEntry entry in directory)
        {
            output.WriteInt32(checked((int)entry.DocumentId));
            output.WriteInt64(checked((long)entry.Offset));
            output.WriteInt32(checked((int)entry.Length));
        }

        output.WriteBytes("SDFT"u8);
        output.WriteInt32(records.Count);
        output.WriteInt64(directoryOffset);
    }

    private static void WriteRecordHeader(
        CodecBodyOutput output,
        SpatialFieldKind fieldKind,
        ShapeDocValuesRecord record,
        ReadOnlyMemory<byte> primitiveBytes,
        uint treeLength)
    {
        ShapeRecordMetadata metadata = ShapeDocValuesMetadata.Compute(fieldKind, record.ValueCount, primitiveBytes);
        output.WriteInt32(checked((int)record.ValueCount));
        output.WriteInt32(record.PrimitiveCount);
        output.WriteByte((byte)metadata.HighestDimension);
        output.WriteByte(metadata.Flags);
        output.WriteByte(0);
        output.WriteByte(0);
        output.WriteBytes(metadata.Bounds);
        output.WriteInt64(BitConverter.DoubleToInt64Bits(metadata.Accumulator0));
        output.WriteInt64(BitConverter.DoubleToInt64Bits(metadata.Accumulator1));
        output.WriteInt64(BitConverter.DoubleToInt64Bits(metadata.Accumulator2));
        output.WriteInt64(BitConverter.DoubleToInt64Bits(metadata.Weight));
        output.WriteInt32(checked((int)treeLength));
    }

    private static void WriteFieldName(CodecBodyOutput output, string fieldName)
    {
        byte[] encoded;
        try
        {
            encoded = StrictUtf8.GetBytes(fieldName);
        }
        catch (EncoderFallbackException exception)
        {
            throw new ArgumentException("Shape DocValues field names must contain valid UTF-16 for strict UTF-8 encoding.", nameof(fieldName), exception);
        }

        if (encoded.Length == 0 || encoded.Length > MaximumFieldNameBytes)
            throw new ArgumentException($"Shape DocValues field names must contain 1 to {MaximumFieldNameBytes} UTF-8 bytes.", nameof(fieldName));
        output.WriteVarInt(encoded.Length);
        output.WriteBytes(encoded);
    }

    private readonly record struct DirectoryEntry(string Name, long Offset, long Length);

    private readonly record struct RecordDirectoryEntry(uint DocumentId, ulong Offset, uint Length);
}

internal readonly record struct ShapeRecordMetadata(
    SpatialDimension HighestDimension,
    byte Flags,
    byte[] Bounds,
    double Accumulator0,
    double Accumulator1,
    double Accumulator2,
    double Weight);

internal static class ShapeDocValuesMetadata
{
    internal static ShapeRecordMetadata Compute(
        SpatialFieldKind fieldKind,
        uint valueCount,
        ReadOnlyMemory<byte> primitiveBytes)
    {
        if (valueCount == 0 || primitiveBytes.Length == 0
            || primitiveBytes.Length % ShapePrimitiveCodec.PackedValueLength != 0)
            throw new InvalidDataException("Shape DocValues metadata requires values and complete packed primitives.");

        bool geo = fieldKind == SpatialFieldKind.GeoShape;
        bool xy = fieldKind == SpatialFieldKind.XYShape;
        if (!geo && !xy)
            throw new InvalidDataException($"Spatial field kind '{fieldKind}' cannot contain Shape DocValues.");

        int primitiveCount = primitiveBytes.Length / ShapePrimitiveCodec.PackedValueLength;
        uint minY = uint.MaxValue;
        uint maxY = uint.MinValue;
        uint minX = uint.MaxValue;
        uint maxX = uint.MinValue;
        var longitudeIntervals = geo ? new List<LongitudeInterval>(primitiveCount) : null;
        SpatialDimension highestDimension = SpatialDimension.Point;
        double accumulator0 = 0;
        double accumulator1 = 0;
        double accumulator2 = 0;
        double weight = 0;
        ReadOnlySpan<byte> bytes = primitiveBytes.Span;

        for (int i = 0; i < primitiveCount; i++)
        {
            ReadOnlySpan<byte> encoded = bytes.Slice(i * ShapePrimitiveCodec.PackedValueLength, ShapePrimitiveCodec.PackedValueLength);
            ShapePrimitive primitive = ShapePrimitiveCodec.Decode(encoded, fieldKind);
            uint d0 = BinaryPrimitives.ReadUInt32BigEndian(encoded);
            uint d1 = BinaryPrimitives.ReadUInt32BigEndian(encoded[4..]);
            uint d2 = BinaryPrimitives.ReadUInt32BigEndian(encoded[8..]);
            uint d3 = BinaryPrimitives.ReadUInt32BigEndian(encoded[12..]);
            minY = Math.Min(minY, d0);
            maxY = Math.Max(maxY, d2);
            minX = Math.Min(minX, d1);
            maxX = Math.Max(maxX, d3);
            if (geo)
                longitudeIntervals!.Add(new LongitudeInterval(
                    ShapePrimitiveCodec.DecodeXKey(d1, fieldKind),
                    ShapePrimitiveCodec.DecodeXKey(d3, fieldKind)));

            SpatialDimension dimension = primitive.Kind switch
            {
                ShapePrimitiveKind.Point => SpatialDimension.Point,
                ShapePrimitiveKind.Line => SpatialDimension.Line,
                ShapePrimitiveKind.Triangle => SpatialDimension.Area,
                _ => throw new InvalidDataException("A Shape DocValues primitive kind is invalid."),
            };
            if (dimension > highestDimension)
            {
                highestDimension = dimension;
                accumulator0 = 0;
                accumulator1 = 0;
                accumulator2 = 0;
                weight = 0;
            }

            if (dimension != highestDimension)
                continue;

            double x;
            double y;
            double primitiveWeight;
            switch (dimension)
            {
                case SpatialDimension.Point:
                    x = primitive.A.X;
                    y = primitive.A.Y;
                    primitiveWeight = 1;
                    break;
                case SpatialDimension.Line:
                    double deltaX = primitive.B.X - primitive.A.X;
                    double deltaY = primitive.B.Y - primitive.A.Y;
                    primitiveWeight = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
                    if (primitiveWeight == 0)
                        continue;
                    x = (primitive.A.X + primitive.B.X) / 2;
                    y = (primitive.A.Y + primitive.B.Y) / 2;
                    break;
                case SpatialDimension.Area:
                    double cross = ((primitive.B.X - primitive.A.X) * (primitive.C.Y - primitive.A.Y))
                        - ((primitive.B.Y - primitive.A.Y) * (primitive.C.X - primitive.A.X));
                    primitiveWeight = Math.Abs(cross) / 2;
                    if (primitiveWeight == 0)
                        continue;
                    x = (primitive.A.X + primitive.B.X + primitive.C.X) / 3;
                    y = (primitive.A.Y + primitive.B.Y + primitive.C.Y) / 3;
                    break;
                default:
                    throw new InvalidDataException("Shape DocValues spatial dimension is invalid.");
            }

            if (geo)
            {
                accumulator0 += y * primitiveWeight;
                double radians = x * (Math.PI / 180d);
                accumulator1 += Math.Sin(radians) * primitiveWeight;
                accumulator2 += Math.Cos(radians) * primitiveWeight;
            }
            else
            {
                accumulator0 += x * primitiveWeight;
                accumulator1 += y * primitiveWeight;
            }
            weight += primitiveWeight;
        }

        if (!double.IsFinite(accumulator0) || !double.IsFinite(accumulator1)
            || !double.IsFinite(accumulator2) || !double.IsFinite(weight) || weight <= 0)
            throw new InvalidDataException("Shape DocValues centroid accumulators are non-finite or have non-positive weight.");

        var bounds = new byte[24];
        byte flags = 0;
        if (geo)
        {
            (double west, double east, bool wraps) = MinimalLongitudeEnvelope(longitudeIntervals!);
            flags = wraps ? (byte)1 : (byte)0;
            WriteGeoLatitude(bounds, 0, ShapePrimitiveCodec.DecodeYKey(minY, fieldKind));
            WriteGeoLatitude(bounds, 1, ShapePrimitiveCodec.DecodeYKey(maxY, fieldKind));
            WriteGeoLongitude(bounds, 2, west);
            WriteGeoLongitude(bounds, 3, east);
            WriteGeoLongitude(bounds, 4, ShapePrimitiveCodec.DecodeXKey(minX, fieldKind));
            WriteGeoLongitude(bounds, 5, ShapePrimitiveCodec.DecodeXKey(maxX, fieldKind));
        }
        else
        {
            WriteRawCode(bounds, 0, minX);
            WriteRawCode(bounds, 1, maxX);
            WriteRawCode(bounds, 2, minY);
            WriteRawCode(bounds, 3, maxY);
            // The last two XY bound slots are reserved zero values.
        }

        return new ShapeRecordMetadata(
            highestDimension,
            flags,
            bounds,
            accumulator0,
            accumulator1,
            accumulator2,
            weight);
    }

    private static (double West, double East, bool Wraps) MinimalLongitudeEnvelope(List<LongitudeInterval> intervals)
    {
        if (intervals.Count == 0)
            throw new InvalidDataException("A Geo shape record has no longitude intervals.");

        intervals.Sort(static (left, right) =>
        {
            int comparison = left.West.CompareTo(right.West);
            return comparison != 0 ? comparison : left.East.CompareTo(right.East);
        });

        var merged = new List<LongitudeInterval>(intervals.Count);
        foreach (LongitudeInterval interval in intervals)
        {
            double start = interval.West + 180d;
            double end = interval.East + 180d;
            if (merged.Count == 0 || start > merged[^1].East)
                merged.Add(new LongitudeInterval(start, end));
            else if (end > merged[^1].East)
                merged[^1] = merged[^1] with { East = end };
        }

        double largestGap = double.NegativeInfinity;
        double westCoordinate = 0;
        double eastCoordinate = 0;
        for (int i = 0; i < merged.Count; i++)
        {
            LongitudeInterval current = merged[i];
            LongitudeInterval next = merged[(i + 1) % merged.Count];
            double nextStart = i + 1 == merged.Count ? next.West + 360d : next.West;
            double gap = nextStart - current.East;
            double candidateWest = next.West;
            double candidateEast = current.East;
            if (gap > largestGap || (gap == largestGap && candidateWest < westCoordinate))
            {
                largestGap = gap;
                westCoordinate = candidateWest;
                eastCoordinate = candidateEast;
            }
        }

        double west = NormaliseLongitude(westCoordinate - 180d);
        double east = NormaliseLongitude(eastCoordinate - 180d);
        return (west, east, west > east);
    }

    private static double NormaliseLongitude(double longitude)
    {
        while (longitude < -180d)
            longitude += 360d;
        while (longitude > 180d)
            longitude -= 360d;
        return longitude == 0 ? 0 : longitude;
    }

    private static void WriteGeoLatitude(Span<byte> destination, int slot, double coordinate)
    {
        int encoded = GeoEncodingUtils.EncodeLat(coordinate);
        uint sortable = unchecked((uint)(encoded ^ int.MinValue));
        WriteRawCode(destination, slot, sortable);
    }

    private static void WriteGeoLongitude(Span<byte> destination, int slot, double coordinate)
    {
        int encoded = GeoEncodingUtils.EncodeLon(coordinate);
        uint sortable = unchecked((uint)(encoded ^ int.MinValue));
        WriteRawCode(destination, slot, sortable);
    }

    private static void WriteRawCode(Span<byte> destination, int slot, uint value)
        => BinaryPrimitives.WriteUInt32BigEndian(destination.Slice(slot * sizeof(uint), sizeof(uint)), value);

    private readonly record struct LongitudeInterval(double West, double East);
}

internal static class ShapeDocValuesTreeWriter
{
    private const int LeafCapacity = 16;
    private const int NodePrefixLength = 24;
    private const int PrimitiveLength = ShapePrimitiveCodec.PackedValueLength;

    internal static byte[] Build(ReadOnlyMemory<byte> primitives)
    {
        if (primitives.Length == 0 || primitives.Length % PrimitiveLength != 0)
            throw new InvalidDataException("Shape DocValues tree input is empty or has an invalid primitive length.");
        int primitiveCount = primitives.Length / PrimitiveLength;
        int leafCount = CountLeaves(primitiveCount);
        int capacity = checked((primitiveCount * PrimitiveLength) + (leafCount * 56) - 28);
        using var stream = new MemoryStream(capacity);
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        int[] ordinals = Enumerable.Range(0, primitiveCount).ToArray();
        var comparer = new PrimitivePartitionComparer(primitives);
        WriteNode(writer, stream, ordinals, 0, primitiveCount, depth: 0, comparer);
        if (stream.Length != capacity)
            throw new InvalidDataException("Shape DocValues tree length does not match its computed size.");
        return stream.ToArray();
    }

    private static int WriteNode(
        BinaryWriter writer,
        MemoryStream stream,
        int[] ordinals,
        int start,
        int count,
        int depth,
        PrimitivePartitionComparer comparer)
    {
        if (depth > 64)
            throw new InvalidDataException("Shape DocValues tree exceeds the maximum depth of 64.");

        long nodeStart = stream.Position;
        NodeBounds bounds = GetBounds(comparer.Primitives, ordinals, start, count);
        writer.Write(0);
        bool leaf = count <= LeafCapacity;
        writer.Write(leaf ? (byte)1 : (byte)0);
        writer.Write((byte)0);
        writer.Write((ushort)0);
        Span<byte> boundBytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32BigEndian(boundBytes, bounds.MinimumD0);
        BinaryPrimitives.WriteUInt32BigEndian(boundBytes[4..], bounds.MinimumD1);
        BinaryPrimitives.WriteUInt32BigEndian(boundBytes[8..], bounds.MaximumD2);
        BinaryPrimitives.WriteUInt32BigEndian(boundBytes[12..], bounds.MaximumD3);
        writer.Write(boundBytes);

        if (leaf)
        {
            comparer.Axis = -1;
            Array.Sort(ordinals, start, count, comparer);
            writer.Write(checked((ushort)count));
            writer.Write((ushort)0);
            ReadOnlySpan<byte> bytes = comparer.Primitives.Span;
            for (int i = start; i < start + count; i++)
                writer.Write(bytes.Slice(ordinals[i] * PrimitiveLength, PrimitiveLength));
        }
        else
        {
            comparer.Axis = depth % 2;
            Array.Sort(ordinals, start, count, comparer);
            writer.Write(0u);
            int leftCount = count / 2;
            int leftLength = WriteNode(writer, stream, ordinals, start, leftCount, depth + 1, comparer);
            long leftLengthPosition = nodeStart + NodePrefixLength;
            long returnPosition = stream.Position;
            stream.Position = leftLengthPosition;
            writer.Write(checked((uint)leftLength));
            stream.Position = returnPosition;
            int rightCount = count - leftCount;
            _ = WriteNode(writer, stream, ordinals, start + leftCount, rightCount, depth + 1, comparer);
        }

        int nodeLength = checked((int)(stream.Position - nodeStart));
        long nodeEnd = stream.Position;
        stream.Position = nodeStart;
        writer.Write(nodeLength);
        stream.Position = nodeEnd;
        return nodeLength;
    }

    private static NodeBounds GetBounds(
        ReadOnlyMemory<byte> primitives,
        int[] ordinals,
        int start,
        int count)
    {
        ReadOnlySpan<byte> bytes = primitives.Span;
        uint minimumD0 = uint.MaxValue;
        uint minimumD1 = uint.MaxValue;
        uint maximumD2 = uint.MinValue;
        uint maximumD3 = uint.MinValue;
        for (int i = start; i < start + count; i++)
        {
            ReadOnlySpan<byte> primitive = bytes.Slice(ordinals[i] * PrimitiveLength, PrimitiveLength);
            minimumD0 = Math.Min(minimumD0, BinaryPrimitives.ReadUInt32BigEndian(primitive));
            minimumD1 = Math.Min(minimumD1, BinaryPrimitives.ReadUInt32BigEndian(primitive[4..]));
            maximumD2 = Math.Max(maximumD2, BinaryPrimitives.ReadUInt32BigEndian(primitive[8..]));
            maximumD3 = Math.Max(maximumD3, BinaryPrimitives.ReadUInt32BigEndian(primitive[12..]));
        }
        return new NodeBounds(minimumD0, minimumD1, maximumD2, maximumD3);
    }

    private static int CountLeaves(int count)
        => count <= LeafCapacity ? 1 : checked(CountLeaves(count / 2) + CountLeaves(count - (count / 2)));

    private readonly record struct NodeBounds(uint MinimumD0, uint MinimumD1, uint MaximumD2, uint MaximumD3);

    private sealed class PrimitivePartitionComparer(ReadOnlyMemory<byte> primitives) : IComparer<int>
    {
        internal ReadOnlyMemory<byte> Primitives => primitives;
        internal int Axis { get; set; } = -1;

        public int Compare(int leftOrdinal, int rightOrdinal)
        {
            ReadOnlySpan<byte> bytes = primitives.Span;
            ReadOnlySpan<byte> left = bytes.Slice(leftOrdinal * PrimitiveLength, PrimitiveLength);
            ReadOnlySpan<byte> right = bytes.Slice(rightOrdinal * PrimitiveLength, PrimitiveLength);
            if (Axis >= 0)
            {
                int minDimension = Axis == 0 ? 4 : 0;
                int maxDimension = Axis == 0 ? 12 : 8;
                ulong leftCentre = (ulong)BinaryPrimitives.ReadUInt32BigEndian(left.Slice(minDimension, 4))
                    + BinaryPrimitives.ReadUInt32BigEndian(left.Slice(maxDimension, 4));
                ulong rightCentre = (ulong)BinaryPrimitives.ReadUInt32BigEndian(right.Slice(minDimension, 4))
                    + BinaryPrimitives.ReadUInt32BigEndian(right.Slice(maxDimension, 4));
                int centreComparison = leftCentre.CompareTo(rightCentre);
                if (centreComparison != 0)
                    return centreComparison;
            }

            int bytesComparison = left.SequenceCompareTo(right);
            return bytesComparison != 0 ? bytesComparison : leftOrdinal.CompareTo(rightOrdinal);
        }
    }
}
