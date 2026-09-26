using System.Buffers;
using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class StoredFieldsStreamingTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public StoredFieldsStreamingTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Stored Fields: flat writer preserves fields with dirty pooled scratch")]
    public void FlatWriter_PreservesFieldsWithDirtyPooledScratch()
    {
        var path = Path.Combine(_fixture.Path, $"sf-pooled-{Guid.NewGuid():N}");
        List<int> starts = [0, 3, 4];
        List<int> ids = [0, 1, 0, 1, 0, 1];
        List<string> names = ["id", "body"];
        List<StoredFieldValue> values =
        [
            StoredFieldValue.FromString("first"),
            StoredFieldValue.FromString("hello"),
            StoredFieldValue.FromString("alias"),
            StoredFieldValue.FromString("body only"),
            StoredFieldValue.FromString("last"),
            StoredFieldValue.FromString("goodbye")
        ];

        // Shared pools retain previous contents. Return on this thread immediately
        // before writing so the writer reuses scratch with every field marked seen.
        var scratch = ArrayPool<bool>.Shared.Rent(16);
        Array.Fill(scratch, true);
        ArrayPool<bool>.Shared.Return(scratch);
        try
        {
            StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", starts, ids, values, names, blockSize: 2);
        }
        finally
        {
            var cleanup = ArrayPool<bool>.Shared.Rent(16);
            ArrayPool<bool>.Shared.Return(cleanup, clearArray: true);
        }

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        var first = reader.ReadDocument(0);
        Assert.Equal(["first", "alias"], first["id"]);
        Assert.Equal(["hello"], first["body"]);
        var middle = reader.ReadDocument(1);
        Assert.Single(middle);
        Assert.Equal(["body only"], middle["body"]);
        var last = reader.ReadDocument(2);
        Assert.Equal(["last"], last["id"]);
        Assert.Equal(["goodbye"], last["body"]);
        AssertCanonicalFrame(path + ".fdt", StoredFieldsCodecFiles.Data);
        AssertCanonicalFrame(path + ".fdx", StoredFieldsCodecFiles.Index);
    }

    [Fact(DisplayName = "Stored Fields v4: writer emits coordinated canonical frames")]
    public void Writer_EmitsVersion4()
    {
        var path = Path.Combine(_fixture.Path, $"sf-v4-{Guid.NewGuid():N}");
        var docs = new[]
        {
            new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["title"] = [StoredFieldValue.FromString("hello")]
            }
        };

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs.Length, docId => docs[docId]);

        AssertCanonicalFrame(path + ".fdt", StoredFieldsCodecFiles.Data);
        AssertCanonicalFrame(path + ".fdx", StoredFieldsCodecFiles.Index);
    }

    [Fact(DisplayName = "Stored Fields: round-trip many documents")]
    public void Writer_RoundTrip_ManyDocuments()
    {
        var path = Path.Combine(_fixture.Path, $"sf-many-{Guid.NewGuid():N}");
        int docCount = 1000;
        var docs = new Dictionary<string, List<StoredFieldValue>>[docCount];
        for (int i = 0; i < docCount; i++)
        {
            docs[i] = new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["id"] = [StoredFieldValue.FromString($"doc-{i}")],
                ["body"] = [StoredFieldValue.FromString(new string('x', i % 100))]
            };
        }

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docCount, docId => docs[docId]);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        for (int i = 0; i < docCount; i++)
        {
            var stored = reader.ReadDocument(i);
            Assert.Equal($"doc-{i}", stored["id"][0]);
            Assert.Equal(new string('x', i % 100), stored["body"][0]);
        }
    }

    [Fact(DisplayName = "Stored Fields: writer splits blocks at the raw byte target")]
    public void Writer_SplitsBlocksWhenRawByteTargetWouldBeExceeded()
    {
        var path = Path.Combine(_fixture.Path, $"sf-byte-target-{Guid.NewGuid():N}");
        const int payloadBytes = 600_000;
        var docs = new Dictionary<string, List<StoredFieldValue>>[]
        {
            new(StringComparer.Ordinal) { ["body"] = [StoredFieldValue.FromString(new string('a', payloadBytes))] },
            new(StringComparer.Ordinal) { ["body"] = [StoredFieldValue.FromString(new string('b', payloadBytes))] }
        };

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs.Length, docId => docs[docId]);

        Assert.Equal(2, ReadBlockCount(path + ".fdx"));

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        Assert.Equal(new string('a', payloadBytes), reader.ReadDocument(0)["body"][0]);
        Assert.Equal(new string('b', payloadBytes), reader.ReadDocument(1)["body"][0]);
    }

    [Fact(DisplayName = "Stored Fields: flat writer splits blocks at the raw byte target")]
    public void FlatWriter_SplitsBlocksWhenRawByteTargetWouldBeExceeded()
    {
        var path = Path.Combine(_fixture.Path, $"sf-flat-byte-target-{Guid.NewGuid():N}");
        const int payloadBytes = 600_000;
        List<int> docStarts = [0, 1];
        List<int> fieldIds = [0, 0];
        List<StoredFieldValue> values =
        [
            StoredFieldValue.FromString(new string('a', payloadBytes)),
            StoredFieldValue.FromString(new string('b', payloadBytes))
        ];

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docStarts, fieldIds, values, ["body"]);

        Assert.Equal(2, ReadBlockCount(path + ".fdx"));
        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        Assert.Equal(new string('a', payloadBytes), reader.ReadDocument(0)["body"][0]);
        Assert.Equal(new string('b', payloadBytes), reader.ReadDocument(1)["body"][0]);
    }

    [Fact(DisplayName = "Stored Fields: streaming writer splits blocks at the raw byte target")]
    public void StreamWriter_SplitsBlocksWhenRawByteTargetWouldBeExceeded()
    {
        var path = Path.Combine(_fixture.Path, $"sf-stream-byte-target-{Guid.NewGuid():N}");
        const int payloadBytes = 600_000;
        using (var writer = new StoredFieldsStreamWriter(path + ".fdt", path + ".fdx"))
        {
            writer.AddDocument(new Dictionary<string, IReadOnlyList<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["body"] = new[] { StoredFieldValue.FromString(new string('a', payloadBytes)) }
            });
            writer.AddDocument(new Dictionary<string, IReadOnlyList<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["body"] = new[] { StoredFieldValue.FromString(new string('b', payloadBytes)) }
            });
        }

        Assert.Equal(2, ReadBlockCount(path + ".fdx"));
        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        Assert.Equal(new string('a', payloadBytes), reader.ReadDocument(0)["body"][0]);
        Assert.Equal(new string('b', payloadBytes), reader.ReadDocument(1)["body"][0]);
    }

    [Fact(DisplayName = "Stored Fields: a document over the target occupies a single block")]
    public void Writer_WritesOversizedDocumentInItsOwnBlock()
    {
        var path = Path.Combine(_fixture.Path, $"sf-oversized-single-{Guid.NewGuid():N}");
        var docs = new Dictionary<string, List<StoredFieldValue>>[]
        {
            new(StringComparer.Ordinal)
            {
                ["body"] = [StoredFieldValue.FromString(new string('x', StoredFieldsBlockPolicy.TargetRawBytes))]
            },
            new(StringComparer.Ordinal) { ["body"] = [StoredFieldValue.FromString("small")] }
        };

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs.Length, docId => docs[docId]);

        Assert.Equal(2, ReadBlockCount(path + ".fdx"));
        var headers = ReadBlockHeaders(path + ".fdt");
        Assert.Equal(1, headers[0].DocumentCount);
        Assert.True(headers[0].RawLength > StoredFieldsBlockPolicy.TargetRawBytes);
        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        Assert.Equal(new string('x', StoredFieldsBlockPolicy.TargetRawBytes), reader.ReadDocument(0)["body"][0]);
        Assert.Equal("small", reader.ReadDocument(1)["body"][0]);
    }

    [Fact(DisplayName = "Stored Fields v4: reader maps variable block counts during parallel reads")]
    public void Reader_MapsVariableBlockCountsDuringParallelReads()
    {
        var path = Path.Combine(_fixture.Path, $"sf-variable-parallel-{Guid.NewGuid():N}");
        const int payloadBytes = 600_000;
        var docs = new Dictionary<string, List<StoredFieldValue>>[]
        {
            new(StringComparer.Ordinal) { ["body"] = [StoredFieldValue.FromString(new string('a', payloadBytes))] },
            new(StringComparer.Ordinal) { ["body"] = [StoredFieldValue.FromString(new string('b', payloadBytes))] },
            new(StringComparer.Ordinal) { ["body"] = [StoredFieldValue.FromString("small-2")] },
            new(StringComparer.Ordinal) { ["body"] = [StoredFieldValue.FromString("small-3")] },
            new(StringComparer.Ordinal) { ["body"] = [StoredFieldValue.FromString("small-4")] }
        };

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs.Length, docId => docs[docId], blockSize: 16);

        Assert.Equal(new[] { 1, 4 }, ReadBlockHeaders(path + ".fdt").Select(static header => header.DocumentCount));
        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        Parallel.For(0, 500, operation =>
        {
            int docId = operation % docs.Length;
            Assert.Equal(docs[docId]["body"][0].StringValue, reader.ReadDocument(docId)["body"][0]);
        });
    }

    [Fact(DisplayName = "Stored Fields: shared policy rejects blocks beyond the hard byte limit")]
    public void BlockPolicy_RejectsRawLengthAboveHardMaximum()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            StoredFieldsBlockPolicy.ValidateRawLength((long)StoredFieldsBlockPolicy.MaximumRawBytes + 1));
        Assert.Contains("maximum block size", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Stored Fields: reader exposes DocCount and rejects out-of-range docId")]
    public void Reader_DocCount_AndBoundsCheck()
    {
        var path = Path.Combine(_fixture.Path, $"sf-bounds-{Guid.NewGuid():N}");
        var docs = new[]
        {
            new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["title"] = [StoredFieldValue.FromString("hello")]
            }
        };

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs.Length, docId => docs[docId]);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        Assert.Equal(1, reader.DocCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadDocument(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadDocument(1));
    }

    [Fact(DisplayName = "Stored Fields: stream writer round-trip")]
    public void StreamWriter_RoundTrip()
    {
        var path = Path.Combine(_fixture.Path, $"sf-stream-{Guid.NewGuid():N}");
        int docCount = 100;
        using (var writer = new StoredFieldsStreamWriter(path + ".fdt", path + ".fdx"))
        {
            for (int i = 0; i < docCount; i++)
            {
                writer.AddDocument(new Dictionary<string, IReadOnlyList<StoredFieldValue>>(StringComparer.Ordinal)
                {
                    ["id"] = new[] { StoredFieldValue.FromString($"doc-{i}") },
                    ["value"] = new[] { StoredFieldValue.FromLong(i) }
                });
            }
        }

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        for (int i = 0; i < docCount; i++)
        {
            var stored = reader.ReadDocument(i);
            Assert.Equal($"doc-{i}", stored["id"][0]);
            Assert.Equal(i.ToString(System.Globalization.CultureInfo.InvariantCulture), stored["value"][0]);
        }
    }

    [Fact(DisplayName = "Stored Fields: stream writer cleans up .fdt when .fdx write fails")]
    public void StreamWriter_CleansUpFdt_WhenFdxWriteFails()
    {
        var path = Path.Combine(_fixture.Path, $"sf-cleanup-{Guid.NewGuid():N}");
        var fdtPath = path + ".fdt";
        var fdxPath = Path.Combine(_fixture.Path, $"nonexistent-{Guid.NewGuid():N}", "file.fdx");

        Assert.Throws<DirectoryNotFoundException>(() =>
        {
            using (var writer = new StoredFieldsStreamWriter(fdtPath, fdxPath))
            {
                writer.AddDocument(new Dictionary<string, IReadOnlyList<StoredFieldValue>>(StringComparer.Ordinal)
                {
                    ["id"] = new[] { StoredFieldValue.FromString("doc-0") }
                });
            }
        });

        Assert.False(File.Exists(fdtPath), "Orphaned .fdt should be removed when .fdx write fails.");
    }

    [Fact(DisplayName = "Stored Fields v1: reader can read legacy CodecKit-envelope files")]
    public void Reader_ReadsV1Files()
    {
        var path = Path.Combine(_fixture.Path, $"sf-v1-{Guid.NewGuid():N}");
        var docs = new[]
        {
            new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["title"] = [StoredFieldValue.FromString("legacy")],
                ["count"] = [StoredFieldValue.FromLong(42)]
            }
        };

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs.Length, docId => docs[docId]);
        long fdtOffsetDelta = RewriteAsV1(path + ".fdt", StoredFieldsCodecFiles.Data);
        RewriteFdxAsV1(path + ".fdx", fdtOffsetDelta);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        var stored = reader.ReadDocument(0);
        Assert.Equal("legacy", stored["title"][0]);
        Assert.Equal("42", stored["count"][0]);
    }

    [Fact(DisplayName = "Stored Fields v3: reader keeps fixed-count canonical blocks readable")]
    public void Reader_ReadsV3CanonicalFiles()
    {
        var path = Path.Combine(_fixture.Path, $"sf-v3-{Guid.NewGuid():N}");
        var docs = new[]
        {
            new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["title"] = [StoredFieldValue.FromString("legacy-v3")]
            },
            new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["title"] = [StoredFieldValue.FromString("legacy-v3-second")]
            }
        };
        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs.Length, docId => docs[docId]);
        RewriteCanonicalVersion(path + ".fdt", StoredFieldsCodecFiles.Data, StoredFieldsFileHeader.V3);
        RewriteCanonicalVersion(path + ".fdx", StoredFieldsCodecFiles.Index, StoredFieldsFileHeader.V3);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        Assert.Equal("legacy-v3", reader.ReadDocument(0)["title"][0]);
        Assert.Equal("legacy-v3-second", reader.ReadDocument(1)["title"][0]);
    }

    [Fact(DisplayName = "Stored Fields: reader rejects future version")]
    public void Reader_RejectsFutureVersion()
    {
        var path = Path.Combine(_fixture.Path, $"sf-future-{Guid.NewGuid():N}");
        File.WriteAllBytes(path + ".fdt", [(byte)(CodecConstants.StoredFieldsVersion + 1), 16, 0, 0, 0, 0]);
        File.WriteAllBytes(path + ".fdx", [(byte)(CodecConstants.StoredFieldsVersion + 1), 16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        var ex = Assert.Throws<InvalidDataException>(() => StoredFieldsReader.Open(path + ".fdt", path + ".fdx"));
        Assert.Contains("format version", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Stored Fields: reader rejects decompression bomb with oversized rawLength")]
    public void Reader_RejectsDecompressionBomb()
    {
        var path = Path.Combine(_fixture.Path, $"sf-bomb-{Guid.NewGuid():N}");
        var docs = new Dictionary<string, List<string>>[]
        {
            new() { ["title"] = new List<string> { "test" } }
        };
        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs);

        // Overwrite rawLength in the first block header with a value exceeding the limit.
        long bodyStart = ReadCanonicalBodyStart(path + ".fdt", StoredFieldsCodecFiles.Data);
        using (var fs = new FileStream(path + ".fdt", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            fs.Position = bodyStart + sizeof(int) + sizeof(byte) + sizeof(int);
            var bombLength = BitConverter.GetBytes(StoredFieldsReader.MaxDecompressedBlockBytes + 1);
            fs.Write(bombLength);
        }

        var ex = Assert.Throws<InvalidDataException>(() => StoredFieldsReader.Open(path + ".fdt", path + ".fdx"));
        Assert.Contains("rawLength", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("exceeds maximum", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "Stored Fields: reader rejects block with docCount exceeding blockSize")]
    public void Reader_RejectsOversizedDocCount()
    {
        var path = Path.Combine(_fixture.Path, $"sf-doccnt-{Guid.NewGuid():N}");
        var docs = new Dictionary<string, List<string>>[]
        {
            new() { ["title"] = new List<string> { "test" } }
        };
        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs);

        // Overwrite docCount in the first block header to exceed blockSize (default 16).
        long bodyStart = ReadCanonicalBodyStart(path + ".fdt", StoredFieldsCodecFiles.Data);
        using (var fs = new FileStream(path + ".fdt", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            fs.Position = bodyStart + sizeof(int) + sizeof(byte);
            fs.Write(BitConverter.GetBytes(9999));
        }

        var ex = Assert.Throws<InvalidDataException>(() => StoredFieldsReader.Open(path + ".fdt", path + ".fdx"));
        Assert.Contains("documents", ex.Message, StringComparison.OrdinalIgnoreCase);
    }


    private static long RewriteAsV1(string filePath, CodecFileDescriptor descriptor)
    {
        var (body, canonicalBodyStart) = ReadCanonicalBody(filePath, descriptor);
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.WriteByte(StoredFieldsFileHeader.V1);
        int varintSize = WriteVarInt64(fs, body.Length);
        fs.Write(body);
        return 1 + varintSize - canonicalBodyStart;
    }

    private static void RewriteCanonicalVersion(string filePath, CodecFileDescriptor descriptor, byte version)
    {
        var (body, _) = ReadCanonicalBody(filePath, descriptor);
        CodecFileWriter.WriteAtomically(
            filePath,
            descriptor.FormatId,
            version,
            durable: false,
            output => output.WriteBytes(body));
    }

    private static void RewriteFdxAsV1(string filePath, long offsetDelta)
    {
        var (bodyBytes, _) = ReadCanonicalBody(filePath, StoredFieldsCodecFiles.Index);
        var body = bodyBytes.AsSpan();
        int blockCount = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(body.Slice(8));

        for (int i = 0; i < blockCount; i++)
        {
            long offset = System.Buffers.Binary.BinaryPrimitives.ReadInt64LittleEndian(body.Slice(12 + i * 8));
            System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(
                body.Slice(12 + i * 8), offset + offsetDelta);
        }

        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.WriteByte(StoredFieldsFileHeader.V1);
        WriteVarInt64(fs, body.Length);
        fs.Write(body);
    }

    private static void AssertCanonicalFrame(string path, CodecFileDescriptor descriptor)
    {
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, descriptor);
        Assert.Equal(descriptor.FormatId, frame.Metadata.FormatId);
        Assert.Equal(descriptor.CurrentFormatVersion, frame.Metadata.FormatVersion);
        Assert.Equal(CodecFileChecksumAlgorithm.XxHash64, frame.Metadata.ChecksumAlgorithm);
        frame.ValidateChecksum();
    }

    private static int ReadBlockCount(string fdxPath)
    {
        using var input = new IndexInput(fdxPath);
        using var frame = CodecFileReader.Open(input, StoredFieldsCodecFiles.Index);
        _ = input.ReadInt32();
        _ = input.ReadInt32();
        return input.ReadInt32();
    }

    private static (int DocumentCount, int RawLength)[] ReadBlockHeaders(string fdtPath)
    {
        using var input = new IndexInput(fdtPath);
        using var frame = StoredFieldsCodecFiles.OpenData(input);
        _ = input.ReadInt32();
        _ = input.ReadByte();
        var offsets = new List<long>();
        using (var fdxInput = new IndexInput(Path.ChangeExtension(fdtPath, ".fdx")))
        using (var fdxFrame = CodecFileReader.Open(fdxInput, StoredFieldsCodecFiles.Index))
        {
            _ = fdxInput.ReadInt32();
            _ = fdxInput.ReadInt32();
            int blockCount = fdxInput.ReadInt32();
            for (int i = 0; i < blockCount; i++)
                offsets.Add(fdxInput.ReadInt64());
        }

        var headers = new (int DocumentCount, int RawLength)[offsets.Count];
        for (int i = 0; i < offsets.Count; i++)
        {
            long position = offsets[i];
            using var session = input.BeginReadSession();
            headers[i] = (session.ReadInt32(ref position), session.ReadInt32(ref position));
        }

        return headers;
    }

    private static long ReadCanonicalBodyStart(string path, CodecFileDescriptor descriptor)
    {
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, descriptor);
        return frame.Metadata.BodyStart;
    }

    private static (byte[] Body, long BodyStart) ReadCanonicalBody(string path, CodecFileDescriptor descriptor)
    {
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, descriptor);
        return (frame.ReadBody(), frame.Metadata.BodyStart);
    }

    private static int WriteVarInt64(Stream stream, long value)
    {
        int bytesWritten = 0;
        while (value >= 0x80)
        {
            stream.WriteByte((byte)(value | 0x80));
            value >>= 7;
            bytesWritten++;
        }
        stream.WriteByte((byte)value);
        return bytesWritten + 1;
    }
}
