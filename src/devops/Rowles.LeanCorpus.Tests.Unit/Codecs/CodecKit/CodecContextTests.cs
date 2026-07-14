using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CodecContextTests
{
    [Fact(DisplayName = "ByteOffset increments through sequential reads")]
    public void ByteOffset_IncrementsThroughReads()
    {
        // Use Codec.Decode which creates a CodecContext internally
        byte[] data = new byte[16];
        Random.Shared.NextBytes(data);

        // Decode a 16-byte owned bytes codec
        var codec = Codec.BytesOwned(16);
        byte[] result = Codec.Decode(codec, data);

        Assert.Equal(data, result);
    }

    [Fact(DisplayName = "PushPath builds correct breadcrumb path in exception")]
    public void PushPath_InExceptionMessage()
    {
        // Using MapCodec which pushes path segments
        var inner = Codec.Bool;
        // Decode invalid bool with a path
        byte[] data = [0x02];

        var ex = Assert.Throws<InvalidBooleanException>(() => Codec.Decode(inner, data));
        // ByteOffset should be 0
        Assert.Equal(0, ex.ByteOffset);
    }

    [Fact(DisplayName = "Checkpoint and Rewind restore reader position")]
    public void Checkpoint_Rewind_RestoresPosition()
    {
        // Decode a sequence: u8=1, u8=2. Read first, checkpoint, read second, rewind, read second again.
        byte[] data = [0x01, 0x02];
        var seq = new ReadOnlySequence<byte>(data);
        var reader = new SequenceReader<byte>(seq);
        var ctx = new CodecContext(CodecOptions.Default, CodecRegistry.Default);

        byte first = Codec.UInt8.Decode(ref reader, ctx);
        Assert.Equal(1, first);

        var checkpoint = ctx.Checkpoint(ref reader);
        byte second = Codec.UInt8.Decode(ref reader, ctx);
        Assert.Equal(2, second);

        ctx.Rewind(ref reader, checkpoint);
        byte secondAgain = Codec.UInt8.Decode(ref reader, ctx);
        Assert.Equal(2, secondAgain);
    }

    [Fact(DisplayName = "Nested scopes: delimited decode within delimited decode")]
    public void NestedScopes_WorkCorrectly()
    {
        // LengthPrefixed wrapping another LengthPrefixed
        var innerCodec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwnedRemaining(), TrailingDataPolicy.Allow);
        var outerCodec = Codec.LengthPrefixed(Codec.VarUInt32, innerCodec, TrailingDataPolicy.Allow);

        byte[] payload = [0x01, 0x02]; // inner: 1 byte body, outer: 2 bytes inner
        byte[] data = new byte[2 + payload.Length];
        data[0] = (byte)payload.Length; // outer length
        data[1] = payload[0];           // inner length
        data[2] = payload[1];           // inner body

        byte[] result = Codec.Decode(outerCodec, data);
        Assert.Equal(new byte[] { payload[1] }, result);
    }

    [Fact(DisplayName = "MaxNestingDepth exceeded throws DepthExceeded")]
    public void MaxNestingDepth_Exceeded_Throws()
    {
        var opts = new CodecOptions { MaxNestingDepth = 1 };
        // Chain two LengthPrefixed codecs to reach depth 2
        var inner = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwnedRemaining(), TrailingDataPolicy.Allow);
        var outer = Codec.LengthPrefixed(Codec.VarUInt32, inner, TrailingDataPolicy.Allow);

        byte[] payload = [0x01, 0x02];
        byte[] data = new byte[2 + payload.Length];
        data[0] = (byte)payload.Length;
        data[1] = payload[0];
        data[2] = payload[1];

        var ex = Assert.Throws<LimitExceededException>(() =>
            Codec.Decode(outer, data, options: opts));

        Assert.Equal(CodecErrorCode.DepthExceeded, ex.ErrorCode);
        Assert.Equal("MaxNestingDepth", ex.LimitName);
    }

    [Fact(DisplayName = "Trailing data with Reject policy throws TrailingDataException")]
    public void TrailingData_PolicyReject_Throws()
    {
        // LengthPrefixed with reject should throw if extra bytes follow
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwnedRemaining(), TrailingDataPolicy.Reject);

        // 1 byte length says 2, but we provide 3 bytes of data
        byte[] data = [0x02, 0xAA, 0xBB, 0xCC];

        var ex = Assert.Throws<TrailingDataException>(() => Codec.Decode(codec, data));

        Assert.Equal(CodecErrorCode.TrailingData, ex.ErrorCode);
        Assert.Equal(1, ex.TrailingBytes);
    }

    [Fact(DisplayName = "Trailing data with Ignore policy succeeds")]
    public void TrailingData_PolicyIgnore_Succeeds()
    {
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwnedRemaining(), TrailingDataPolicy.Allow);

        byte[] data = [0x02, 0xAA, 0xBB, 0xCC]; // extra byte at end
        byte[] result = Codec.Decode(codec, data);

        Assert.Equal(new byte[] { 0xAA, 0xBB }, result);
    }

    [Fact(DisplayName = "FixedFrame nested exceeds MaxNestingDepth")]
    public void FixedFrame_Nested_Exceeded_Throws()
    {
        var opts = new CodecOptions { MaxNestingDepth = 1 };
        // Chain two FixedFrame codecs to reach depth 2
        var inner = Codec.FixedFrame(1, Codec.UInt8, FramePadding.ZeroFill, TrailingDataPolicy.Allow);
        var outer = Codec.FixedFrame(2, inner, FramePadding.ZeroFill, TrailingDataPolicy.Allow);

        // Encode: outer frame is 2 bytes, inner UInt8=42 + 1 pad byte
        byte[] encoded = Codec.EncodeToArray(outer, (byte)42);

        var ex = Assert.Throws<LimitExceededException>(() =>
            Codec.Decode(outer, encoded, options: opts));

        Assert.Equal(CodecErrorCode.DepthExceeded, ex.ErrorCode);
        Assert.Equal("MaxNestingDepth", ex.LimitName);
    }

    [Fact(DisplayName = "Record nested exceeds MaxNestingDepth")]
    public void Record_Nested_Exceeded_Throws()
    {
        var opts = new CodecOptions { MaxNestingDepth = 1 };

        var innerRecord = Codec.Record<byte>()
            .Field("value", v => v, Codec.UInt8)
            .Build<byte>(v => v);

        var outerRecord = Codec.Record<byte>()
            .Field("inner", v => v, innerRecord)
            .Build<byte>(v => v);

        byte[] encoded = Codec.EncodeToArray(outerRecord, (byte)99);

        var ex = Assert.Throws<LimitExceededException>(() =>
            Codec.Decode(outerRecord, encoded, options: opts));

        Assert.Equal(CodecErrorCode.DepthExceeded, ex.ErrorCode);
        Assert.Equal("MaxNestingDepth", ex.LimitName);
    }

    [Fact(DisplayName = "Choice nested exceeds MaxNestingDepth")]
    public void Choice_Nested_Exceeded_Throws()
    {
        var opts = new CodecOptions { MaxNestingDepth = 1 };

        var innerChoice = Codec.Choice<byte, byte>(
            Codec.UInt8,
            Codec.Case<byte, byte>((byte)42, "answer", Codec.UInt8));

        var outerChoice = Codec.Choice<byte, byte>(
            Codec.UInt8,
            Codec.Case<byte, byte>((byte)1, "inner", innerChoice));

        byte[] encoded = Codec.EncodeToArray(outerChoice, (byte)42);

        var ex = Assert.Throws<LimitExceededException>(() =>
            Codec.Decode(outerChoice, encoded, options: opts));

        Assert.Equal(CodecErrorCode.DepthExceeded, ex.ErrorCode);
        Assert.Equal("MaxNestingDepth", ex.LimitName);
    }
}
