using System.Buffers.Binary;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class StoredFieldsCorruptionTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public StoredFieldsCorruptionTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Stored Fields: unknown value kind is corruption and another block remains readable")]
    public void ReadDocumentValues_RejectsUnknownValueKindAndKeepsReaderUsable()
    {
        string path = CreateIndex([CreateStringDocument("first"), CreateStringDocument("second")], blockSize: 1);
        long[] blockOffsets = ReadBlockOffsets(path + ".fdx");
        long rawDataOffset = ReadRawDataOffset(path + ".fdt", blockOffsets[0]);
        WriteByte(path + ".fdt", rawDataOffset + ValueKindOffset("id"), byte.MaxValue);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");

        Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(0));
        Assert.Equal("second", reader.ReadDocumentValues(1)["id"][0].StringValue);
    }

    [Fact(DisplayName = "Stored Fields: HasField validates values after finding the requested field")]
    public void HasField_RejectsUnknownValueKindAfterMatchingField()
    {
        string path = CreateIndex([CreateStringDocument("first")], blockSize: 1);
        long blockOffset = ReadBlockOffsets(path + ".fdx")[0];
        long rawDataOffset = ReadRawDataOffset(path + ".fdt", blockOffset);
        WriteByte(path + ".fdt", rawDataOffset + ValueKindOffset("id"), byte.MaxValue);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");

        Assert.Throws<InvalidDataException>(() => reader.HasField(0, "id"));
    }

    [Fact(DisplayName = "Stored Fields: skipped values still reject negative lengths")]
    public void ReadDocumentValues_RejectsNegativeLengthWhenSkippingField()
    {
        string path = CreateIndex([CreateStringDocument("first"), CreateStringDocument("second")], blockSize: 1);
        long[] blockOffsets = ReadBlockOffsets(path + ".fdx");
        long rawDataOffset = ReadRawDataOffset(path + ".fdt", blockOffsets[0]);
        WriteInt32(path + ".fdt", rawDataOffset + ValueLengthOffset("id"), -1);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");

        Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(0, new HashSet<string>(StringComparer.Ordinal)));
        Assert.Equal("second", reader.ReadDocumentValues(1)["id"][0].StringValue);
    }

    [Fact(DisplayName = "Stored Fields: field and value counts are bounded by the document")]
    public void ReadDocumentValues_RejectsCountsBeyondDocumentBounds()
    {
        string fieldCountPath = CreateIndex([CreateStringDocument("first")], blockSize: 1);
        long fieldCountBlock = ReadBlockOffsets(fieldCountPath + ".fdx")[0];
        long fieldCountRaw = ReadRawDataOffset(fieldCountPath + ".fdt", fieldCountBlock);
        WriteInt32(fieldCountPath + ".fdt", fieldCountRaw, 1_000);
        using (var reader = StoredFieldsReader.Open(fieldCountPath + ".fdt", fieldCountPath + ".fdx"))
            Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(0));

        string valueCountPath = CreateIndex([CreateStringDocument("first")], blockSize: 1);
        long valueCountBlock = ReadBlockOffsets(valueCountPath + ".fdx")[0];
        long valueCountRaw = ReadRawDataOffset(valueCountPath + ".fdt", valueCountBlock);
        WriteInt32(valueCountPath + ".fdt", valueCountRaw + ValueCountOffset("id"), 1_000);
        using var valueReader = StoredFieldsReader.Open(valueCountPath + ".fdt", valueCountPath + ".fdx");
        Assert.Throws<InvalidDataException>(() => valueReader.ReadDocumentValues(0));
    }

    [Fact(DisplayName = "Stored Fields: field and value counts cannot be negative")]
    public void ReadDocumentValues_RejectsNegativeCounts()
    {
        string fieldCountPath = CreateIndex([CreateStringDocument("first")], blockSize: 1);
        long fieldCountBlock = ReadBlockOffsets(fieldCountPath + ".fdx")[0];
        long fieldCountRaw = ReadRawDataOffset(fieldCountPath + ".fdt", fieldCountBlock);
        WriteInt32(fieldCountPath + ".fdt", fieldCountRaw, -1);
        using (var reader = StoredFieldsReader.Open(fieldCountPath + ".fdt", fieldCountPath + ".fdx"))
            Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(0));

        string valueCountPath = CreateIndex([CreateStringDocument("first")], blockSize: 1);
        long valueCountBlock = ReadBlockOffsets(valueCountPath + ".fdx")[0];
        long valueCountRaw = ReadRawDataOffset(valueCountPath + ".fdt", valueCountBlock);
        WriteInt32(valueCountPath + ".fdt", valueCountRaw + ValueCountOffset("id"), -1);
        using var valueReader = StoredFieldsReader.Open(valueCountPath + ".fdt", valueCountPath + ".fdx");
        Assert.Throws<InvalidDataException>(() => valueReader.ReadDocumentValues(0));
    }

    [Fact(DisplayName = "Stored Fields: field names cannot extend beyond the current document")]
    public void ReadDocumentValues_RejectsFieldNameLengthBeyondDocument()
    {
        string path = CreateIndex([CreateStringDocument("first")], blockSize: 1);
        long blockOffset = ReadBlockOffsets(path + ".fdx")[0];
        long rawDataOffset = ReadRawDataOffset(path + ".fdt", blockOffset);
        WriteInt32(path + ".fdt", rawDataOffset + sizeof(int), int.MaxValue);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");

        Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(0));
    }

    [Fact(DisplayName = "Stored Fields: lengths cannot extend beyond the current document")]
    public void ReadDocumentValues_RejectsLengthBeyondDocument()
    {
        string path = CreateIndex([CreateStringDocument("first")], blockSize: 1);
        long blockOffset = ReadBlockOffsets(path + ".fdx")[0];
        long rawDataOffset = ReadRawDataOffset(path + ".fdt", blockOffset);
        WriteInt32(path + ".fdt", rawDataOffset + ValueLengthOffset("id"), int.MaxValue);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");

        Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(0));
    }

    [Fact(DisplayName = "Stored Fields: long values require exactly eight payload bytes")]
    public void ReadDocumentValues_RejectsLongWithIncorrectLength()
    {
        var document = new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
        {
            ["count"] = [StoredFieldValue.FromLong(42)]
        };
        string path = CreateIndex([document], blockSize: 1);
        long blockOffset = ReadBlockOffsets(path + ".fdx")[0];
        long rawDataOffset = ReadRawDataOffset(path + ".fdt", blockOffset);
        WriteInt32(path + ".fdt", rawDataOffset + ValueLengthOffset("count"), sizeof(long) - 1);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");

        Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(0));
    }

    [Fact(DisplayName = "Stored Fields: document offsets must increase within the decompressed block")]
    public void ReadDocumentValues_RejectsNonMonotonicOffsets()
    {
        string path = CreateIndex([CreateStringDocument("first"), CreateStringDocument("second")], blockSize: 2);
        long blockOffset = ReadBlockOffsets(path + ".fdx")[0];
        WriteInt32(path + ".fdt", blockOffset + 3 * sizeof(int) + sizeof(int), 0);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");

        Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(1));
    }

    [Fact(DisplayName = "Stored Fields: the first document offset is zero and within the block")]
    public void ReadDocumentValues_RejectsFirstOffsetOutsideBlock()
    {
        string path = CreateIndex([CreateStringDocument("first")], blockSize: 1);
        long blockOffset = ReadBlockOffsets(path + ".fdx")[0];
        int rawLength = ReadRawLength(path + ".fdt", blockOffset);
        WriteInt32(path + ".fdt", blockOffset + 3 * sizeof(int), rawLength);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");

        Assert.Throws<InvalidDataException>(() => reader.ReadDocumentValues(0));
    }

    private string CreateIndex(Dictionary<string, List<StoredFieldValue>>[] documents, int blockSize)
    {
        string path = Path.Combine(_fixture.Path, $"sf-corrupt-{Guid.NewGuid():N}");
        StoredFieldsWriter.Write(
            path + ".fdt",
            path + ".fdx",
            documents.Length,
            docId => documents[docId],
            blockSize,
            FieldCompressionPolicy.None);
        return path;
    }

    private static Dictionary<string, List<StoredFieldValue>> CreateStringDocument(string value)
        => new(StringComparer.Ordinal)
        {
            ["id"] = [StoredFieldValue.FromString(value)]
        };

    private static long[] ReadBlockOffsets(string fdxPath)
    {
        using var input = new IndexInput(fdxPath);
        using var frame = CodecFileReader.Open(input, StoredFieldsCodecFiles.Index);
        byte[] body = frame.ReadBody();
        int blockCount = BinaryPrimitives.ReadInt32LittleEndian(body.AsSpan(2 * sizeof(int)));
        var offsets = new long[blockCount];
        for (int i = 0; i < blockCount; i++)
            offsets[i] = BinaryPrimitives.ReadInt64LittleEndian(body.AsSpan(3 * sizeof(int) + i * sizeof(long)));
        return offsets;
    }

    private static long ReadRawDataOffset(string fdtPath, long blockOffset)
    {
        using var input = new FileStream(fdtPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        input.Position = blockOffset;
        Span<byte> header = stackalloc byte[3 * sizeof(int)];
        input.ReadExactly(header);
        int docCount = BinaryPrimitives.ReadInt32LittleEndian(header);
        return checked(blockOffset + header.Length + (long)docCount * sizeof(int));
    }

    private static int ValueKindOffset(string fieldName)
        => checked(sizeof(int) + sizeof(int) + System.Text.Encoding.UTF8.GetByteCount(fieldName) + sizeof(int));

    private static int ValueLengthOffset(string fieldName)
        => checked(ValueKindOffset(fieldName) + sizeof(byte));

    private static int ValueCountOffset(string fieldName)
        => checked(sizeof(int) + sizeof(int) + System.Text.Encoding.UTF8.GetByteCount(fieldName));

    private static int ReadRawLength(string fdtPath, long blockOffset)
    {
        using var input = new FileStream(fdtPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        input.Position = blockOffset + sizeof(int);
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        input.ReadExactly(bytes);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    private static void WriteByte(string path, long offset, byte value)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        stream.Position = offset;
        stream.WriteByte(value);
    }

    private static void WriteInt32(string path, long offset, int value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        stream.Position = offset;
        stream.Write(bytes);
    }
}
