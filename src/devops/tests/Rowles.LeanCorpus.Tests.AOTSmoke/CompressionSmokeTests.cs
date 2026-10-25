using Rowles.LeanCorpus.Codecs.StoredFields;
using Rowles.LeanCorpus.Compression.LZ4;
using Rowles.LeanCorpus.Compression.Snappy;
using Rowles.LeanCorpus.Compression.Zstandard;
using Xunit;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

public class CompressionSmokeTests
{
    [Fact]
    public void Lz4Codec_RegisteredAfterExplicitRegister()
    {
        Lz4Compression.Register();
        Assert.True(
            CompressionCodecRegistry.TryGet((byte)FieldCompressionPolicy.Lz4, out var codec));
        Assert.NotNull(codec);
    }

    [Fact]
    public void SnappyCodec_RegisteredAfterExplicitRegister()
    {
        SnappyCompression.Register();
        Assert.True(
            CompressionCodecRegistry.TryGet((byte)FieldCompressionPolicy.Snappy, out var codec));
        Assert.NotNull(codec);
    }

    [Fact]
    public void ZstandardCodec_RegisteredAfterExplicitRegister()
    {
        ZstandardCompression.Register();
        Assert.True(
            CompressionCodecRegistry.TryGet((byte)FieldCompressionPolicy.Zstandard, out var codec));
        Assert.NotNull(codec);
    }
}
