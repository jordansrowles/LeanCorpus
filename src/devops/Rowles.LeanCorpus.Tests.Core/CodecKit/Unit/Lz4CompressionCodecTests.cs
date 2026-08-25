using Rowles.LeanCorpus.Compression.LZ4;
using Rowles.LeanCorpus.Codecs.StoredFields;

namespace Rowles.LeanCorpus.Tests.Core.Codecs;

/// <summary>
/// Unit tests for <see cref="Lz4CompressionCodec"/>.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.CodecKit)]
public sealed class Lz4CompressionCodecTests
{
    private readonly Lz4CompressionCodec _codec = new();

    [Fact(DisplayName = "Lz4CompressionCodec: PolicyByte returns Lz4 policy")]
    public void PolicyByte_IsLz4()
    {
        Assert.Equal((byte)FieldCompressionPolicy.Lz4, _codec.PolicyByte);
    }

    [Fact(DisplayName = "Lz4CompressionCodec: Compress empty span returns empty array")]
    public void Compress_EmptyInput_ReturnsEmpty()
    {
        byte[] result = _codec.Compress([]);

        Assert.Empty(result);
    }

    [Fact(DisplayName = "Lz4CompressionCodec: Compress small payload round-trips correctly")]
    public void Compress_SmallPayload_RoundTrips()
    {
        byte[] original = "hello world"u8.ToArray();

        byte[] compressed = _codec.Compress(original);
        byte[] decompressed = _codec.Decompress(compressed, original.Length);

        Assert.Equal(original, decompressed);
    }

    [Fact(DisplayName = "Lz4CompressionCodec: Compress large payload round-trips correctly")]
    public void Compress_LargePayload_RoundTrips()
    {
        byte[] original = new byte[65_536];
        for (int i = 0; i < original.Length; i++)
            original[i] = (byte)(i % 256);

        byte[] compressed = _codec.Compress(original);
        byte[] decompressed = _codec.Decompress(compressed, original.Length);

        Assert.Equal(original, decompressed);
    }

    [Fact(DisplayName = "Lz4CompressionCodec: Decompress negative originalSize throws ArgumentOutOfRangeException")]
    public void Decompress_NegativeOriginalSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _codec.Decompress([], -1));
    }

    [Fact(DisplayName = "Lz4CompressionCodec: Decompress zero originalSize with empty compressed returns empty")]
    public void Decompress_ZeroOriginalSizeEmptyCompressed_ReturnsEmpty()
    {
        byte[] result = _codec.Decompress([], 0);

        Assert.Empty(result);
    }

    [Fact(DisplayName = "Lz4CompressionCodec: Decompress zero originalSize with non-empty compressed throws InvalidDataException")]
    public void Decompress_ZeroOriginalSizeNonEmptyCompressed_Throws()
    {
        byte[] junk = [0x01, 0x02, 0x03];

        Assert.Throws<InvalidDataException>(() => _codec.Decompress(junk, 0));
    }

    [Fact(DisplayName = "Lz4CompressionCodec: Decompress with wrong originalSize throws InvalidDataException")]
    public void Decompress_WrongOriginalSize_Throws()
    {
        byte[] original = "hello world"u8.ToArray();
        byte[] compressed = _codec.Compress(original);

        // Claim the original was much larger than it really is.
        Assert.Throws<InvalidDataException>(() => _codec.Decompress(compressed, original.Length + 100));
    }
}
