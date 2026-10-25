using System.Buffers.Binary;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class PackedBkdReaderTests
{
    [Fact(DisplayName = "Packed BKD v1 accepts one-point leaves while shared writer configuration retains the 1D minimum")]
    public void LeafSizeContracts_RemainDistinct()
    {
        Assert.Equal(1, PackedBkdConfig.Point2D(maxPointsPerLeaf: 1).MaxPointsPerLeaf);
        var writerConfig = new IndexWriterConfig { BKDMaxLeafSize = 1 };
        Assert.Throws<ArgumentException>(writerConfig.Validate);
    }

    [Fact(DisplayName = "Packed BKD metadata exposes exact indexed root bounds")]
    public void Reader_ReportsExactRootBounds()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "bounds.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 2));
            buffer.Append([0, 0, 0, 10, 0, 0, 0, 20], 0);
            buffer.Append([0, 0, 0, 5, 0, 0, 0, 25], 1);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            using var reader = PackedBkdReader.Open(path);
            var metadata = reader.GetFieldMetadata("location");
            Assert.Equal([0, 0, 0, 5, 0, 0, 0, 20], metadata.RootMinimum.ToArray());
            Assert.Equal([0, 0, 0, 10, 0, 0, 0, 25], metadata.RootMaximum.ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD legal leaf sizes preserve intersection results")]
    public void Reader_LegalLeafSizesPreserveQueryResults()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            var expected = new HashSet<int> { 2, 3, 4, 5 };
            foreach (int leafSize in new[] { 1, 3, 4096 })
            {
                string path = Path.Combine(directory, $"leaf-results-{leafSize}.pbkd");
                using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(leafSize));
                for (int document = 0; document < 8; document++)
                    PackedBkdTestSupport.AppendPoint(buffer, document, document, document);
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
                using var reader = PackedBkdReader.Open(path);
                var visitor = new PackedBkdTestSupport.RangeVisitor(new XYPoint(2, 2), new XYPoint(5, 5));
                Assert.True(reader.Intersect("location", ref visitor));
                Assert.Equal(expected.Order(), visitor.Documents.ToHashSet().Order());
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD reads the required seven-dimensional configuration")]
    public void Reader_HandlesSevenDimensionsAndInsideVisits()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "seven.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Shape7D4Indexed(maxPointsPerLeaf: 2));
            byte[] packed = new byte[28];
            for (int docId = 0; docId < 3; docId++)
            {
                for (int dimension = 0; dimension < 7; dimension++)
                    XYEncodingUtils.Encode(docId + dimension, packed.AsSpan(dimension * 4, 4));
                buffer.Append(packed, docId);
            }

            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["shape"] = buffer });
            using var reader = PackedBkdReader.Open(path);
            var metadata = reader.GetFieldMetadata("shape");
            Assert.Equal(7, metadata.Config.Dimensions);
            Assert.Equal(4, metadata.Config.IndexedDimensions);
            var visitor = new PackedBkdTestSupport.VisitAllVisitor();
            Assert.True(reader.Intersect("shape", ref visitor));
            Assert.Equal([0, 1, 2], visitor.Documents.Order());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD defers checksum validation until deep validation")]
    public void Reader_DefersChecksumValidationUntilDeepValidation()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            byte[] bytes = File.ReadAllBytes(path);
            int bodyOffset;
            using (var input = new IndexInput(path))
            using (var session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true))
                bodyOffset = checked((int)session.Metadata.BodyStart);
            bytes[bodyOffset + 32] ^= 1;
            File.WriteAllBytes(path, bytes);

            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<CodecFileException>(() => reader.DeepValidate());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD metadata ignores an unrelated corrupt leaf payload")]
    public void Reader_MetadataDoesNotDecodeUnrelatedCorruptLeaf()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "lazy-metadata.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            byte[] body = PackedBkdTestSupport.ReadBody(path);
            int leafStart = GetLeafStart(body, indexedBytes: 8, leafIndex: 4);
            body[leafStart + 2] = sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(leafStart + 4, sizeof(int)), int.MaxValue);
            PackedBkdTestSupport.RewriteBody(path, body);

            using var reader = PackedBkdReader.Open(path);
            Assert.Equal(10, reader.GetFieldMetadata("location").PointCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD outside leaf comparison skips corrupt payload decoding")]
    public void Reader_OutsideLeafDoesNotDecodePayload()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "lazy-query.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            byte[] body = PackedBkdTestSupport.ReadBody(path);
            int leafStart = GetLeafStart(body, indexedBytes: 8, leafIndex: 1);
            body[leafStart + 2] = sizeof(int);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(leafStart + 4, sizeof(int)), int.MaxValue);
            PackedBkdTestSupport.RewriteBody(path, body);

            using var reader = PackedBkdReader.Open(path);
            var visitor = new PackedBkdTestSupport.RangeVisitor(new XYPoint(0, 0), new XYPoint(1, 1));
            Assert.True(reader.Intersect("location", ref visitor));
            Assert.Equal([0, 1], visitor.Documents.Order());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD selective traversal reports only touched leaves and values")]
    public void Reader_SelectiveTraversalDoesNotDecodeUnvisitedLeaves()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "selective-stats.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            using var reader = PackedBkdReader.Open(path);
            var metadata = reader.GetFieldMetadata("location");
            var visitor = new PackedBkdTestSupport.RangeVisitor(new XYPoint(0, 0), new XYPoint(1, 1));
            Assert.True(reader.Intersect("location", ref visitor, out var stats));

            Assert.True(stats.LeavesVisited < metadata.LeafCount);
            Assert.True(stats.LeavesSemanticallyValidated < metadata.LeafCount);
            Assert.True(stats.PackedValuesDecoded < metadata.PointCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects semantically corrupt bounds after checksum validation")]
    public void Reader_RejectsInvertedRootBounds()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt-bounds.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            byte[] body = PackedBkdTestSupport.ReadBody(path);
            body.AsSpan(32, 8).Fill(0xff);
            PackedBkdTestSupport.RewriteBody(path, body);
            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<InvalidDataException>(() => reader.DeepValidate());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects hostile directory counts before allocation")]
    public void Reader_RejectsHostileDirectoryCount()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "hostile-directory.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            byte[] body = PackedBkdTestSupport.ReadBody(path);
            long directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(body.Length - sizeof(long), sizeof(long)));
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(checked((int)directoryOffset), sizeof(int)), int.MaxValue);
            PackedBkdTestSupport.RewriteBody(path, body);
            Assert.Throws<InvalidDataException>(() => PackedBkdReader.Open(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects a corrupt footer before field allocation")]
    public void Reader_RejectsCorruptFooter()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt-footer.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            byte[] body = PackedBkdTestSupport.ReadBody(path);
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(body.Length - 16, sizeof(int)), 0);
            PackedBkdTestSupport.RewriteBody(path, body);
            Assert.Throws<InvalidDataException>(() => PackedBkdReader.Open(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects an invalid split dimension")]
    public void Reader_RejectsInvalidSplitDimension()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt-split.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            byte[] body = PackedBkdTestSupport.ReadBody(path);
            int splitCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(28, sizeof(int)));
            Assert.True(splitCount > 0);
            body[32 + 2 * 4 * 2] = byte.MaxValue;
            PackedBkdTestSupport.RewriteBody(path, body);
            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<InvalidDataException>(() => reader.DeepValidate());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects an invalid leaf offset")]
    public void Reader_RejectsInvalidLeafOffset()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt-offset.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            byte[] body = PackedBkdTestSupport.ReadBody(path);
            int splitCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(28, sizeof(int)));
            int offsetsStart = 32 + 2 * 4 * 2 + splitCount * (1 + 4);
            BinaryPrimitives.WriteInt64LittleEndian(body.AsSpan(offsetsStart, sizeof(long)), 1);
            PackedBkdTestSupport.RewriteBody(path, body);
            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("location"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects an invalid leaf encoding")]
    public void Reader_RejectsInvalidLeafEncoding()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt-leaf.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            byte[] body = PackedBkdTestSupport.ReadBody(path);
            int leafCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(12, sizeof(int)));
            int splitCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(28, sizeof(int)));
            int leafDataStart = 32 + 2 * 4 * 2 + splitCount * (1 + 4) + (leafCount + 1) * sizeof(long);
            body[leafDataStart + 3] = byte.MaxValue;
            PackedBkdTestSupport.RewriteBody(path, body);
            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<InvalidDataException>(() => reader.DeepValidate());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD keeps a valid earlier file readable after another file fails")]
    public void Reader_ValidEarlierFileRemainsReadableAfterOtherFailure()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string validPath = Path.Combine(directory, "valid.pbkd");
            string invalidPath = Path.Combine(directory, "invalid.pbkd");
            using (var valid = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(validPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = valid });
            using (var invalid = PackedBkdTestSupport.CreateBuffer(reverse: true))
                PackedBkdWriter.Write(invalidPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = invalid });
            byte[] invalidBody = PackedBkdTestSupport.ReadBody(invalidPath);
            BinaryPrimitives.WriteInt32LittleEndian(invalidBody.AsSpan(invalidBody.Length - 16, sizeof(int)), 0);
            PackedBkdTestSupport.RewriteBody(invalidPath, invalidBody);
            Assert.Throws<InvalidDataException>(() => PackedBkdReader.Open(invalidPath));
            using var reader = PackedBkdReader.Open(validPath);
            Assert.Equal(10, reader.GetFieldMetadata("location").PointCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects an inverted non-zero root dimension")]
    public void Reader_RejectsInvertedRootDimensionOne()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt-root-dimension.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            byte[] body = PackedBkdTestSupport.ReadBody(path);
            body.AsSpan(32 + 4, 4).Fill(byte.MaxValue);
            body.AsSpan(32 + 8 + 4, 4).Clear();
            PackedBkdTestSupport.RewriteBody(path, body);

            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("location"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects an inverted non-zero leaf dimension")]
    public void Reader_RejectsInvertedLeafDimensionOne()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt-leaf-dimension.pbkd");
            using (var buffer = PackedBkdTestSupport.CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            byte[] body = PackedBkdTestSupport.ReadBody(path);
            int leafStart = GetLeafStart(body, indexedBytes: 8, leafIndex: 0);
            int minimumOffset = leafStart + 8;
            int maximumOffset = minimumOffset + 8;
            body.AsSpan(minimumOffset + 4, 4).Fill(byte.MaxValue);
            body.AsSpan(maximumOffset + 4, 4).Clear();
            PackedBkdTestSupport.RewriteBody(path, body);

            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<InvalidDataException>(() => reader.DeepValidate());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory(DisplayName = "Packed BKD rejects inverted seven-dimensional indexed bounds")]
    [InlineData(2)]
    [InlineData(3)]
    public void Reader_RejectsInvertedSevenDimensionalRootDimension(int dimension)
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, $"corrupt-seven-dimension-{dimension}.pbkd");
            using (var buffer = CreateSevenDimensionalBuffer())
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["shape"] = buffer });
            byte[] body = PackedBkdTestSupport.ReadBody(path);
            int minimumOffset = 32 + dimension * 4;
            int maximumOffset = 32 + 16 + dimension * 4;
            body.AsSpan(minimumOffset, 4).Fill(byte.MaxValue);
            body.AsSpan(maximumOffset, 4).Clear();
            PackedBkdTestSupport.RewriteBody(path, body);

            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("shape"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects a split outside its parent in a non-zero dimension")]
    public void Reader_RejectsSplitOutsideParentDimensionOne()
    {
        string directory = PackedBkdTestSupport.CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt-split-dimension.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Point2D(maxPointsPerLeaf: 1));
            PackedBkdTestSupport.AppendPoint(buffer, 0, 0, 0);
            PackedBkdTestSupport.AppendPoint(buffer, 0, 1, 1);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            byte[] body = PackedBkdTestSupport.ReadBody(path);
            Assert.Equal(1, body[48]);
            body.AsSpan(32 + 16 + 1 + 4, 4).Fill(byte.MaxValue);
            PackedBkdTestSupport.RewriteBody(path, body);

            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<InvalidDataException>(() => reader.DeepValidate());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int GetLeafStart(byte[] body, int indexedBytes, int leafIndex)
    {
        int leafCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(12, sizeof(int)));
        int splitCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(28, sizeof(int)));
        int offsetsStart = checked(32 + indexedBytes * 2 + splitCount * (1 + 4));
        int leafDataStart = checked(offsetsStart + (leafCount + 1) * sizeof(long));
        long leafOffset = BinaryPrimitives.ReadInt64LittleEndian(
            body.AsSpan(checked(offsetsStart + leafIndex * sizeof(long)), sizeof(long)));
        return checked(leafDataStart + (int)leafOffset);
    }

    private static PackedBkdFieldBuffer CreateSevenDimensionalBuffer()
    {
        var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Shape7D4Indexed(maxPointsPerLeaf: 2));
        byte[] packed = new byte[28];
        for (int document = 0; document < 3; document++)
        {
            for (int dimension = 0; dimension < 7; dimension++)
                XYEncodingUtils.Encode(document + dimension, packed.AsSpan(dimension * 4, 4));
            buffer.Append(packed, document);
        }
        return buffer;
    }
}
