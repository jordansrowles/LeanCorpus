using System.Buffers.Binary;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class PackedBkdTests
{
    [Fact(DisplayName = "Packed BKD v1 body has locked golden bytes")]
    public void Writer_UsesLockedV1GoldenBytes()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "golden.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2));
            Span<byte> first = stackalloc byte[8];
            Span<byte> second = stackalloc byte[8];
            XYEncodingUtils.Encode(0, first[..4]);
            XYEncodingUtils.Encode(0, first[4..]);
            XYEncodingUtils.Encode(1, second[..4]);
            XYEncodingUtils.Encode(1, second[4..]);
            buffer.Append(first, 0);
            buffer.Append(second, 1);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            Assert.Equal(
                "50424631020204000200000001000000020000000000000002000000000000008000000080000000BF800000BF80000000000000000000002A0000000000000002000100000000008000000080000000BF800000BF80000000018000000080000000BF800000BF80000001000000086C6F636174696F6E00000000000000006A0000000000000050424B44010000006A00000000000000",
                Convert.ToHexString(ReadBody(path)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory(DisplayName = "Packed BKD document IDs use the smallest delta width")]
    [InlineData(0, 0)]
    [InlineData(255, 1)]
    [InlineData(256, 2)]
    [InlineData(65_536, 3)]
    [InlineData(16_777_216, 4)]
    public void Writer_UsesSmallestDocumentWidth(int secondDocument, int expectedWidth)
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "doc-width.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2));
            AppendPoint(buffer, 0, 0, 0);
            AppendPoint(buffer, 1, 1, secondDocument);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            var header = ReadFirstLeafHeader(path);
            Assert.Equal(expectedWidth, header.DocumentWidth);
            Assert.Equal(0, header.MinimumDocument);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD selects raw and prefix leaf encodings deterministically")]
    public void Writer_SelectsRawAndPrefixLeafEncodings()
    {
        string directory = CreateDirectory();
        try
        {
            string rawPath = Path.Combine(directory, "raw.pbkd");
            using (var raw = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2)))
            {
                AppendPoint(raw, -1, -1, 0);
                AppendPoint(raw, 1, 1, 1);
                PackedBkdWriter.Write(rawPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = raw });
            }

            string prefixPath = Path.Combine(directory, "prefix.pbkd");
            using (var prefix = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2)))
            {
                AppendPoint(prefix, 0, 0, 0);
                AppendPoint(prefix, float.BitIncrement(0), float.BitIncrement(0), 1);
                PackedBkdWriter.Write(prefixPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = prefix });
            }

            Assert.Equal(0, ReadFirstLeafHeader(rawPath).Encoding);
            Assert.Equal(1, ReadFirstLeafHeader(prefixPath).Encoding);

            string tiePath = Path.Combine(directory, "tie.pbkd");
            using (var tie = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2)))
            {
                tie.Append([0, 0, 0, 0, 0, 0, 0, 0], 0);
                tie.Append([0, 1, 0, 0, 0, 1, 0, 0], 1);
                PackedBkdWriter.Write(tiePath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = tie });
            }

            Assert.Equal(0, ReadFirstLeafHeader(tiePath).Encoding);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD uses common prefixes for identical values")]
    public void Writer_UsesPrefixEncodingForIdenticalValues()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "identical.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2));
            buffer.Append([0, 1, 2, 3, 4, 5, 6, 7], 0);
            buffer.Append([0, 1, 2, 3, 4, 5, 6, 7], 1);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
            Assert.Equal(1, ReadFirstLeafHeader(path).Encoding);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD metadata exposes exact indexed root bounds")]
    public void Reader_ReportsExactRootBounds()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "bounds.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2));
            buffer.Append([0, 0, 0, 10, 0, 0, 0, 20], 0);
            buffer.Append([0, 0, 0, 5, 0, 0, 0, 25], 1);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            using var reader = PackedBkdReader.Open(path);
            var metadata = reader.GetFieldMetadata("location");
            Assert.Equal([0, 0, 0, 5, 0, 0, 0, 20], metadata.RootMinimum);
            Assert.Equal([0, 0, 0, 10, 0, 0, 0, 25], metadata.RootMaximum);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD legal leaf sizes preserve intersection results")]
    public void Reader_LegalLeafSizesPreserveQueryResults()
    {
        string directory = CreateDirectory();
        try
        {
            var expected = new HashSet<int> { 2, 3, 4, 5 };
            foreach (int leafSize in new[] { 1, 3, 4096 })
            {
                string path = Path.Combine(directory, $"leaf-results-{leafSize}.pbkd");
                using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(leafSize));
                for (int document = 0; document < 8; document++)
                    AppendPoint(buffer, document, document, document);
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
                using var reader = PackedBkdReader.Open(path);
                var visitor = new RangeVisitor(new XYPoint(2, 2), new XYPoint(5, 5));
                Assert.True(reader.Intersect("location", visitor));
                Assert.Equal(expected.Order(), visitor.Documents.ToHashSet().Order());
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD supports the legal leaf size extremes")]
    public void Writer_SupportsLeafSizeExtremes()
    {
        string directory = CreateDirectory();
        try
        {
            foreach (int leafSize in new[] { 1, 4096 })
            {
                string path = Path.Combine(directory, $"leaf-{leafSize}.pbkd");
                using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: leafSize));
                for (int document = 0; document < 3; document++)
                    AppendPoint(buffer, document, document, document);
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });
                using var reader = PackedBkdReader.Open(path);
                Assert.Equal(leafSize == 1 ? 3 : 1, reader.GetFieldMetadata("location").LeafCount);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "DWPT packed BKD capacity is tracked without metadata enumeration")]
    public void DocumentsWriterTracksPackedBkdCapacity()
    {
        var dwpt = new DocumentsWriterPerThread(
            new WhitespaceAnalyser(),
            new Dictionary<string, IAnalyser>(),
            new IndexWriterConfig { DurableCommits = false });
        try
        {
            long before = dwpt.EstimatedRamBytes;
            Span<byte> packed = stackalloc byte[8];
            XYEncodingUtils.Encode(12, packed[..4]);
            XYEncodingUtils.Encode(34, packed[4..]);
            dwpt.AddPackedBkdValue("location", PackedBkdConfig.Geo2D(), packed, 0);
            Assert.True(dwpt.EstimatedRamBytes > before);
        }
        finally
        {
            dwpt.Dispose();
        }
        Assert.Equal(0, dwpt.EstimatedRamBytes);
    }

    [Fact(DisplayName = "Packed BKD writes deterministic multidimensional fields")]
    public void WriterReader_RoundTripsAndIsDeterministic()
    {
        string directory = CreateDirectory();
        try
        {
            var first = CreateBuffer(reverse: false);
            var second = CreateBuffer(reverse: true);
            string firstPath = Path.Combine(directory, "first.pbkd");
            string secondPath = Path.Combine(directory, "second.pbkd");
            try
            {
                PackedBkdWriter.Write(firstPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = first });
                PackedBkdWriter.Write(secondPath, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = second });
                Assert.Equal(File.ReadAllBytes(firstPath), File.ReadAllBytes(secondPath));

                using var reader = PackedBkdReader.Open(firstPath);
                var metadata = reader.GetFieldMetadata("location");
                Assert.Equal(10, metadata.PointCount);
                Assert.Equal(10, metadata.DocumentCount);
                Assert.Equal(2, metadata.Config.Dimensions);
                Assert.True(metadata.LeafCount > 1);

                var visitor = new RangeVisitor(new XYPoint(2, 2), new XYPoint(7, 7));
                Assert.True(reader.Intersect("location", visitor));
                Assert.Equal(Enumerable.Range(2, 6), visitor.Documents.Order());
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD spill files are cleaned after success and failure")]
    public void Writer_SpillPathIsCleaned()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "spill.pbkd");
            using var buffer = CreateBuffer(reverse: false);
            PackedBkdWriter.Write(path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(1024, directory, ForceSpill: true));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.leaf"));
            Assert.True(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD in-memory and spill builds are byte-identical")]
    public void Writer_SpillBuildMatchesInMemoryBuild()
    {
        string directory = CreateDirectory();
        try
        {
            string memoryPath = Path.Combine(directory, "memory.pbkd");
            string spillPath = Path.Combine(directory, "spill.pbkd");
            using var memory = CreateBuffer(reverse: false);
            using var spill = CreateBuffer(reverse: false);
            PackedBkdWriter.Write(memoryPath, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["location"] = memory
            });
            PackedBkdWriter.Write(spillPath, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["location"] = spill
            }, new PackedBkdBuildOptions(1024, directory, ForceSpill: true));

            Assert.Equal(File.ReadAllBytes(memoryPath), File.ReadAllBytes(spillPath));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.leaf"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD writes multiple fields in UTF-8 ordinal order")]
    public void Writer_OrdersMultipleFieldsDeterministically()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "multiple-fields.pbkd");
            using var zeta = CreateBuffer(reverse: false);
            using var alpha = CreateBuffer(reverse: true);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["zeta"] = zeta,
                ["alpha"] = alpha
            });

            using var input = new IndexInput(path);
            using var session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true);
            using var body = session.OpenBodyInput();
            body.Seek(body.Length - 16);
            _ = body.ReadInt32();
            Assert.Equal(2, body.ReadInt32());
            long directoryOffset = body.ReadInt64();
            body.Seek(directoryOffset);
            Assert.Equal(2, body.ReadInt32());
            Assert.Equal("alpha", body.ReadLengthPrefixedString());
            _ = body.ReadInt64();
            _ = body.ReadInt64();
            Assert.Equal("zeta", body.ReadLengthPrefixedString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD cancellation removes spill artefacts")]
    public void Writer_CancellationCleansSpillArtifacts()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "cancelled.pbkd");
            using var buffer = CreateBuffer(reverse: false);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() => PackedBkdWriter.Write(
                path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(1024, directory, ForceSpill: true, CancellationToken: cancellation.Token)));
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.leaf"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD failed spill setup removes the output and preserves the source buffer")]
    public void Writer_FailedSpillSetupCleansOutput()
    {
        string directory = CreateDirectory();
        try
        {
            string blockedPath = Path.Combine(directory, "spill-blocker");
            File.WriteAllBytes(blockedPath, [1]);
            string path = Path.Combine(directory, "failed.pbkd");
            using var buffer = CreateBuffer(reverse: false);

            Assert.ThrowsAny<IOException>(() => PackedBkdWriter.Write(
                path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(1024, blockedPath, ForceSpill: true)));

            Assert.False(File.Exists(path));
            Assert.Equal(10, buffer.Count);
            buffer.Append(buffer.Records[..8], 10);
            Assert.Equal(11, buffer.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD defers checksum validation until field access")]
    public void Reader_RejectsChecksumMismatchOnFieldAccess()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt.pbkd");
            using (var buffer = CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            byte[] bytes = File.ReadAllBytes(path);
            int bodyOffset;
            using (var input = new IndexInput(path))
            using (var session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true))
                bodyOffset = checked((int)session.Metadata.BodyStart);
            bytes[bodyOffset + 32] ^= 1;
            File.WriteAllBytes(path, bytes);

            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<CodecFileException>(() => reader.GetFieldMetadata("location"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects semantically corrupt bounds after checksum validation")]
    public void Reader_RejectsInvertedRootBounds()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "corrupt-bounds.pbkd");
            using (var buffer = CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            byte[] body = ReadBody(path);
            body.AsSpan(32, 8).Fill(0xff);
            RewriteBody(path, body);

            using var reader = PackedBkdReader.Open(path);
            Assert.Throws<InvalidDataException>(() => reader.GetFieldMetadata("location"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects hostile directory counts before allocation")]
    public void Reader_RejectsHostileDirectoryCount()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "hostile-directory.pbkd");
            using (var buffer = CreateBuffer(reverse: false))
                PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer });

            byte[] body = ReadBody(path);
            long directoryOffset = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(body.Length - sizeof(long), sizeof(long)));
            BinaryPrimitives.WriteInt32LittleEndian(body.AsSpan(checked((int)directoryOffset), sizeof(int)), int.MaxValue);
            RewriteBody(path, body);

            Assert.Throws<InvalidDataException>(() => PackedBkdReader.Open(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD rejects an impossible build budget without leaving spill files")]
    public void Writer_RejectsInsufficientBuildBudgetWithoutSpill()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "insufficient-budget.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 4096));
            AppendPoint(buffer, 0, 0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => PackedBkdWriter.Write(
                path,
                new Dictionary<string, PackedBkdFieldBuffer> { ["location"] = buffer },
                new PackedBkdBuildOptions(1024, directory, ForceSpill: true)));
            Assert.False(File.Exists(path));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.spill"));
            Assert.Empty(Directory.EnumerateFiles(directory, "*.leaf"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD writes the v1 footer and relative leaf offsets")]
    public void Writer_WritesV1FooterAndRelativeLeafOffsets()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "layout.pbkd");
            using var buffer = CreateBuffer(reverse: false);
            PackedBkdWriter.Write(path, new Dictionary<string, PackedBkdFieldBuffer>
            {
                ["location"] = buffer
            });

            using var input = new IndexInput(path);
            using var session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true);
            session.ValidateChecksum();
            using var body = session.OpenBodyInput();

            body.Seek(body.Length - 16);
            Assert.Equal(unchecked((int)PackedBkdWriter.FooterMagic), body.ReadInt32());
            Assert.Equal(1, body.ReadInt32());
            long directoryOffset = body.ReadInt64();

            body.Seek(directoryOffset);
            Assert.Equal(1, body.ReadInt32());
            Assert.Equal("location", body.ReadLengthPrefixedString());
            long sectionOffset = body.ReadInt64();
            long sectionLength = body.ReadInt64();
            Assert.Equal(0, sectionOffset);

            body.Seek(sectionOffset);
            Assert.Equal(unchecked((int)0x3146_4250), body.ReadInt32());
            int dimensions = body.ReadByte();
            int indexedDimensions = body.ReadByte();
            int bytesPerDimension = body.ReadByte();
            Assert.Equal(2, dimensions);
            Assert.Equal(2, indexedDimensions);
            Assert.Equal(4, bytesPerDimension);
            Assert.Equal(0, body.ReadByte());
            int maxPointsPerLeaf = body.ReadByte() | (body.ReadByte() << 8);
            Assert.Equal(0, body.ReadByte());
            Assert.Equal(0, body.ReadByte());
            int leafCount = body.ReadInt32();
            _ = body.ReadInt64();
            _ = body.ReadInt32();
            int splitCount = body.ReadInt32();
            body.Seek(body.Position + indexedDimensions * bytesPerDimension * 2L + splitCount * (1L + bytesPerDimension));
            long leafDataOffset = body.Position + (leafCount + 1L) * sizeof(long);
            long firstLeafOffset = body.ReadInt64();
            body.Seek(body.Position + leafCount * sizeof(long) - sizeof(long));
            long finalLeafOffset = body.ReadInt64();

            Assert.Equal(2, maxPointsPerLeaf);
            Assert.Equal(leafCount - 1, splitCount);
            Assert.Equal(0, firstLeafOffset);
            Assert.Equal(sectionLength - leafDataOffset, finalLeafOffset);
            Assert.True(finalLeafOffset > 0);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD reads the required seven-dimensional configuration")]
    public void Reader_HandlesSevenDimensionsAndInsideVisits()
    {
        string directory = CreateDirectory();
        try
        {
            string path = Path.Combine(directory, "seven.pbkd");
            using var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.SevenDimensional(maxPointsPerLeaf: 2));
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
            var visitor = new VisitAllVisitor();
            Assert.True(reader.Intersect("shape", visitor));
            Assert.Equal([0, 1, 2], visitor.Documents.Order());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Packed BKD survives detached flush and compound reopen")]
    public void FlushAndCompoundReader_RoundTripPackedField()
    {
        string directoryPath = CreateDirectory();
        var config = new IndexWriterConfig
        {
            DurableCommits = false,
            UseCompoundFile = true,
            MergePolicy = NoMergePolicy.Instance
        };
        var dwpt = new DocumentsWriterPerThread(
            new WhitespaceAnalyser(),
            new Dictionary<string, IAnalyser>(),
            config);
        var packedConfig = PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2);
        const int documentCount = 5;
        byte[] packed = new byte[8];
        for (int documentId = 0; documentId < documentCount; documentId++)
        {
            dwpt.AddDocument(new LeanDocument());
            XYEncodingUtils.Encode(documentId, packed.AsSpan(0, 4));
            XYEncodingUtils.Encode(documentId, packed.AsSpan(4, 4));
            dwpt.AddPackedBkdValue("location", packedConfig, packed, documentId);
        }

        DwptFlushSnapshot snapshot;
        lock (dwpt)
            snapshot = DwptFlushSnapshot.CaptureFrom(dwpt);

        try
        {
            SegmentInfo info = SegmentFlusher.FlushFromSnapshot(
                snapshot,
                config,
                directoryPath,
                ordinal: 0,
                commitGeneration: 0,
                seqStart: 0,
                seqEnd: documentCount);

            Assert.True(info.IsCompoundFile);
            Assert.False(File.Exists(Path.Combine(directoryPath, "seg_0.pbkd")));

            using var directory = new MMapDirectory(directoryPath);
            using var reader = new SegmentReader(directory, info);
            var packedReader = reader.PackedBkd;
            Assert.NotNull(packedReader);
            var visitor = new RangeVisitor(new XYPoint(1, 1), new XYPoint(3, 3));
            Assert.True(packedReader.Intersect("location", visitor));
            Assert.Equal([1, 2, 3], visitor.Documents.Distinct().Order());
        }
        finally
        {
            snapshot.Dispose();
            dwpt.Dispose();
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static PackedBkdFieldBuffer CreateBuffer(bool reverse)
    {
        var buffer = new PackedBkdFieldBuffer(PackedBkdConfig.Geo2D(maxPointsPerLeaf: 2));
        byte[] packed = new byte[8];
        IEnumerable<int> ids = reverse ? Enumerable.Range(0, 10).Reverse() : Enumerable.Range(0, 10);
        foreach (int id in ids)
        {
            XYEncodingUtils.Encode(id, packed.AsSpan(0, 4));
            XYEncodingUtils.Encode(id, packed.AsSpan(4, 4));
            buffer.Append(packed, id);
        }
        return buffer;
    }

    private static void AppendPoint(PackedBkdFieldBuffer buffer, float x, float y, int document)
    {
        byte[] packed = new byte[8];
        XYEncodingUtils.Encode(x, packed.AsSpan(0, 4));
        XYEncodingUtils.Encode(y, packed.AsSpan(4, 4));
        buffer.Append(packed, document);
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "leancorpus-packed-bkd", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static byte[] ReadBody(string path)
    {
        using var input = new IndexInput(path);
        using var session = CodecFileReader.Open(input, PackedBkdCodecFiles.Descriptor, ownsInput: true);
        session.ValidateChecksum();
        using var body = session.OpenBodyInput();
        byte[] bytes = new byte[checked((int)body.Length)];
        body.ReadBytes(bytes);
        return bytes;
    }

    private static void RewriteBody(string path, byte[] body)
    {
        string replacement = path + ".rewrite";
        CodecFileWriter.WriteAtomically(
            replacement,
            PackedBkdCodecFiles.Descriptor,
            durable: false,
            output => output.WriteBytes(body));
        File.Move(replacement, path, overwrite: true);
    }

    private static LeafHeader ReadFirstLeafHeader(string path)
    {
        byte[] body = ReadBody(path);
        int indexedDimensions = body[5];
        int leafCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(12, sizeof(int)));
        int splitCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(28, sizeof(int)));
        int offsetsStart = checked(32 + indexedDimensions * 4 * 2 + splitCount * 5);
        int leafDataStart = checked(offsetsStart + (leafCount + 1) * sizeof(long));
        return new LeafHeader(
            body[leafDataStart + 2],
            body[leafDataStart + 3],
            BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(leafDataStart + 4, sizeof(int))));
    }

    private readonly record struct LeafHeader(int DocumentWidth, byte Encoding, int MinimumDocument);

    private sealed class RangeVisitor(XYPoint minimum, XYPoint maximum) : IPackedBkdIntersectVisitor
    {
        private readonly byte[] _minimum = Pack(minimum);
        private readonly byte[] _maximum = Pack(maximum);
        internal List<int> Documents { get; } = [];

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
        {
            if (IsOutside(minimum, maximum))
                return PackedBkdCellRelation.Outside;
            return IsInside(minimum, maximum)
                ? PackedBkdCellRelation.Inside
                : PackedBkdCellRelation.Crosses;
        }

        public void Visit(int docId) => Documents.Add(docId);

        public void Visit(int docId, ReadOnlySpan<byte> packedValue)
        {
            if (IsValueInRange(packedValue))
                Documents.Add(docId);
        }

        private bool IsOutside(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => maximum[..4].SequenceCompareTo(_minimum.AsSpan(0, 4)) < 0
                || minimum[..4].SequenceCompareTo(_maximum.AsSpan(0, 4)) > 0
                || maximum[4..8].SequenceCompareTo(_minimum.AsSpan(4, 4)) < 0
                || minimum[4..8].SequenceCompareTo(_maximum.AsSpan(4, 4)) > 0;

        private bool IsInside(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => minimum[..4].SequenceCompareTo(_minimum.AsSpan(0, 4)) >= 0
                && maximum[..4].SequenceCompareTo(_maximum.AsSpan(0, 4)) <= 0
                && minimum[4..8].SequenceCompareTo(_minimum.AsSpan(4, 4)) >= 0
                && maximum[4..8].SequenceCompareTo(_maximum.AsSpan(4, 4)) <= 0;

        private bool IsValueInRange(ReadOnlySpan<byte> packedValue)
            => packedValue[..4].SequenceCompareTo(_minimum.AsSpan(0, 4)) >= 0
                && packedValue[..4].SequenceCompareTo(_maximum.AsSpan(0, 4)) <= 0
                && packedValue[4..8].SequenceCompareTo(_minimum.AsSpan(4, 4)) >= 0
                && packedValue[4..8].SequenceCompareTo(_maximum.AsSpan(4, 4)) <= 0;

        private static byte[] Pack(XYPoint point)
        {
            byte[] result = new byte[8];
            XYEncodingUtils.Encode(point.X, result.AsSpan(0, 4));
            XYEncodingUtils.Encode(point.Y, result.AsSpan(4, 4));
            return result;
        }
    }

    private sealed class VisitAllVisitor : IPackedBkdIntersectVisitor
    {
        internal List<int> Documents { get; } = [];

        public PackedBkdCellRelation Compare(ReadOnlySpan<byte> minimum, ReadOnlySpan<byte> maximum)
            => PackedBkdCellRelation.Inside;

        public void Visit(int docId) => Documents.Add(docId);
        public void Visit(int docId, ReadOnlySpan<byte> packedValue) => Documents.Add(docId);
    }
}
