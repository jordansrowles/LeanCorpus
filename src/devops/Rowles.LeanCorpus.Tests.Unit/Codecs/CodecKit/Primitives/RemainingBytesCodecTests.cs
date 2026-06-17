using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Primitives;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Primitives;

[Trait("Category", "CodecKit")]
public sealed class RemainingBytesCodecTests
{
    // ═══════════════════════════════════════════════════
    //  Instance
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "RemainingBytesCodec: Instance is accessible and returns a RemainingBytesCodec")]
    public void Instance_IsAccessible()
    {
        var instance = RemainingBytesCodec.Instance;
        Assert.NotNull(instance);
        Assert.IsType<RemainingBytesCodec>(instance);
    }

    [Fact(DisplayName = "RemainingBytesCodec: Instance is a singleton")]
    public void Instance_IsSingleton()
    {
        Assert.Same(RemainingBytesCodec.Instance, RemainingBytesCodec.Instance);
    }

    // ═══════════════════════════════════════════════════
    //  Round-trip
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "RemainingBytesCodec: Round-trip arbitrary byte array")]
    public void RoundTrip_ArbitraryBytes()
    {
        byte[] value = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04];
        byte[] encoded = Codec.EncodeToArray(RemainingBytesCodec.Instance, value);
        byte[] decoded = Codec.Decode(RemainingBytesCodec.Instance, encoded);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "RemainingBytesCodec: Round-trip empty array")]
    public void RoundTrip_EmptyArray()
    {
        byte[] value = [];
        byte[] encoded = Codec.EncodeToArray(RemainingBytesCodec.Instance, value);
        byte[] decoded = Codec.Decode(RemainingBytesCodec.Instance, encoded);
        Assert.Empty(decoded);
    }

    [Fact(DisplayName = "RemainingBytesCodec: Round-trip single byte")]
    public void RoundTrip_SingleByte()
    {
        byte[] value = [0xAB];
        byte[] encoded = Codec.EncodeToArray(RemainingBytesCodec.Instance, value);
        byte[] decoded = Codec.Decode(RemainingBytesCodec.Instance, encoded);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "RemainingBytesCodec: Round-trip large array (10,240 bytes)")]
    public void RoundTrip_LargeArray()
    {
        var value = new byte[10_240];
        Random.Shared.NextBytes(value);
        byte[] encoded = Codec.EncodeToArray(RemainingBytesCodec.Instance, value);
        byte[] decoded = Codec.Decode(RemainingBytesCodec.Instance, encoded);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "RemainingBytesCodec: Round-trip max-size small array (255 bytes)")]
    public void RoundTrip_255Bytes()
    {
        var value = new byte[255];
        for (int i = 0; i < value.Length; i++) value[i] = (byte)i;
        byte[] encoded = Codec.EncodeToArray(RemainingBytesCodec.Instance, value);
        byte[] decoded = Codec.Decode(RemainingBytesCodec.Instance, encoded);
        Assert.Equal(value, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  Decode behaviour
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "RemainingBytesCodec: Decode on empty sequence returns empty array")]
    public void Decode_EmptySequence_ReturnsEmptyArray()
    {
        var seq = new ReadOnlySequence<byte>([]);
        var reader = new SequenceReader<byte>(seq);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        byte[] result = RemainingBytesCodec.Instance.Decode(ref reader, ctx);
        Assert.Empty(result);
    }

    [Fact(DisplayName = "RemainingBytesCodec: Decode consumes all remaining bytes (reader.Remaining is 0 after)")]
    public void Decode_ConsumesAllBytes()
    {
        byte[] data = [0xAA, 0xBB, 0xCC, 0xDD];
        var seq = new ReadOnlySequence<byte>(data);
        var reader = new SequenceReader<byte>(seq);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        byte[] result = RemainingBytesCodec.Instance.Decode(ref reader, ctx);
        Assert.Equal(data, result);
        Assert.Equal(0, reader.Remaining);
    }

    [Fact(DisplayName = "RemainingBytesCodec: Decode from partial sequence consumes only remaining")]
    public void Decode_FromPartialSequence()
    {
        // Create a sequence where only part of the data is remaining
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06];
        var seq = new ReadOnlySequence<byte>(data);
        var reader = new SequenceReader<byte>(seq);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        // Advance past the first 2 bytes
        reader.Advance(2);
        Assert.Equal(4, reader.Remaining);

        byte[] result = RemainingBytesCodec.Instance.Decode(ref reader, ctx);
        Assert.Equal(new byte[] { 0x03, 0x04, 0x05, 0x06 }, result);
        Assert.Equal(0, reader.Remaining);
    }

    // ═══════════════════════════════════════════════════
    //  Encode behaviour
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "RemainingBytesCodec: Encode null writes empty span")]
    public void Encode_Null_WritesEmptySpan()
    {
        var buffer = new ArrayBufferWriter<byte>();
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        RemainingBytesCodec.Instance.Encode(null!, buffer, ctx);

        Assert.Equal(0, buffer.WrittenCount);
    }

    // ═══════════════════════════════════════════════════
    //  Public API equivalence
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "RemainingBytesCodec: Codec.Decode and Codec.EncodeToArray work with Instance")]
    public void StaticApi_WorksWithInstance()
    {
        byte[] value = [0x11, 0x22, 0x33];
        byte[] encoded = Codec.EncodeToArray(RemainingBytesCodec.Instance, value);
        byte[] decoded = Codec.Decode(RemainingBytesCodec.Instance, encoded);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "RemainingBytesCodec: Encoded length equals source length")]
    public void EncodedLength_EqualsSourceLength()
    {
        byte[] value = [0x01, 0x02, 0x03, 0x04, 0x05];
        byte[] encoded = Codec.EncodeToArray(RemainingBytesCodec.Instance, value);
        Assert.Equal(value.Length, encoded.Length);
    }
}
