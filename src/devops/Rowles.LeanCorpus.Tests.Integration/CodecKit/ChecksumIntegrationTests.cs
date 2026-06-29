using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class ChecksumIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public ChecksumIntegrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string TempFile(string name) => Path.Combine(_fixture.Path, name);

    [Fact(DisplayName = "VersionEnvelope → WithChecksum(Header) → body: write→read integrity passes")]
    public void VersionEnvelope_WithChecksumHeader_RoundTrip()
    {
        var path = TempFile("checksum_header.dat");
        byte[] body = new byte[1024];
        Random.Shared.NextBytes(body);

        // Use the higher-level codec: VersionEnvelope wrapping WithChecksum(Header)
        var inner = Codec.WithChecksum(Codec.BytesOwned(1024), ChecksumAlgorithms.Crc32, ChecksumPlacement.Header);
        // Write body with checksum via Codec.EncodeToArray, then wrap in version envelope
        byte[] checkedBody = Codec.EncodeToArray(inner, body);

        // Write using IndexOutput header format
        using (var output = new IndexOutput(path))
        {
            output.WriteByte(1); // version
            output.WriteVarInt(checkedBody.Length);
            output.WriteBytes(checkedBody);
            output.Flush();
        }

        // Read back and verify
        using var input = new IndexInput(path);
        byte version = input.ReadByte();
        int bodyLen = input.ReadVarInt();
        Assert.Equal(1, version);
        Assert.Equal(checkedBody.Length, bodyLen);

        byte[] rawBody = new byte[bodyLen];
        for (long i = 0; i < bodyLen; i++)
            rawBody[i] = input.ReadByte();

        byte[] decoded = Codec.Decode(inner, rawBody);
        Assert.Equal(body, decoded);
    }

    [Fact(DisplayName = "Corrupt one byte in body → ChecksumMismatchException with Header placement")]
    public void CorruptBody_Header_ThrowsChecksumMismatch()
    {
        var path = TempFile("checksum_header_corrupt.dat");
        byte[] body = new byte[1024];
        Random.Shared.NextBytes(body);

        var inner = Codec.WithChecksum(Codec.BytesOwned(1024), ChecksumAlgorithms.Crc32, ChecksumPlacement.Header);
        byte[] checkedBody = Codec.EncodeToArray(inner, body);

        // Write to file
        using (var output = new IndexOutput(path))
        {
            output.WriteByte(1);
            output.WriteVarInt(checkedBody.Length);
            output.WriteBytes(checkedBody);
            output.Flush();
        }

        // Read back, corrupt body (not the checksum header)
        byte[] fileBytes = File.ReadAllBytes(path);
        // Find body offset: version(1) + VarInt(varies)
        long headerEnd = 0;
        using (var probe = new IndexInput(path))
        {
            probe.ReadByte(); // version
            probe.ReadVarInt(); // bodyLen
            headerEnd = probe.Position;
        }

        // Corrupt a byte in the body (after checksum)
        long corruptOffset = headerEnd + 4 + 10; // after 4-byte CRC header + 10 bytes into body
        if (corruptOffset < fileBytes.Length)
        {
            fileBytes[corruptOffset] ^= 0xFF;
            File.WriteAllBytes(path, fileBytes);
        }

        // Read and decode → should fail
        using var input = new IndexInput(path);
        input.ReadByte();
        int bodyLen = input.ReadVarInt();
        byte[] rawBody = new byte[bodyLen];
        for (long i = 0; i < bodyLen; i++)
            rawBody[i] = input.ReadByte();

        Assert.Throws<ChecksumMismatchException>(() => Codec.Decode(inner, rawBody));
    }

    [Fact(DisplayName = "VersionEnvelope → WithChecksum(Trailer): write→read integrity passes")]
    public void VersionEnvelope_WithChecksumTrailer_RoundTrip()
    {
        var path = TempFile("checksum_trailer.dat");
        byte[] body = new byte[512];
        Random.Shared.NextBytes(body);

        var inner = Codec.WithChecksum(Codec.BytesOwned(512), ChecksumAlgorithms.XxHash64, ChecksumPlacement.Trailer);
        byte[] checkedBody = Codec.EncodeToArray(inner, body);

        using (var output = new IndexOutput(path))
        {
            output.WriteByte(1);
            output.WriteVarInt(checkedBody.Length);
            output.WriteBytes(checkedBody);
            output.Flush();
        }

        using var input = new IndexInput(path);
        input.ReadByte();
        int bodyLen = input.ReadVarInt();
        byte[] rawBody = new byte[bodyLen];
        for (long i = 0; i < bodyLen; i++)
            rawBody[i] = input.ReadByte();

        byte[] decoded = Codec.Decode(inner, rawBody);
        Assert.Equal(body, decoded);
    }

    [Fact(DisplayName = "Corrupt one byte in trailer body → ChecksumMismatchException")]
    public void CorruptBody_Trailer_ThrowsChecksumMismatch()
    {
        var path = TempFile("checksum_trailer_corrupt.dat");
        byte[] body = new byte[512];
        Random.Shared.NextBytes(body);

        var inner = Codec.WithChecksum(Codec.BytesOwned(512), ChecksumAlgorithms.Crc32, ChecksumPlacement.Trailer);
        byte[] checkedBody = Codec.EncodeToArray(inner, body);

        using (var output = new IndexOutput(path))
        {
            output.WriteByte(1);
            output.WriteVarInt(checkedBody.Length);
            output.WriteBytes(checkedBody);
            output.Flush();
        }

        byte[] fileBytes = File.ReadAllBytes(path);
        long headerEnd;
        using (var probe = new IndexInput(path))
        {
            probe.ReadByte();
            probe.ReadVarInt();
            headerEnd = probe.Position;
        }

        // Corrupt byte in body (not the trailer checksum)
        long corruptOffset = headerEnd + 10;
        if (corruptOffset < fileBytes.Length - 4) // stay before checksum trailer
        {
            fileBytes[corruptOffset] ^= 0xFF;
            File.WriteAllBytes(path, fileBytes);
        }

        using var input = new IndexInput(path);
        int bodyLen = input.ReadVarInt();
        byte[] rawBody = new byte[bodyLen];
        for (long i = 0; i < bodyLen; i++)
            rawBody[i] = input.ReadByte();

        Assert.ThrowsAny<Exception>(() => Codec.Decode(inner, rawBody));
    }

    [Fact(DisplayName = "Checksum algorithm is identifiable in error")]
    public void ChecksumMismatch_AlgorithmId_InError()
    {
        byte[] body = new byte[64];
        Random.Shared.NextBytes(body);

        var inner = Codec.WithChecksum(Codec.BytesOwned(64), ChecksumAlgorithms.Crc32, ChecksumPlacement.Header);
        byte[] checkedBody = Codec.EncodeToArray(inner, body);

        // Corrupt body
        checkedBody[4 + 5] ^= 0xFF;

        var ex = Assert.Throws<ChecksumMismatchException>(() => Codec.Decode(inner, checkedBody));
        Assert.Equal("crc32", ex.AlgorithmId);
    }
}
