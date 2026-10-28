using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.CodecKit;

[Category(TestCategory.Integration)]
[Area(TestArea.CodecKit)]
public sealed class StoredFieldsBoundaryIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public StoredFieldsBoundaryIntegrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    // Explicit because this single exact-boundary regression retains several hundred MiB.
    [Fact(Explicit = true, DisplayName = "Stored Fields: exact 256 MiB raw document round-trips with one-byte codec expansion")]
    public void MaximumRawDocumentWithExpandingCodecRoundTrips()
    {
        var document = CreateBoundaryDocument();
        Assert.Equal(StoredFieldsBlockPolicy.MaximumRawBytes,
            StoredFieldsBlockEncoder.GetDictionaryDocumentRawLength(document, new(StringComparer.Ordinal), []));
        var catalogue = new CodecCatalogBuilder().AddBuiltIns()
            .ReplaceCompressionCodec(new SentinelCompressionCodec()).Build();
        string path = Path.Combine(_fixture.Path, $"stored-boundary-{Guid.NewGuid():N}");
        StoredFieldsWriter.Write(path + ".fdt", path + ".fdx", 1, _ => document, catalog: catalogue);

        using (var input = new IndexInput(path + ".fdt"))
        using (var frame = CodecFileReader.Open(input, StoredFieldsCodecFiles.Data))
        {
            Assert.Equal(5, frame.Metadata.FormatVersion);
            frame.ValidateChecksum();
            Assert.Equal(16, input.ReadInt32());
            Assert.Equal((byte)FieldCompressionPolicy.Deflate, input.ReadByte());
            Assert.Equal(1, input.ReadInt32());
            Assert.Equal(256 * 1024 * 1024, input.ReadInt32());
            Assert.Equal(256 * 1024 * 1024 + 1, input.ReadInt32());
        }
        using (var input = new IndexInput(path + ".fdx"))
        using (var frame = CodecFileReader.Open(input, StoredFieldsCodecFiles.Index))
        {
            Assert.Equal(5, frame.Metadata.FormatVersion);
            frame.ValidateChecksum();
        }
        using var reader = StoredFieldsReader.Open(path + ".fdt", path + ".fdx", catalogue);
        Assert.Equal(1, reader.DocCount);
        var values = reader.ReadDocumentValues(0);
        Assert.True(values["payload"][0].IsBinary);
        Assert.True(document["payload"][0].BinaryValue!.AsSpan().SequenceEqual(values["payload"][0].BinaryValue));
        Assert.Equal(5, CodecConstants.StoredFieldsVersion);
    }

    private static Dictionary<string, List<StoredFieldValue>> CreateBoundaryDocument()
    {
        var document = new Dictionary<string, List<StoredFieldValue>>(StringComparer.Ordinal)
        {
            ["payload"] = [StoredFieldValue.FromBinary([])]
        };
        long overhead = StoredFieldsBlockEncoder.GetDictionaryDocumentRawLength(document, new(StringComparer.Ordinal), []);
        byte[] payload = new byte[checked((int)(StoredFieldsBlockPolicy.MaximumRawBytes - overhead))];
        for (int i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i * 31 + 17);
        document["payload"][0] = StoredFieldValue.FromBinary(payload);
        return document;
    }

    private sealed class SentinelCompressionCodec : IFieldCompressionCodec
    {
        private const byte Sentinel = 0xA7;
        public byte PolicyByte => (byte)FieldCompressionPolicy.Deflate;

        public byte[] Compress(ReadOnlySpan<byte> data)
        {
            byte[] encoded = new byte[checked(data.Length + 1)];
            data.CopyTo(encoded);
            encoded[^1] = Sentinel;
            return encoded;
        }

        public byte[] Decompress(ReadOnlySpan<byte> data, int originalLength)
        {
            if (data.Length != checked(originalLength + 1) || data[^1] != Sentinel)
                throw new InvalidDataException("Sentinel codec length or sentinel is invalid.");
            return data[..originalLength].ToArray();
        }
    }
}
