using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Checksum;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class CombinatorCodecTests
{
    // ═══════════════════════════════════════════════════
    //  LengthPrefixedCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "LengthPrefixedCodec empty body (len=0)")]
    public void LengthPrefixed_EmptyBody()
    {
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwnedRemaining(), TrailingDataPolicy.Allow);
        byte[] data = [0x00]; // length = 0
        byte[] result = Codec.Decode(codec, data);
        Assert.Empty(result);
    }

    [Fact(DisplayName = "LengthPrefixedCodec exact-length body")]
    public void LengthPrefixed_ExactLengthBody()
    {
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwnedRemaining(), TrailingDataPolicy.Allow);
        byte[] data = [0x03, 0xAA, 0xBB, 0xCC];
        byte[] result = Codec.Decode(codec, data);
        Assert.Equal(new byte[] { 0xAA, 0xBB, 0xCC }, result);
    }

    [Fact(DisplayName = "LengthPrefixedCodec round-trip")]
    public void LengthPrefixed_RoundTrip()
    {
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwnedRemaining(), TrailingDataPolicy.Allow);
        byte[] value = new byte[256];
        Random.Shared.NextBytes(value);

        byte[] encoded = Codec.EncodeToArray(codec, value);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "LengthPrefixedCodec trailing data with Fail throws TrailingDataException")]
    public void LengthPrefixed_TrailingData_Fail_Throws()
    {
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwned(1), TrailingDataPolicy.Reject);
        byte[] data = [0x01, 0xAA, 0xBB];

        var ex = Assert.Throws<TrailingDataException>(() => Codec.Decode(codec, data));
        Assert.Equal(CodecErrorCode.TrailingData, ex.ErrorCode);
    }

    [Fact(DisplayName = "LengthPrefixedCodec trailing data with Ignore succeeds")]
    public void LengthPrefixed_TrailingData_Ignore_Succeeds()
    {
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwnedRemaining(), TrailingDataPolicy.Allow);
        byte[] data = [0x01, 0xAA, 0xBB]; // len=1, extra byte at end

        byte[] result = Codec.Decode(codec, data);
        Assert.Equal(new byte[] { 0xAA }, result);
    }

    // ═══════════════════════════════════════════════════
    //  FixedFrameCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "FixedFrameCodec exact-match body")]
    public void FixedFrame_ExactMatch()
    {
        var codec = Codec.FixedFrame(4, Codec.BytesOwnedRemaining(), FramePadding.Exact, TrailingDataPolicy.Allow);
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        byte[] result = Codec.Decode(codec, data);
        Assert.Equal(data, result);
    }

    [Fact(DisplayName = "FixedFrameCodec body shorter than frame (zero-fill padding)")]
    public void FixedFrame_Shorter_ZeroFill()
    {
        // Encode: frame=5, body=3 → padded with 0x00
        var codec = Codec.FixedFrame(5, Codec.BytesOwned(3), FramePadding.ZeroFill, TrailingDataPolicy.Allow);
        byte[] value = [0xAA, 0xBB, 0xCC];
        byte[] encoded = Codec.EncodeToArray(codec, value);
        byte[] decoded = Codec.Decode(codec, encoded);
        Assert.Equal(value, decoded);
        Assert.Equal(5, encoded.Length);
        Assert.Equal(0x00, encoded[3]);
        Assert.Equal(0x00, encoded[4]);
    }

    [Fact(DisplayName = "FixedFrameCodec body shorter than frame (0xAB fill padding)")]
    public void FixedFrame_Shorter_ByteFill()
    {
        var padding = FramePadding.ByteFill(0xAB);
        var codec = Codec.FixedFrame(5, Codec.BytesOwned(3), padding, TrailingDataPolicy.Allow);
        byte[] value = [0x01, 0x02, 0x03];
        byte[] encoded = Codec.EncodeToArray(codec, value);
        byte[] decoded = Codec.Decode(codec, encoded);
        Assert.Equal(value, decoded);
        Assert.Equal(5, encoded.Length);
        Assert.Equal(0xAB, encoded[3]);
        Assert.Equal(0xAB, encoded[4]);
    }

    [Fact(DisplayName = "FixedFrameCodec body longer than frame throws FrameOverflowException")]
    public void FixedFrame_Longer_Throws()
    {
        var codec = Codec.FixedFrame(2, Codec.BytesOwned(4), FramePadding.Exact, TrailingDataPolicy.Allow);
        byte[] value = [0x01, 0x02, 0x03, 0x04];

        var ex = Assert.Throws<FrameOverflowException>(() => Codec.EncodeToArray(codec, value));
        Assert.Equal(2, ex.FrameSize);
        Assert.Equal(4, ex.PayloadSize);
    }

    [Fact(DisplayName = "FixedFrameCodec padding byte mismatch throws InvalidPaddingException")]
    public void FixedFrame_PaddingByte_Mismatch_Throws()
    {
        // Frame expects 0x00 padding, provide 0xFF instead
        byte[] data = [0x01, 0x02, 0xFF, 0xFF]; // frame=4, body is 2 bytes (must match)
        // We need an exact 2-byte body codec inside a 4-byte frame with ZeroFill
        var codec = Codec.FixedFrame(4, Codec.BytesOwned(2), FramePadding.ZeroFill, TrailingDataPolicy.Allow);

        var ex = Assert.Throws<InvalidPaddingException>(() => Codec.Decode(codec, data));
        Assert.Equal(0x00, ex.ExpectedByte);
        Assert.NotNull(ex.ActualByte);
    }

    // ═══════════════════════════════════════════════════
    //  VersionEnvelopeCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "VersionEnvelopeCodec known version dispatches to correct decoder")]
    public void VersionEnvelope_KnownVersion_DecodesCorrectly()
    {
        var codec = Codec.VersionEnvelope<byte[], byte>(
            Codec.UInt8,
            Codec.VarInt64,
            unknown: (byte ver, byte[] body) => body,
            Codec.VersionCase<byte[], byte[]>((byte)1, "v1", Codec.BytesOwnedRemaining()));

        // Write: version=1, bodyLen=4, body = uint 42 LE
        byte[] body = Codec.EncodeToArray(Codec.UInt32LE, 42u);

        // Encode whole envelope
        byte[] encoded = Codec.EncodeToArray(codec, body);

        // Decode
        byte[] decoded = Codec.Decode(codec, encoded);
        Assert.Equal(body, decoded);
    }

    [Fact(DisplayName = "VersionEnvelopeCodec unknown version invokes unknown delegate")]
    public void VersionEnvelope_UnknownVersion_InvokesUnknown()
    {
        var codec = Codec.VersionEnvelope<byte[], byte>(
            Codec.UInt8,
            Codec.VarInt64,
            unknown: (byte ver, byte[] body) => body,
            Codec.VersionCase<byte[], byte[]>((byte)1, "v1", Codec.BytesOwnedRemaining()));
        // Write with version=5 (unknown)
        byte[] body = [0xAA, 0xBB, 0xCC];
        // Construct: version=5, proper VarInt64(bodyLen=3), body
        byte[] lenBytes = Codec.EncodeToArray(Codec.VarInt64, 3L);
        byte[] data = new byte[1 + lenBytes.Length + body.Length];
        data[0] = 5;
        Array.Copy(lenBytes, 0, data, 1, lenBytes.Length);
        Array.Copy(body, 0, data, 1 + lenBytes.Length, body.Length);

        byte[] decoded = Codec.Decode(codec, data);
        Assert.Equal(body, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  WithChecksumCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "WithChecksumCodec Header round-trip")]
    public void WithChecksum_Header_RoundTrip()
    {
        var codec = Codec.WithChecksum(
            Codec.BytesOwned(64),
            ChecksumAlgorithms.Crc32,
            ChecksumPlacement.Header);

        byte[] value = new byte[64];
        Random.Shared.NextBytes(value);

        byte[] encoded = Codec.EncodeToArray(codec, value);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "WithChecksumCodec Trailer round-trip")]
    public void WithChecksum_Trailer_RoundTrip()
    {
        var codec = Codec.WithChecksum(
            Codec.BytesOwned(128),
            ChecksumAlgorithms.XxHash64,
            ChecksumPlacement.Trailer);

        byte[] value = new byte[128];
        Random.Shared.NextBytes(value);

        byte[] encoded = Codec.EncodeToArray(codec, value);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "WithChecksumCodec corrupt body throws ChecksumMismatchException (Header)")]
    public void WithChecksum_CorruptBody_Header_Throws()
    {
        var codec = Codec.WithChecksum(
            Codec.BytesOwned(64),
            ChecksumAlgorithms.Crc32,
            ChecksumPlacement.Header);

        byte[] value = new byte[64];
        Random.Shared.NextBytes(value);

        byte[] encoded = Codec.EncodeToArray(codec, value);
        // Corrupt a byte in the body (after the 4-byte checksum header)
        encoded[4 + 10] ^= 0xFF;

        Assert.Throws<ChecksumMismatchException>(() => Codec.Decode(codec, encoded));
    }

    [Fact(DisplayName = "WithChecksumCodec corrupt body throws ChecksumMismatchException (Trailer)")]
    public void WithChecksum_CorruptBody_Trailer_Throws()
    {
        var codec = Codec.WithChecksum(
            Codec.BytesOwned(64),
            ChecksumAlgorithms.Crc32,
            ChecksumPlacement.Trailer);

        byte[] value = new byte[64];
        Random.Shared.NextBytes(value);

        byte[] encoded = Codec.EncodeToArray(codec, value);
        // Corrupt a byte in the body
        encoded[10] ^= 0xFF;

        Assert.Throws<ChecksumMismatchException>(() => Codec.Decode(codec, encoded));
    }

    // ═══════════════════════════════════════════════════
    //  WithCompressionCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "WithCompressionCodec round-trip empty body")]
    public void WithCompression_EmptyBody()
    {
        var codec = Codec.WithCompression(
            Codec.BytesOwned(0));

        byte[] value = [];
        byte[] encoded = Codec.EncodeToArray(codec, value);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "WithCompressionCodec round-trip small body")]
    public void WithCompression_SmallBody()
    {
        var codec = Codec.WithCompression(
            Codec.BytesOwned(4));

        byte[] value = [0x01, 0x02, 0x03, 0x04];
        byte[] encoded = Codec.EncodeToArray(codec, value);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "WithCompressionCodec round-trip large body")]
    public void WithCompression_LargeBody()
    {
        var codec = Codec.WithCompression(
            Codec.BytesOwned(4096));

        byte[] value = new byte[4096];
        Random.Shared.NextBytes(value);

        byte[] encoded = Codec.EncodeToArray(codec, value);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "WithCompressionCodec decompressed size > MaxDecompressedBytes throws")]
    public void WithCompression_DecompressionLimitExceeded_Throws()
    {
        var opts = new CodecOptions { MaxDecompressedBytes = 10 };
        var codec = Codec.WithCompression(
            Codec.BytesOwned(1000));

        byte[] value = new byte[1000]; // will compress to something, but decompressed > 10
        Random.Shared.NextBytes(value);
        byte[] encoded = Codec.EncodeToArray(codec, value);

        var ex = Assert.Throws<LimitExceededException>(() =>
            Codec.Decode(codec, encoded, options: opts));

        Assert.Equal(CodecErrorCode.DecompressionLimitExceeded, ex.ErrorCode);
    }

    // ═══════════════════════════════════════════════════
    //  ChoiceCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ChoiceCodec correct discriminator round-trip")]
    public void Choice_CorrectDiscriminator_RoundTrip()
    {
        var codec = Codec.Choice<int, int>(
            Codec.Int32LE,
            Codec.Case<int, int>(1, "case1", Codec.Int32LE));

        byte[] encoded = Codec.EncodeToArray(codec, 42);
        int decoded = Codec.Decode(codec, encoded);
        Assert.Equal(42, decoded);
    }

    [Fact(DisplayName = "ChoiceCodec unknown discriminator throws UnknownDiscriminatorException")]
    public void Choice_UnknownDiscriminator_Throws()
    {
        var codec = Codec.Choice<int, int>(
            Codec.Int32LE,
            Codec.Case<int, int>(1, "case1", Codec.Int32LE));

        byte[] data = new byte[8];
        BitConverter.TryWriteBytes(data.AsSpan(), 99);
        BitConverter.TryWriteBytes(data.AsSpan(4), 123);

        var ex = Assert.Throws<UnknownDiscriminatorException>(() => Codec.Decode(codec, data));
        _ = ex.DiscriminatorValue; // DiscriminatorValue is populated
    }

    // ═══════════════════════════════════════════════════
    //  OptionalCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "OptionalCodec value present round-trip")]
    public void Optional_Present_RoundTrip()
    {
        var codec = Codec.Optional(Codec.Utf8String(4), Codec.Bool);

        byte[] encoded = Codec.EncodeToArray(codec, "test");
        string? decoded = Codec.Decode(codec, encoded);
        Assert.Equal("test", decoded);
    }

    [Fact(DisplayName = "OptionalCodec value absent returns null")]
    public void Optional_Absent_ReturnsNull()
    {
        var codec = Codec.Optional(Codec.Utf8String(4), Codec.Bool);

        byte[] encoded = Codec.EncodeToArray(codec, (string?)null);
        string? decoded = Codec.Decode(codec, encoded);
        Assert.Null(decoded);
    }

    // ═══════════════════════════════════════════════════
    //  RepeatCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "RepeatCodec 0 elements")]
    public void Repeat_ZeroElements()
    {
        var codec = Codec.UInt8.Repeat(0);
        byte[] encoded = Codec.EncodeToArray(codec, Array.Empty<byte>());
        var decoded = Codec.Decode(codec, encoded);
        Assert.Empty(decoded);
    }

    [Fact(DisplayName = "RepeatCodec 1 element")]
    public void Repeat_OneElement()
    {
        var codec = Codec.UInt8.Repeat(1);
        byte[] encoded = Codec.EncodeToArray(codec, new byte[] { 42 });
        var decoded = Codec.Decode(codec, encoded);
        Assert.Equal(new byte[] { 42 }, decoded);
    }

    [Fact(DisplayName = "RepeatPrefixedCodec N elements from count-prefix")]
    public void RepeatPrefixed_N_Elements()
    {
        var codec = Codec.Int32LE.RepeatPrefixed(Codec.VarInt32);
        int[] value = [10, 20, 30];

        byte[] encoded = Codec.EncodeToArray(codec, value);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "RepeatPrefixedCodec max elements exceeded throws SequenceTooLarge")]
    public void RepeatPrefixed_MaxElementsExceeded_Throws()
    {
        var opts = new CodecOptions { MaxSequenceElements = 2 };
        var codec = Codec.Int32LE.RepeatPrefixed(Codec.VarInt32);
        int[] value = [1, 2, 3, 4, 5];

        byte[] encoded = Codec.EncodeToArray(codec, value);

        var ex = Assert.Throws<LimitExceededException>(() =>
            Codec.Decode(codec, encoded, options: opts));

        Assert.Equal(CodecErrorCode.SequenceTooLarge, ex.ErrorCode);
    }

    // ═══════════════════════════════════════════════════
    //  ValidateCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ValidateCodec predicate passes → value returned")]
    public void Validate_Passes_ReturnsValue()
    {
        var codec = Codec.Int32LE.Validate(x => x >= 0, "must be non-negative");
        byte[] encoded = Codec.EncodeToArray(codec, 42);
        int decoded = Codec.Decode(codec, encoded);
        Assert.Equal(42, decoded);
    }

    [Fact(DisplayName = "ValidateCodec predicate fails throws CodecValidationException")]
    public void Validate_Fails_Throws()
    {
        var codec = Codec.Int32LE.Validate(x => x >= 0, "must be non-negative");

        var ex = Assert.Throws<CodecValidationException>(() => Codec.EncodeToArray(codec, -1));
        Assert.Equal("must be non-negative", ex.ValidationMessage);
    }

    // ═══════════════════════════════════════════════════
    //  MapCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "MapCodec decode and encode transform (round-trip)")]
    public void Map_RoundTrip()
    {
        var codec = Codec.Int32LE.Map(
            decode: x => x * 2,
            encode: x => x / 2);

        byte[] encoded = Codec.EncodeToArray(codec, 42);
        int decoded = Codec.Decode(codec, encoded);
        Assert.Equal(42, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  ThenCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ThenCodec chaining two codecs (both succeed)")]
    public void Then_BothSucceed()
    {
        var codec = Codec.Int32LE.Then(Codec.Int32LE);
        // First codec consumes 4 bytes, result discarded; second codec provides the value
        byte[] data = [0x01, 0x00, 0x00, 0x00, 0x02, 0x00, 0x00, 0x00];

        int decoded = Codec.Decode(codec, data);
        Assert.Equal(2, decoded);
    }

    [Fact(DisplayName = "ThenCodec first codec fails propagates error")]
    public void Then_FirstFails_Propagates()
    {
        var codec = Codec.UInt8.Then(Codec.Int32LE);
        byte[] data = []; // truncated

        Assert.Throws<InsufficientDataException>(() => Codec.Decode(codec, data));
    }

    // ═══════════════════════════════════════════════════
    //  RecordBuilder
    // ═══════════════════════════════════════════════════

    private record SimpleRecord(int Count, string Name);

    [Fact(DisplayName = "RecordBuilder 2-field record round-trip")]
    public void RecordBuilder_TwoField_RoundTrip()
    {
        var codec = Codec.Record<SimpleRecord>()
            .Field("count", r => r.Count, Codec.VarInt32)
            .Field("name", r => r.Name, Codec.Utf8String(5))
            .Build((int c, string n) => new SimpleRecord(c, n));

        var record = new SimpleRecord(42, "hello");

        byte[] encoded = Codec.EncodeToArray(codec, record);
        SimpleRecord decoded = Codec.Decode(codec, encoded);

        Assert.Equal(record, decoded);
    }

    [Fact(DisplayName = "RecordBuilder duplicate field name throws")]
    public void RecordBuilder_DuplicateFieldName_Throws()
    {
        var ex = Assert.Throws<CodecValidationException>(() =>
        {
            Codec.Record<SimpleRecord>()
                .Field("dup", r => r.Count, Codec.VarInt32)
                .Field("dup", r => r.Name, Codec.Utf8String(5));
        });

        Assert.Equal(CodecErrorCode.DuplicateFieldName, ex.ErrorCode);
    }

    [Fact(DisplayName = "RecordBuilder field count mismatch throws")]
    public void RecordBuilder_FieldCountMismatch_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            Codec.Record<SimpleRecord>()
                .Field("count", r => r.Count, Codec.VarInt32)
                .Field("name", r => r.Name, Codec.Utf8String(5))
                .Build((int c) => new SimpleRecord(c, "")); // wrong arity
        });
    }

    // ═══════════════════════════════════════════════════
    //  Nesting
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Nested LengthPrefixed → WithChecksum → FixedFrame round-trip")]
    public void Nested_LP_Checksum_FixedFrame_RoundTrip()
    {
        var inner = Codec.FixedFrame(8, Codec.BytesOwnedRemaining(), FramePadding.Exact, TrailingDataPolicy.Allow);
        var withChecksum = Codec.WithChecksum(inner, ChecksumAlgorithms.Crc32, ChecksumPlacement.Trailer);
        var outer = Codec.LengthPrefixed(Codec.VarUInt32, withChecksum, TrailingDataPolicy.Allow);

        byte[] value = new byte[8];
        Random.Shared.NextBytes(value);

        byte[] encoded = Codec.EncodeToArray(outer, value);
        byte[] decoded = Codec.Decode(outer, encoded);

        Assert.Equal(value, decoded);
    }
}
