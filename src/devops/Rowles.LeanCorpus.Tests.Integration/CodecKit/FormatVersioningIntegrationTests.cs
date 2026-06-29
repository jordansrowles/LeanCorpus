using Rowles.LeanCorpus.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Formats;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class FormatVersioningIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public FormatVersioningIntegrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact(DisplayName = "All format version constants are ≤ 127 (fit in signed byte)")]
    public void AllVersionConstants_AreLessThanOrEqual127()
    {
        Assert.True(CodecConstants.TermDictionaryVersion <= 127);
        Assert.True(CodecConstants.PostingsVersion <= 127);
        Assert.True(CodecConstants.NormsVersion <= 127);
        Assert.True(CodecConstants.VectorVersion <= 127);
        Assert.True(CodecConstants.QuantisedVectorVersion <= 127);
        Assert.True(CodecConstants.HnswVersion <= 127);
        Assert.True(CodecConstants.StoredFieldsVersion <= 127);
        Assert.True(CodecConstants.TermVectorsVersion <= 127);
        Assert.True(CodecConstants.NumericDocValuesVersion <= 127);
        Assert.True(CodecConstants.SortedDocValuesVersion <= 127);
        Assert.True(CodecConstants.SortedSetDocValuesVersion <= 127);
        Assert.True(CodecConstants.SortedNumericDocValuesVersion <= 127);
        Assert.True(CodecConstants.BinaryDocValuesVersion <= 127);
        Assert.True(CodecConstants.BKDVersion <= 127);
        Assert.True(CodecConstants.FieldLengthVersion <= 127);
        Assert.True(CodecConstants.RoaringBitmapVersion <= 127);
    }

    [Fact(DisplayName = "VersionEnvelopeCodec handles version=0 (future old-format compat)")]
    public void VersionEnvelope_Version0_HandledByUnknown()
    {
        // The CodecFormats use CodecConstants values which are all 1.
        // Version 0 would be handled by the unknown delegate.
        var codec = CodecFormats.Postings;
        byte[] body = [0x42];
        // Write manually with version=0 to a temp file
        var path = Path.Combine(_fixture.Path, $"v0_{Guid.NewGuid():N}.dat");
        using (var output = new IndexOutput(path))
        {
            output.WriteByte(0); // version 0
            WriteZigZagVarInt64(output, body.Length);
            output.WriteBytes(body);
            output.Flush();
        }

        // Read should succeed via unknown delegate (returns raw bytes)
        using var input = new IndexInput(path);
        var result = CodecFileHeader.Read(input, codec);
        // The unknown delegate for CodecFormats returns the raw body
        Assert.Equal(body.Length, result.Body.Length);
        Assert.Equal(body[0], result.Body[0]);
    }

    [Fact(DisplayName = "BinaryReader-based read of version-0 file succeeds")]
    public void BinaryReader_Version0_Succeeds()
    {
        var path = Path.Combine(_fixture.Path, $"br_v0_{Guid.NewGuid():N}.dat");
        byte[] body = [0xAA, 0xBB];

        using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            fs.WriteByte(0); // version 0
            WriteZigZagVarInt64(fs, body.Length);
            fs.Write(body);
        }

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
        {
            var result = CodecFileHeader.Read(reader, CodecFormats.Postings);
            Assert.Equal(0, result.Version);
            Assert.Equal(body, result.Body);
        }
    }
    // ═══════════════════════════════════════════════════
    //  Format round-trip
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "All CodecFormats decode correctly with BinaryReader round-trip")]
    public void AllCodecFormats_BinaryReader_RoundTrip()
    {
        var path = Path.Combine(_fixture.Path, $"br_allfmts_{Guid.NewGuid():N}.dat");
        byte[] body = [0x01, 0x02, 0x03];

        var formats = new[]
        {
            (CodecFormats.Postings, CodecConstants.PostingsVersion),
            (CodecFormats.StoredFields, CodecConstants.StoredFieldsVersion),
            (CodecFormats.TermDictionary, CodecConstants.TermDictionaryVersion),
            (CodecFormats.TermVectors, CodecConstants.TermVectorsVersion),
        };

        foreach (var (format, expectedVersion) in formats)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(fs, System.Text.Encoding.UTF8, leaveOpen: false))
                CodecFileHeader.Write(writer, format, body);

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new BinaryReader(fs, System.Text.Encoding.UTF8, leaveOpen: false))
            {
                var result = CodecFileHeader.Read(reader, format);
                Assert.Equal(expectedVersion, result.Version);
                Assert.Equal(body, result.Body);
            }
        }
    }

    private static void WriteZigZagVarInt64(Stream stream, long value)
    {
        ulong zigzag = (ulong)((value << 1) ^ (value >> 63));
        while (zigzag >= 0x80)
        {
            stream.WriteByte((byte)(zigzag | 0x80));
            zigzag >>= 7;
        }
        stream.WriteByte((byte)zigzag);
    }

    private static void WriteZigZagVarInt64(IndexOutput output, long value)
    {
        ulong zigzag = (ulong)((value << 1) ^ (value >> 63));
        while (zigzag >= 0x80)
        {
            output.WriteByte((byte)(zigzag | 0x80));
            zigzag >>= 7;
        }
        output.WriteByte((byte)zigzag);
    }
}
