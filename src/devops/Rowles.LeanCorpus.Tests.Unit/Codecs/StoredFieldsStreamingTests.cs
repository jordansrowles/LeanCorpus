using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

[Trait("Category", "Codecs")]
public sealed class StoredFieldsStreamingTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public StoredFieldsStreamingTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Stored Fields v3: writer emits version 3")]
    public void Writer_EmitsVersion3()
    {
        var path = Path.Combine(_fixture.Path, $"sf-v3-{Guid.NewGuid():N}");
        var docs = new[]
        {
            new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["title"] = [StoredFieldValue.FromString("hello")]
            }
        };

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs.Length, docId => docs[docId]);

        Assert.Equal(StoredFieldsFileHeader.V3, File.ReadAllBytes(path + ".fdt")[0]);
        Assert.Equal(StoredFieldsFileHeader.V3, File.ReadAllBytes(path + ".fdx")[0]);
    }

    [Fact(DisplayName = "Stored Fields v2: round-trip many documents")]
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

    [Fact(DisplayName = "Stored Fields v2: stream writer round-trip")]
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

    [Fact(DisplayName = "Stored Fields v2: stream writer cleans up .fdt when .fdx write fails")]
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
        int fdtHeaderSize = RewriteAsV1(path + ".fdt");
        RewriteFdxAsV1(path + ".fdx", fdtHeaderSize - 1);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        var stored = reader.ReadDocument(0);
        Assert.Equal("legacy", stored["title"][0]);
        Assert.Equal("42", stored["count"][0]);
    }

    [Fact(DisplayName = "Stored Fields: reader rejects future version")]
    public void Reader_RejectsFutureVersion()
    {
        var path = Path.Combine(_fixture.Path, $"sf-future-{Guid.NewGuid():N}");
        File.WriteAllBytes(path + ".fdt", [(byte)(StoredFieldsFileHeader.V3 + 1), 16, 0, 0, 0, 0]);
        File.WriteAllBytes(path + ".fdx", [(byte)(StoredFieldsFileHeader.V3 + 1), 16, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

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
        // .fdt layout: [version:1][blockSize:4][compression:1] then [docCount:4][rawLength:4][compLength:4]...
        // rawLength is at offset 1 + 4 + 1 + 4 = 10
        using (var fs = new FileStream(path + ".fdt", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            fs.Position = 10;
            var bombLength = BitConverter.GetBytes(StoredFieldsReader.MaxDecompressedBlockBytes + 1);
            fs.Write(bombLength);
        }

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        var ex = Assert.Throws<InvalidDataException>(() => reader.ReadDocument(0));
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
        // docCount is at offset 1 + 4 + 1 = 6
        using (var fs = new FileStream(path + ".fdt", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            fs.Position = 6;
            fs.Write(BitConverter.GetBytes(9999));
        }

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        var ex = Assert.Throws<InvalidDataException>(() => reader.ReadDocument(0));
        Assert.Contains("documents", ex.Message, StringComparison.OrdinalIgnoreCase);
    }


    private static int RewriteAsV1(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var body = bytes.AsSpan(1);
        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.WriteByte(StoredFieldsFileHeader.V1);
        int varintSize = WriteVarInt64(fs, body.Length);
        fs.Write(body);
        return 1 + varintSize;
    }

    private static void RewriteFdxAsV1(string filePath, long offsetDelta)
    {
        var bytes = File.ReadAllBytes(filePath);
        var body = bytes.AsSpan(1);
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
