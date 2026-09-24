using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.ShapeDocValues;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search.Spatial;
using Rowles.LeanCorpus.Search.Spatial.Internal;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Search;

[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class ShapeDocValuesReaderTests
{
    [Fact(DisplayName = "Empty Shape DocValues file has a valid canonical directory")]
    public void EmptyFile_OpensAndDeepValidates()
    {
        string path = GetPath();
        try
        {
            ShapeDocValuesWriter.Write(path, maxDoc: 0, new Dictionary<string, ShapeDocValuesFieldBuffer>());

            using ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path);

            Assert.Empty(reader.FieldNames);
            reader.DeepValidate();
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues writer emits the fixed XY record and leaf bytes")]
    public void Writer_EmitsGoldenXyRecordAndLeafHeaders()
    {
        string path = GetPath();
        byte[] primitive = EncodePoint(SpatialFieldKind.XYShape, new ShapeVertex(2, -1), 0);
        try
        {
            using var field = new ShapeDocValuesFieldBuffer("x", SpatialFieldKind.XYShape);
            field.AppendValue(documentId: 0, valueOrdinal: 0, primitive);
            ShapeDocValuesWriter.Write(path, maxDoc: 1, new Dictionary<string, ShapeDocValuesFieldBuffer>
            {
                ["x"] = field,
            });

            using var input = new IndexInput(path);
            using CodecReadSession frame = CodecFileReader.Open(input, ShapeDocValuesCodecFiles.Data);
            byte[] body = frame.ReadBody();

            var expectedRecordHeader = new byte[72];
            BinaryPrimitives.WriteUInt32LittleEndian(expectedRecordHeader, 1);
            BinaryPrimitives.WriteUInt32LittleEndian(expectedRecordHeader.AsSpan(4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(expectedRecordHeader.AsSpan(12), ReadDimension(primitive, 1));
            BinaryPrimitives.WriteUInt32BigEndian(expectedRecordHeader.AsSpan(16), ReadDimension(primitive, 3));
            BinaryPrimitives.WriteUInt32BigEndian(expectedRecordHeader.AsSpan(20), ReadDimension(primitive, 0));
            BinaryPrimitives.WriteUInt32BigEndian(expectedRecordHeader.AsSpan(24), ReadDimension(primitive, 2));
            BinaryPrimitives.WriteInt64LittleEndian(expectedRecordHeader.AsSpan(36), BitConverter.DoubleToInt64Bits(2));
            BinaryPrimitives.WriteInt64LittleEndian(expectedRecordHeader.AsSpan(44), BitConverter.DoubleToInt64Bits(-1));
            BinaryPrimitives.WriteInt64LittleEndian(expectedRecordHeader.AsSpan(60), BitConverter.DoubleToInt64Bits(1));
            BinaryPrimitives.WriteUInt32LittleEndian(expectedRecordHeader.AsSpan(68), 56);
            Assert.Equal(expectedRecordHeader, body.AsSpan(16, 72).ToArray());

            var expectedTree = new byte[56];
            BinaryPrimitives.WriteUInt32LittleEndian(expectedTree, 56);
            expectedTree[4] = 1;
            primitive.AsSpan(0, 16).CopyTo(expectedTree.AsSpan(8));
            BinaryPrimitives.WriteUInt16LittleEndian(expectedTree.AsSpan(24), 1);
            primitive.CopyTo(expectedTree, 28);
            Assert.Equal(expectedTree, body.AsSpan(88, 56).ToArray());

            Assert.Equal("SHDV", System.Text.Encoding.ASCII.GetString(body.AsSpan(body.Length - 16, 4)));
            Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(body.Length - 12)));
            Assert.Equal(176, BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(body.Length - 8)));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues reader binary-searches records and visits a deterministic internal tree")]
    public void Reader_OpensUnicodeFieldsAndTraversesInternalTree()
    {
        string path = GetPath();
        try
        {
            using var geo = new ShapeDocValuesFieldBuffer("shape", SpatialFieldKind.GeoShape);
            byte[] westPoint = EncodePoint(SpatialFieldKind.GeoShape, new ShapeVertex(179, 2), 0);
            byte[] eastPoint = EncodePoint(SpatialFieldKind.GeoShape, new ShapeVertex(-179, 4), 1);
            geo.AppendValue(1, 0, westPoint);
            geo.AppendValue(1, 1, eastPoint);

            using var xy = new ShapeDocValuesFieldBuffer("shape-prefix", SpatialFieldKind.XYShape);
            byte[] xyPoints = new byte[17 * ShapePrimitiveCodec.PackedValueLength];
            for (int i = 0; i < 17; i++)
                EncodePoint(xyPoints.AsSpan(i * ShapePrimitiveCodec.PackedValueLength), SpatialFieldKind.XYShape, new ShapeVertex(i, i * -2), 0);
            xy.AppendValue(2, 0, xyPoints);

            using var supplementary = new ShapeDocValuesFieldBuffer("shape-😀", SpatialFieldKind.XYShape);
            supplementary.AppendValue(0, 0, EncodePoint(SpatialFieldKind.XYShape, new ShapeVertex(1, 1), 0));

            ShapeDocValuesWriter.Write(path, maxDoc: 3, new Dictionary<string, ShapeDocValuesFieldBuffer>
            {
                [geo.FieldName] = geo,
                [xy.FieldName] = xy,
                [supplementary.FieldName] = supplementary,
            });

            using ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path);
            Assert.Equal(["shape", "shape-prefix", "shape-😀"], reader.FieldNames);
            Assert.Equal(SpatialFieldKind.GeoShape, reader.GetFieldMetadata("shape").Kind);
            Assert.Equal(3, reader.GetFieldMetadata("shape").MaxDoc);

            Assert.True(reader.TryGetRecordMetadata("shape", 1, out ShapeDocValuesRecordMetadata geoRecord));
            Assert.Equal(2u, geoRecord.ValueCount);
            Assert.Equal((byte)1, (byte)(geoRecord.Flags & 1));
            Assert.True(geoRecord.Bound2 > geoRecord.Bound3);
            Assert.False(reader.TryGetRecordMetadata("shape", 2, out _));

            var geoPrimitives = new List<ShapePrimitive>();
            Assert.Equal(2, reader.VisitPrimitives("shape", 1, geoPrimitives.Add));
            Assert.Equal([0u, 1u], geoPrimitives.Select(static primitive => primitive.ValueOrdinal));

            Assert.True(reader.TryGetRecordMetadata("shape-prefix", 2, out ShapeDocValuesRecordMetadata xyRecord));
            Assert.Equal(17u, xyRecord.PrimitiveCount);
            Assert.Equal(SpatialDimension.Point, xyRecord.HighestDimension);
            Assert.Equal(17, reader.VisitPrimitives("shape-prefix", 2, static _ => { }));
            reader.DeepValidate();
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Metadata reads do not traverse the tree, while an explicit traversal validates it")]
    public void Reader_MetadataIsLazyAndTreeTraversalChecksNodeBounds()
    {
        string path = GetPath();
        try
        {
            using var field = new ShapeDocValuesFieldBuffer("shape", SpatialFieldKind.XYShape);
            field.AppendValue(0, 0, EncodePoint(SpatialFieldKind.XYShape, new ShapeVertex(3, 7), 0));
            ShapeDocValuesWriter.Write(path, maxDoc: 1, new Dictionary<string, ShapeDocValuesFieldBuffer>
            {
                [field.FieldName] = field,
            });

            long bodyStart;
            using (var input = new IndexInput(path))
            using (CodecReadSession session = CodecFileReader.Open(input, ShapeDocValuesCodecFiles.Data))
                bodyStart = session.Metadata.BodyStart;

            // Corrupt the tree's first bound while leaving the fixed record header and directory intact.
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            {
                stream.Position = bodyStart + 16 + 72 + 8;
                int value = stream.ReadByte();
                stream.Position--;
                stream.WriteByte((byte)(value ^ 0x01));
            }

            using ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path);
            Assert.True(reader.TryGetRecordMetadata("shape", 0, out _));
            Assert.Throws<InvalidDataException>(() => reader.VisitPrimitives("shape", 0, static _ => { }));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues rejects truncated frames and checksum mismatches")]
    public void Reader_RejectsTruncatedFramesAndChecksumMismatches()
    {
        string path = GetPath();
        try
        {
            WriteSinglePointField(path);
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                stream.SetLength(stream.Length - 1);

            Assert.ThrowsAny<IOException>(() => ShapeDocValuesReader.Open(path));

            WriteSinglePointField(path);
            (long bodyStart, byte[] body) = ReadFrameBody(path);
            WriteByte(path, bodyStart + 16 + 72 + 8, (byte)(body[16 + 72 + 8] ^ 1));

            using ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path);
            Assert.Throws<InvalidDataException>(reader.DeepValidate);
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues rejects hostile directory counts and offsets")]
    public void Reader_RejectsHostileDirectoryCountsAndOffsets()
    {
        string path = GetPath();
        try
        {
            WriteSinglePointField(path);
            (long bodyStart, byte[] body) = ReadFrameBody(path);
            long directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(body.Length - 8));
            WriteUInt32(path, bodyStart + directoryOffset, uint.MaxValue);
            WriteUInt32(path, bodyStart + body.Length - 12, uint.MaxValue);
            Assert.Throws<InvalidDataException>(() => ShapeDocValuesReader.Open(path));

            WriteSinglePointField(path);
            (bodyStart, body) = ReadFrameBody(path);
            directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(body.Length - 8));
            long sectionOffsetPosition = bodyStart + directoryOffset + sizeof(uint) + 1 + "shape".Length;
            WriteInt64(path, sectionOffsetPosition, long.MaxValue);
            Assert.Throws<InvalidDataException>(() => ShapeDocValuesReader.Open(path));

            WriteSinglePointField(path);
            (bodyStart, body) = ReadFrameBody(path);
            directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(body.Length - 8));
            long sectionLengthPosition = bodyStart + directoryOffset + sizeof(uint) + 1 + "shape".Length + sizeof(long);
            WriteInt64(path, sectionLengthPosition, long.MaxValue);
            Assert.Throws<InvalidDataException>(() => ShapeDocValuesReader.Open(path));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues rejects hostile field and record counts and offsets")]
    public void Reader_RejectsHostileFieldAndRecordMetadata()
    {
        string path = GetPath();
        try
        {
            WriteSinglePointField(path);
            (long bodyStart, byte[] body) = ReadFrameBody(path);
            WriteUInt32(path, bodyStart + 8, uint.MaxValue);
            using (ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path))
                Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("shape"));

            WriteSinglePointField(path);
            (bodyStart, body) = ReadFrameBody(path);
            WriteUInt32(path, bodyStart + 12, uint.MaxValue);
            using (ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path))
                Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("shape"));

            WriteSinglePointField(path);
            (bodyStart, body) = ReadFrameBody(path);
            long directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(body.Length - 8));
            long recordDirectoryOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(checked((int)directoryOffset - 8), 8));
            WriteInt64(path, bodyStart + recordDirectoryOffset + sizeof(uint), long.MaxValue);
            using (ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path))
                Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("shape"));

            WriteSinglePointField(path);
            (bodyStart, body) = ReadFrameBody(path);
            int sectionLength = checked((int)BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(body.Length - 8)));
            WriteUInt32(path, bodyStart + sectionLength - 12, 2);
            using (ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path))
                Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("shape"));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues rejects invalid leaf counts and excessive tree depth")]
    public void Reader_RejectsInvalidLeafCountsAndExcessiveTreeDepth()
    {
        string path = GetPath();
        try
        {
            WriteSinglePointField(path);
            (long bodyStart, _) = ReadFrameBody(path);
            long treeStart = bodyStart + 16 + 72;
            WriteUInt16(path, treeStart + 24, 17);
            using (ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path))
                Assert.Throws<InvalidDataException>(() => reader.VisitPrimitives("shape", 0, static _ => { }));

            byte[] primitive = EncodePoint(SpatialFieldKind.XYShape, new ShapeVertex(2, -1), 0);
            byte[] deepTree = BuildTreeExceedingDepthLimit(primitive, internalDepth: 65);
            const int primitiveCount = 256;
            byte[] primitives = new byte[primitiveCount * ShapePrimitiveCodec.PackedValueLength];
            for (int i = 0; i < primitiveCount; i++)
                primitive.CopyTo(primitives, i * ShapePrimitiveCodec.PackedValueLength);

            WriteManyPointField(path, primitives);
            (bodyStart, _) = ReadFrameBody(path);
            treeStart = bodyStart + 16 + 72;
            WriteUInt32(path, bodyStart + 16 + 4, 66);
            WriteBytes(path, treeStart, deepTree);
            using (ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path))
                Assert.Throws<InvalidDataException>(() => reader.VisitPrimitives("shape", 0, static _ => { }));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues disposal is idempotent and prevents later reads")]
    public void Reader_DisposeIsIdempotentAndPreventsLaterReads()
    {
        string path = GetPath();
        try
        {
            WriteSinglePointField(path);
            ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path);

            reader.Dispose();
            reader.Dispose();

            Assert.Throws<ObjectDisposedException>(() => reader.GetFieldMetadata("shape"));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues rejects bad top-level and field magic")]
    public void Reader_RejectsTopLevelAndFieldMagic()
    {
        string path = GetPath();
        try
        {
            WriteSinglePointField(path);
            (long bodyStart, byte[] body) = ReadFrameBody(path);
            WriteByte(path, bodyStart + body.Length - 16, (byte)'X');
            Assert.Throws<InvalidDataException>(() => ShapeDocValuesReader.Open(path));

            WriteSinglePointField(path);
            (bodyStart, body) = ReadFrameBody(path);
            WriteByte(path, bodyStart, (byte)'X');
            using (ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path))
                Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("shape"));

            WriteSinglePointField(path);
            (bodyStart, body) = ReadFrameBody(path);
            long sectionLength = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(body.Length - 8));
            WriteByte(path, bodyStart + sectionLength - 16, (byte)'X');
            using (ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path))
                Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("shape"));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues rejects invalid field coordinate systems, flags and reserved bytes")]
    public void Reader_RejectsInvalidFieldHeaderMetadata()
    {
        string path = GetPath();
        try
        {
            AssertFieldMetadataRejected(path, static (file, start) => WriteByte(file, start + 4, 2));
            AssertFieldMetadataRejected(path, static (file, start) => WriteByte(file, start, 2));
            AssertFieldMetadataRejected(path, static (file, start) => WriteByte(file, start + 5, 1));
            AssertFieldMetadataRejected(path, static (file, start) => WriteUInt16(file, start + 6, 1));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues rejects duplicate or unsorted record IDs and overlapping records")]
    public void Reader_RejectsUnorderedAndOverlappingRecordDirectories()
    {
        string path = GetPath();
        try
        {
            AssertRecordDirectoryRejected(path, static (file, bodyStart, firstEntry, secondEntry) =>
                WriteUInt32(file, bodyStart + secondEntry, 0));
            AssertRecordDirectoryRejected(path, static (file, bodyStart, firstEntry, secondEntry) =>
            {
                WriteUInt32(file, bodyStart + firstEntry, 1);
                WriteUInt32(file, bodyStart + secondEntry, 0);
            });
            AssertRecordDirectoryRejected(path, static (file, bodyStart, firstEntry, secondEntry) =>
            {
                long firstRecordOffset = ReadInt64(file, bodyStart + firstEntry + sizeof(uint));
                WriteInt64(file, bodyStart + secondEntry + sizeof(uint), firstRecordOffset);
            });
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues rejects invalid record dimensions flags reserved bytes accumulators and tree lengths")]
    public void Reader_RejectsInvalidRecordHeaderMetadata()
    {
        string path = GetPath();
        try
        {
            AssertRecordMetadataRejected(path, static (file, recordStart) => WriteByte(file, recordStart + 8, 3));
            AssertRecordMetadataRejected(path, static (file, recordStart) => WriteByte(file, recordStart + 9, 1));
            AssertRecordMetadataRejected(path, static (file, recordStart) => WriteUInt16(file, recordStart + 10, 1));
            AssertRecordMetadataRejected(path, static (file, recordStart) =>
                WriteInt64(file, recordStart + 36, BitConverter.DoubleToInt64Bits(double.NaN)));
            AssertRecordMetadataRejected(path, static (file, recordStart) => WriteUInt32(file, recordStart + 68, 57));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    [Fact(DisplayName = "Shape DocValues rejects malformed node headers counts and primitive encodings")]
    public void Reader_RejectsMalformedTreeNodesAndPrimitives()
    {
        string path = GetPath();
        try
        {
            AssertTreeRejected(path, static (file, treeStart) => WriteUInt32(file, treeStart, 55));
            AssertTreeRejected(path, static (file, treeStart) => WriteByte(file, treeStart + 4, 2));
            AssertTreeRejected(path, static (file, treeStart) => WriteByte(file, treeStart + 5, 1));
            AssertTreeRejected(path, static (file, treeStart) => WriteUInt16(file, treeStart + 26, 1));
            AssertTreeRejected(path, static (file, treeStart) =>
            {
                uint metadata = ReadUInt32BigEndian(file, treeStart + 28 + 24);
                WriteUInt32BigEndian(file, treeStart + 28 + 24, (metadata & ~7u) | 7u);
            });
            AssertTreeRejected(path, static (file, treeStart) => WriteUInt32(file, treeStart - 72 + 4, 2));

            var points = new byte[17 * ShapePrimitiveCodec.PackedValueLength];
            for (int i = 0; i < 17; i++)
                EncodePoint(points.AsSpan(i * ShapePrimitiveCodec.PackedValueLength), SpatialFieldKind.XYShape, new ShapeVertex(i, i * 2), 0);
            WriteManyPointField(path, points);
            (long bodyStart, byte[] body) = ReadFrameBody(path);
            long recordDirectoryOffset = GetRecordDirectoryOffset(body);
            long recordOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(checked((int)recordDirectoryOffset + sizeof(uint)), sizeof(long)));
            long treeStart = bodyStart + recordOffset + 72;
            WriteByte(path, treeStart + 8, (byte)(ReadByte(path, treeStart + 8) ^ 1));
            using (ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path))
                Assert.Throws<InvalidDataException>(() => reader.VisitPrimitives("shape", 0, static _ => { }));

            WriteManyPointField(path, points);
            (bodyStart, body) = ReadFrameBody(path);
            recordDirectoryOffset = GetRecordDirectoryOffset(body);
            recordOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(checked((int)recordDirectoryOffset + sizeof(uint)), sizeof(long)));
            treeStart = bodyStart + recordOffset + 72;
            WriteUInt32(path, treeStart + 24, uint.MaxValue);
            using (ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path))
                Assert.Throws<InvalidDataException>(() => reader.VisitPrimitives("shape", 0, static _ => { }));
        }
        finally
        {
            DeleteFile(path);
        }
    }

    private static byte[] EncodePoint(SpatialFieldKind kind, ShapeVertex point, uint valueOrdinal)
    {
        byte[] bytes = new byte[ShapePrimitiveCodec.PackedValueLength];
        EncodePoint(bytes, kind, point, valueOrdinal);
        return bytes;
    }

    private static void EncodePoint(Span<byte> bytes, SpatialFieldKind kind, ShapeVertex point, uint valueOrdinal)
        => ShapePrimitiveCodec.EncodePoint(bytes, kind, point, valueOrdinal);

    private static void WriteSinglePointField(string path)
    {
        using var field = new ShapeDocValuesFieldBuffer("shape", SpatialFieldKind.XYShape);
        field.AppendValue(0, 0, EncodePoint(SpatialFieldKind.XYShape, new ShapeVertex(2, -1), 0));
        ShapeDocValuesWriter.Write(path, maxDoc: 1, new Dictionary<string, ShapeDocValuesFieldBuffer>
        {
            [field.FieldName] = field,
        });
    }

    private static void WriteManyPointField(string path, byte[] primitives)
    {
        using var field = new ShapeDocValuesFieldBuffer("shape", SpatialFieldKind.XYShape);
        field.AppendValue(0, 0, primitives);
        ShapeDocValuesWriter.Write(path, maxDoc: 1, new Dictionary<string, ShapeDocValuesFieldBuffer>
        {
            [field.FieldName] = field,
        });
    }

    private static void WriteTwoPointField(string path)
    {
        using var field = new ShapeDocValuesFieldBuffer("shape", SpatialFieldKind.XYShape);
        field.AppendValue(0, 0, EncodePoint(SpatialFieldKind.XYShape, new ShapeVertex(2, -1), 0));
        field.AppendValue(1, 0, EncodePoint(SpatialFieldKind.XYShape, new ShapeVertex(3, -2), 0));
        ShapeDocValuesWriter.Write(path, maxDoc: 2, new Dictionary<string, ShapeDocValuesFieldBuffer>
        {
            [field.FieldName] = field,
        });
    }

    private static void AssertFieldMetadataRejected(string path, Action<string, long> mutate)
    {
        WriteSinglePointField(path);
        (long bodyStart, byte[] body) = ReadFrameBody(path);
        long sectionOffset = GetSingleFieldSectionOffset(body);
        mutate(path, bodyStart + sectionOffset);

        using ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path);
        Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("shape"));
    }

    private static void AssertRecordDirectoryRejected(
        string path,
        Action<string, long, long, long> mutate)
    {
        WriteTwoPointField(path);
        (long bodyStart, byte[] body) = ReadFrameBody(path);
        long recordDirectoryOffset = GetRecordDirectoryOffset(body);
        long firstEntry = recordDirectoryOffset;
        long secondEntry = firstEntry + 16;
        mutate(path, bodyStart, firstEntry, secondEntry);

        using ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path);
        Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("shape"));
    }

    private static void AssertRecordMetadataRejected(string path, Action<string, long> mutate)
    {
        WriteSinglePointField(path);
        (long bodyStart, byte[] body) = ReadFrameBody(path);
        long recordDirectoryOffset = GetRecordDirectoryOffset(body);
        long recordOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(checked((int)recordDirectoryOffset + sizeof(uint)), sizeof(long)));
        mutate(path, bodyStart + GetSingleFieldSectionOffset(body) + recordOffset);

        using ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path);
        Assert.Throws<InvalidDataException>(() => reader.TryGetRecordMetadata("shape", 0, out _));
    }

    private static void AssertTreeRejected(string path, Action<string, long> mutate)
    {
        WriteSinglePointField(path);
        (long bodyStart, byte[] body) = ReadFrameBody(path);
        long recordDirectoryOffset = GetRecordDirectoryOffset(body);
        long recordOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(checked((int)recordDirectoryOffset + sizeof(uint)), sizeof(long)));
        long treeStart = bodyStart + GetSingleFieldSectionOffset(body) + recordOffset + 72;
        mutate(path, treeStart);

        using ShapeDocValuesReader reader = ShapeDocValuesReader.Open(path);
        Assert.Throws<InvalidDataException>(() => reader.VisitPrimitives("shape", 0, static _ => { }));
    }

    private static long GetRecordDirectoryOffset(ReadOnlySpan<byte> body)
    {
        long sectionOffset = GetSingleFieldSectionOffset(body);
        long topDirectoryOffset = BinaryPrimitives.ReadInt64LittleEndian(body[^sizeof(long)..]);
        int nameLengthPosition = checked((int)topDirectoryOffset + sizeof(uint));
        int nameLength = body[nameLengthPosition];
        int sectionEntryPosition = nameLengthPosition + 1 + nameLength;
        long sectionLength = BinaryPrimitives.ReadInt64LittleEndian(body.Slice(sectionEntryPosition + sizeof(long), sizeof(long)));
        long fieldDirectoryOffset = BinaryPrimitives.ReadInt64LittleEndian(body.Slice(checked((int)(sectionOffset + sectionLength - sizeof(long))), sizeof(long)));
        return sectionOffset + fieldDirectoryOffset;
    }

    private static long GetSingleFieldSectionOffset(ReadOnlySpan<byte> body)
    {
        long topDirectoryOffset = BinaryPrimitives.ReadInt64LittleEndian(body[^sizeof(long)..]);
        int nameLengthPosition = checked((int)topDirectoryOffset + sizeof(uint));
        int nameLength = body[nameLengthPosition];
        int sectionEntryPosition = nameLengthPosition + 1 + nameLength;
        return BinaryPrimitives.ReadInt64LittleEndian(body.Slice(sectionEntryPosition, sizeof(long)));
    }

    private static (long BodyStart, byte[] Body) ReadFrameBody(string path)
    {
        using var input = new IndexInput(path);
        using CodecReadSession session = CodecFileReader.Open(input, ShapeDocValuesCodecFiles.Data);
        return (session.Metadata.BodyStart, session.ReadBody());
    }

    private static byte[] BuildTreeExceedingDepthLimit(ReadOnlySpan<byte> primitive, int internalDepth)
    {
        byte[] leaf = new byte[56];
        BinaryPrimitives.WriteUInt32LittleEndian(leaf, (uint)leaf.Length);
        leaf[4] = 1;
        primitive[..16].CopyTo(leaf.AsSpan(8));
        BinaryPrimitives.WriteUInt16LittleEndian(leaf.AsSpan(24), 1);
        primitive.CopyTo(leaf.AsSpan(28));

        byte[] tree = leaf;
        for (int depth = 0; depth < internalDepth; depth++)
        {
            byte[] parent = new byte[28 + tree.Length + leaf.Length];
            BinaryPrimitives.WriteUInt32LittleEndian(parent, (uint)parent.Length);
            primitive[..16].CopyTo(parent.AsSpan(8));
            BinaryPrimitives.WriteUInt32LittleEndian(parent.AsSpan(24), (uint)tree.Length);
            tree.CopyTo(parent.AsSpan(28));
            leaf.CopyTo(parent.AsSpan(28 + tree.Length));
            tree = parent;
        }
        return tree;
    }

    private static void WriteByte(string path, long offset, byte value)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        stream.Position = offset;
        stream.WriteByte(value);
    }

    private static void WriteUInt16(string path, long offset, ushort value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
        WriteBytes(path, offset, bytes);
    }

    private static void WriteUInt32(string path, long offset, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        WriteBytes(path, offset, bytes);
    }

    private static void WriteUInt32BigEndian(string path, long offset, uint value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        WriteBytes(path, offset, bytes);
    }

    private static uint ReadUInt32BigEndian(string path, long offset)
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        ReadBytes(path, offset, bytes);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static void WriteInt64(string path, long offset, long value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        WriteBytes(path, offset, bytes);
    }

    private static void WriteBytes(string path, long offset, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        stream.Position = offset;
        stream.Write(bytes);
    }

    private static byte ReadByte(string path, long offset)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Position = offset;
        int value = stream.ReadByte();
        if (value < 0)
            throw new EndOfStreamException();
        return (byte)value;
    }

    private static long ReadInt64(string path, long offset)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        ReadBytes(path, offset, bytes);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    private static void ReadBytes(string path, long offset, Span<byte> bytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Position = offset;
        stream.ReadExactly(bytes);
    }

    private static uint ReadDimension(ReadOnlySpan<byte> primitive, int dimension)
        => BinaryPrimitives.ReadUInt32BigEndian(primitive.Slice(dimension * 4, 4));

    private static string GetPath()
        => Path.Combine(Path.GetTempPath(), $"lc_shape_docvalues_{Guid.NewGuid():N}.dvg");

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
}
