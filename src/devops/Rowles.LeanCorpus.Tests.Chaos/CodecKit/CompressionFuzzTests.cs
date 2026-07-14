using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.CodecKit;

[Trait("Category", "Chaos")]
[Trait("Category", "CodecKit")]
public sealed class CompressionFuzzTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public CompressionFuzzTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property(MaxTest = 50)]
    public void AnyByteArray_CompressDecompress_Identical(byte[] data)
    {
        var codec = Codec.WithCompression(
            Codec.BytesOwned(data.Length));

        byte[] compressed = Codec.EncodeToArray(codec, data);
        byte[] decompressed = Codec.Decode(codec, compressed);

        Assert.Equal(data, decompressed);
    }

    [Fact(DisplayName = "Empty byte[] → compress → decompress → empty")]
    public void EmptyArray_CompressDecompress_Empty()
    {
        var codec = Codec.WithCompression(
            Codec.BytesOwned(0));

        byte[] compressed = Codec.EncodeToArray(codec, Array.Empty<byte>());
        byte[] decompressed = Codec.Decode(codec, compressed);

        Assert.Empty(decompressed);
    }

    [Property(MaxTest = 30)]
    public void IncompressibleRandomData_DecompressesCorrectly(byte[] data)
    {
        var codec = Codec.WithCompression(
            Codec.BytesOwned(data.Length));

        byte[] compressed = Codec.EncodeToArray(codec, data);
        byte[] decompressed = Codec.Decode(codec, compressed);

        Assert.Equal(data, decompressed);
    }
}
