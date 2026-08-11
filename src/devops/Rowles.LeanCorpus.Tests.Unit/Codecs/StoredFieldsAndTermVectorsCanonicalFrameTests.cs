using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Codecs.TermVectors;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

[Trait("Category", "Codecs")]
public sealed class StoredFieldsAndTermVectorsCanonicalFrameTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public StoredFieldsAndTermVectorsCanonicalFrameTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Stored Fields: direct and stream writers emit checksummed canonical pairs with monotonic offsets")]
    public void StoredFields_WritersEmitCanonicalPairsWithMonotonicOffsets()
    {
        foreach (bool streaming in new[] { false, true })
        {
            string path = Path.Combine(_fixture.Path, $"stored-canonical-{streaming}-{Guid.NewGuid():N}");
            if (streaming)
            {
                using var writer = new StoredFieldsStreamWriter(path + ".fdt", path + ".fdx", blockSize: 2);
                for (int docId = 0; docId < 5; docId++)
                    writer.AddDocument(CreateStoredDocument(docId));
            }
            else
            {
                StoredFieldsWriter.Write(
                    path + ".fdt",
                    path + ".fdx",
                    5,
                    docId => CreateStoredDocument(docId).ToDictionary(
                        static pair => pair.Key,
                        static pair => pair.Value.ToList()),
                    blockSize: 2);
            }

            using var dataInput = new IndexInput(path + ".fdt");
            using var dataFrame = CodecFileReader.Open(dataInput, StoredFieldsCodecFiles.Data);
            dataFrame.ValidateChecksum();

            using var indexInput = new IndexInput(path + ".fdx");
            using var indexFrame = CodecFileReader.Open(indexInput, StoredFieldsCodecFiles.Index);
            indexFrame.ValidateChecksum();
            indexInput.Seek(indexFrame.Metadata.BodyStart);
            Assert.Equal(2, indexInput.ReadInt32());
            Assert.Equal(5, indexInput.ReadInt32());
            Assert.Equal(3, indexInput.ReadInt32());
            long[] offsets = [indexInput.ReadInt64(), indexInput.ReadInt64(), indexInput.ReadInt64()];
            Assert.Equal(dataFrame.Metadata.BodyStart + sizeof(int) + sizeof(byte), offsets[0]);
            Assert.True(offsets[0] < offsets[1] && offsets[1] < offsets[2]);

            using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
            Assert.Equal("doc-4", reader.ReadDocument(4)["id"].Single());
        }
    }

    [Fact(DisplayName = "Stored Fields: reader accepts the v2 custom-header pair")]
    public void StoredFields_ReaderAcceptsV2CustomPair()
    {
        string path = Path.Combine(_fixture.Path, $"stored-v2-{Guid.NewGuid():N}");
        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", 1, _ =>
            new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["id"] = [StoredFieldValue.FromString("legacy-v2")]
            });

        var (dataBody, dataBodyStart) = ReadCanonicalBody(path + ".fdt", StoredFieldsCodecFiles.Data);
        var (indexBody, _) = ReadCanonicalBody(path + ".fdx", StoredFieldsCodecFiles.Index);
        AdjustOffsets(indexBody, offsetTableStart: 12, count: 1, sizeof(byte) - dataBodyStart);
        WriteCustomFrame(path + ".fdt", StoredFieldsFileHeader.V2, dataBody);
        WriteCustomFrame(path + ".fdx", StoredFieldsFileHeader.V2, indexBody);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        Assert.Equal("legacy-v2", reader.ReadDocument(0)["id"].Single());
    }

    [Fact(DisplayName = "Stored Fields: paired canonical versions must match")]
    public void StoredFields_RejectsMismatchedPairVersions()
    {
        string path = Path.Combine(_fixture.Path, $"stored-mismatch-{Guid.NewGuid():N}");
        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", 1, _ =>
            new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal));
        PatchCanonicalFormatVersion(path + ".fdx", StoredFieldsFileHeader.V2);

        var error = Assert.Throws<InvalidDataException>(() => StoredFieldsReader.Open(path + ".fdt", path + ".fdx"));
        Assert.Contains("Mismatched stored fields versions", error.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Term Vectors: direct and stream writers emit checksummed canonical pairs with monotonic offsets")]
    public void TermVectors_WritersEmitCanonicalPairsWithMonotonicOffsets()
    {
        foreach (bool streaming in new[] { false, true })
        {
            string path = Path.Combine(_fixture.Path, $"vectors-canonical-{streaming}-{Guid.NewGuid():N}");
            var docs = Enumerable.Range(0, 3).Select(CreateTermVectorDocument).ToArray();
            if (streaming)
            {
                using var writer = new TermVectorsStreamWriter(path + ".tvd", path + ".tvx");
                foreach (var document in docs)
                    writer.AddDocument(document);
            }
            else
            {
                TermVectorsWriter.Write(path + ".tvd", path + ".tvx", docs);
            }

            using var dataInput = new IndexInput(path + ".tvd");
            using var dataFrame = CodecFileReader.Open(dataInput, TermVectorsCodecFiles.Data);
            dataFrame.ValidateChecksum();

            using var indexInput = new IndexInput(path + ".tvx");
            using var indexFrame = CodecFileReader.Open(indexInput, TermVectorsCodecFiles.Index);
            indexFrame.ValidateChecksum();
            indexInput.Seek(indexFrame.Metadata.BodyStart);
            Assert.Equal(3, indexInput.ReadInt32());
            long[] offsets = [indexInput.ReadInt64(), indexInput.ReadInt64(), indexInput.ReadInt64()];
            Assert.Equal(dataFrame.Metadata.BodyStart, offsets[0]);
            Assert.True(offsets[0] < offsets[1] && offsets[1] < offsets[2]);

            using var reader = TermVectorsReader.Open(path + ".tvd", path + ".tvx");
            Assert.Equal("term-2", reader.GetTermVector(2)["body"].Single().Term);
        }
    }

    [Fact(DisplayName = "Term Vectors: reader accepts the declared v3 trailer pair")]
    public void TermVectors_ReaderAcceptsV3TrailerPair()
    {
        string path = Path.Combine(_fixture.Path, $"vectors-v3-trailer-{Guid.NewGuid():N}");
        TermVectorsWriter.Write(path + ".tvd", path + ".tvx", [CreateTermVectorDocument(0)]);

        var (dataBody, dataBodyStart) = ReadCanonicalBody(path + ".tvd", TermVectorsCodecFiles.Data);
        var (indexBody, _) = ReadCanonicalBody(path + ".tvx", TermVectorsCodecFiles.Index);
        AdjustOffsets(indexBody, offsetTableStart: sizeof(int), count: 1, sizeof(byte) - dataBodyStart);
        WriteTrailerFrame(path + ".tvd", 3, dataBody);
        WriteTrailerFrame(path + ".tvx", 3, indexBody);

        using var reader = TermVectorsReader.Open(path + ".tvd", path + ".tvx");
        Assert.Equal("term-0", reader.GetTermVector(0)["body"].Single().Term);
    }

    [Fact(DisplayName = "Term Vectors: paired canonical versions must match")]
    public void TermVectors_RejectsMismatchedPairVersions()
    {
        string path = Path.Combine(_fixture.Path, $"vectors-mismatch-{Guid.NewGuid():N}");
        TermVectorsWriter.Write(path + ".tvd", path + ".tvx", [CreateTermVectorDocument(0)]);
        PatchCanonicalFormatVersion(path + ".tvx", 2);

        var error = Assert.Throws<InvalidDataException>(() => TermVectorsReader.Open(path + ".tvd", path + ".tvx"));
        Assert.Contains("Mismatched term vectors versions", error.Message, StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<StoredFieldValue>> CreateStoredDocument(int docId)
        => new Dictionary<string, IReadOnlyList<StoredFieldValue>>(StringComparer.Ordinal)
        {
            ["id"] = [StoredFieldValue.FromString($"doc-{docId}")],
            ["number"] = [StoredFieldValue.FromLong(docId)]
        };

    private static Dictionary<string, List<TermVectorEntry>> CreateTermVectorDocument(int docId)
        => new(StringComparer.Ordinal)
        {
            ["body"] =
            [
                new TermVectorEntry($"term-{docId}", 1, [docId], null, [docId * 2], [docId * 2 + 1])
            ]
        };

    private static (byte[] Body, long BodyStart) ReadCanonicalBody(string path, CodecFileDescriptor descriptor)
    {
        using var input = new IndexInput(path);
        using var frame = CodecFileReader.Open(input, descriptor);
        return (frame.ReadBody(), frame.Metadata.BodyStart);
    }

    private static void AdjustOffsets(byte[] body, int offsetTableStart, int count, long delta)
    {
        for (int i = 0; i < count; i++)
        {
            Span<byte> bytes = body.AsSpan(offsetTableStart + i * sizeof(long), sizeof(long));
            BinaryPrimitives.WriteInt64LittleEndian(bytes, BinaryPrimitives.ReadInt64LittleEndian(bytes) + delta);
        }
    }

    private static void WriteCustomFrame(string path, byte version, byte[] body)
    {
        using var output = new IndexOutput(path);
        output.WriteByte(version);
        output.WriteBytes(body);
    }

    private static void WriteTrailerFrame(string path, byte version, byte[] body)
    {
        using var output = new IndexOutput(path);
        output.WriteByte(version);
        output.WriteBytes(body);
        output.WriteInt64(body.Length);
    }

    private static void PatchCanonicalFormatVersion(string path, int version)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.None);
        stream.Position = sizeof(uint) + sizeof(byte) + sizeof(byte);
        stream.Write(BitConverter.GetBytes(version));
    }
}
