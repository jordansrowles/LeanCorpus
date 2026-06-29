using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CompressionIntegrationTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public CompressionIntegrationTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string TempFile(string name) => Path.Combine(_fixture.Path, name);

    [Fact(DisplayName = "VersionEnvelope → WithCompression → body: decompressed matches original")]
    public void VersionEnvelope_WithCompression_RoundTrip()
    {
        var path = TempFile("compress_roundtrip.dat");
        byte[] body = new byte[2048];
        Random.Shared.NextBytes(body);

        var inner = Codec.WithCompression(Codec.BytesOwned(2048));
        byte[] compressed = Codec.EncodeToArray(inner, body);

        // Write with header
        using (var output = new IndexOutput(path))
        {
            output.WriteByte(1);
            output.WriteVarInt(compressed.Length);
            output.WriteBytes(compressed);
            output.Flush();
        }

        // Read back
        using var input = new IndexInput(path);
        input.ReadByte();
        int bodyLen = input.ReadVarInt();
        byte[] rawBody = new byte[bodyLen];
        for (long i = 0; i < bodyLen; i++)
            rawBody[i] = input.ReadByte();

        byte[] decompressed = Codec.Decode(inner, rawBody);
        Assert.Equal(body, decompressed);
    }

    [Fact(DisplayName = "Large body (>64KB) compresses and decompresses correctly")]
    public void LargeBody_CompressDecompress_RoundTrip()
    {
        byte[] body = new byte[128_000];
        // Use repeating pattern for good compression
        for (int i = 0; i < body.Length; i++)
            body[i] = (byte)(i % 256);

        var inner = Codec.WithCompression(Codec.BytesOwned(128_000));
        byte[] compressed = Codec.EncodeToArray(inner, body);
        byte[] decompressed = Codec.Decode(inner, compressed);

        Assert.Equal(body, decompressed);
        // Should compress significantly for repeating data
        Assert.True(compressed.Length < body.Length,
            $"Expected compressed size ({compressed.Length}) < original ({body.Length})");
    }

    [Fact(DisplayName = "Incompressible random data decompresses correctly")]
    public void IncompressibleData_DecompressesCorrectly()
    {
        byte[] body = new byte[4096];
        Random.Shared.NextBytes(body); // random data is hard to compress

        var inner = Codec.WithCompression(Codec.BytesOwned(4096));
        byte[] compressed = Codec.EncodeToArray(inner, body);
        byte[] decompressed = Codec.Decode(inner, compressed);

        Assert.Equal(body, decompressed);
    }

    [Fact(DisplayName = "Decompressed size exceeding limit throws")]
    public void DecompressedSize_ExceedsLimit_Throws()
    {
        var opts = new CodecOptions { MaxDecompressedBytes = 100 };
        byte[] body = new byte[10_000]; // will decompress to 10KB > 100 byte limit
        Random.Shared.NextBytes(body);

        var codec = Codec.WithCompression(Codec.BytesOwned(10_000));
        byte[] compressed = Codec.EncodeToArray(codec, body);

        var ex = Assert.Throws<LimitExceededException>(() =>
            Codec.Decode(codec, compressed, options: opts));

        Assert.Equal(CodecErrorCode.DecompressionLimitExceeded, ex.ErrorCode);
    }

    [Fact(DisplayName = "Empty body compresses and decompresses correctly")]
    public void EmptyBody_CompressDecompress_RoundTrip()
    {
        byte[] body = [];

        var inner = Codec.WithCompression(Codec.BytesOwned(0));
        byte[] compressed = Codec.EncodeToArray(inner, body);
        byte[] decompressed = Codec.Decode(inner, compressed);

        Assert.Empty(decompressed);
    }
}
