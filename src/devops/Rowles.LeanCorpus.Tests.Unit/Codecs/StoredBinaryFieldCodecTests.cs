using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs;

[Trait("Category", "Codecs")]
public sealed class StoredBinaryFieldCodecTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public StoredBinaryFieldCodecTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Stored Fields: String And Binary Values Round-Trip")]
    public void StoredFields_StringAndBinaryValues_RoundTrip()
    {
        var path = Path.Combine(_fixture.Path, $"stored-binary-{Guid.NewGuid():N}");
        var docs = new[]
        {
            new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["title"] = [StoredFieldValue.FromString("hello")],
                ["blob"] = [StoredFieldValue.FromBinary([0xCA, 0xFE, 0xBA, 0xBE])]
            }
        };

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs.Length, docId => docs[docId]);

        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx");
        var strings = reader.ReadDocument(0);
        var mixed = reader.ReadDocumentValues(0);

        Assert.Equal("hello", strings["title"][0]);
        Assert.False(strings.ContainsKey("blob"));
        Assert.Equal(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, mixed["blob"][0].BinaryValue);
    }

    [Fact(DisplayName = "Stored Fields: Open Rejects Mismatched Fdt Version")]
    public void StoredFields_Open_RejectsMismatchedFdtVersion()
    {
        var path = WriteStoredFieldsFixture();

        using (var stream = new FileStream(path + ".fdt", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            stream.Position = 0;
            writer.Write((byte)99);
        }

        var exception = Assert.Throws<InvalidDataException>(() => StoredFieldsReader.Open(path + ".fdt", path + ".fdx"));
        Assert.Contains("Unsupported stored fields data (.fdt) format version", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Stored Fields: Open Rejects Mismatched Block Size")]
    public void StoredFields_Open_RejectsMismatchedBlockSize()
    {
        var path = WriteStoredFieldsFixture();

        using (var stream = new FileStream(path + ".fdt", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            stream.Position = 2; // skip CodecKit header (version:byte + VarInt bodyLen)
            writer.Write(32);
        }

        var exception = Assert.Throws<InvalidDataException>(() => StoredFieldsReader.Open(path + ".fdt", path + ".fdx"));
        Assert.Contains("Mismatched stored fields block sizes", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Stored Fields: Open Rejects Unsupported Fdt Version")]
    public void StoredFields_Open_RejectsUnsupportedFdtVersion()
    {
        var path = WriteStoredFieldsFixture();

        using (var stream = new FileStream(path + ".fdt", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            stream.Position = 0;
            writer.Write((byte)(CodecConstants.StoredFieldsVersion + 1));
        }

        var exception = Assert.Throws<InvalidDataException>(() => StoredFieldsReader.Open(path + ".fdt", path + ".fdx"));
        Assert.Contains("Unsupported stored fields data (.fdt) format version", exception.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "Binary Field: Preserves Raw Bytes")]
    public void BinaryField_PreservesRawBytes()
    {
        byte[] data = [0x10, 0x20, 0x30];
        var field = new BinaryField("blob", data);

        Assert.Equal(data, field.Value.ToArray());
        Assert.Equal(FieldType.Binary, field.FieldType);
        Assert.True(field.IsStored);
        Assert.False(field.IsIndexed);
    }

    private string WriteStoredFieldsFixture()
    {
        var path = Path.Combine(_fixture.Path, $"stored-binary-open-{Guid.NewGuid():N}");
        var docs = new[]
        {
            new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
            {
                ["title"] = [StoredFieldValue.FromString("hello")],
                ["blob"] = [StoredFieldValue.FromBinary([0xCA, 0xFE, 0xBA, 0xBE])]
            }
        };

        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", docs.Length, docId => docs[docId]);
        return path;
    }
}
