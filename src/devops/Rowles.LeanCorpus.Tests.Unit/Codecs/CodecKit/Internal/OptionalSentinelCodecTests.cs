using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Codecs.CodecKit.Internal;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Internal;

[Trait("Category", "CodecKit")]
public sealed class OptionalSentinelCodecTests
{
    private static readonly ICodec<string> StringInnerCodec =
        Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow);

    // ═══════════════════════════════════════════════════
    //  Constructor — null guards
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "OptionalSentinelCodec: Constructor throws ArgumentNullException when innerCodec is null")]
    public void Constructor_NullInnerCodec_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new OptionalSentinelCodec<int, int>(null!, Codec.Int32LE, 0, 1));
        Assert.Contains("innerCodec", ex.Message);
    }

    [Fact(DisplayName = "OptionalSentinelCodec: Constructor throws ArgumentNullException when sentinelCodec is null")]
    public void Constructor_NullSentinelCodec_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new OptionalSentinelCodec<int, int>(Codec.Int32LE, null!, 0, 1));
        Assert.Contains("sentinelCodec", ex.Message);
    }

    [Fact(DisplayName = "OptionalSentinelCodec: Constructor throws ArgumentNullException when absentValue is null")]
    public void Constructor_NullAbsentValue_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new OptionalSentinelCodec<string, string>(
                StringInnerCodec, StringInnerCodec, null!, "present"));
        Assert.Contains("absentValue", ex.Message);
    }

    [Fact(DisplayName = "OptionalSentinelCodec: Constructor throws ArgumentNullException when presentValue is null")]
    public void Constructor_NullPresentValue_Throws()
    {
        var ex = Assert.Throws<ArgumentNullException>(() =>
            new OptionalSentinelCodec<string, string>(
                StringInnerCodec, StringInnerCodec, "absent", null!));
        Assert.Contains("presentValue", ex.Message);
    }

    [Fact(DisplayName = "OptionalSentinelCodec: Constructor throws ArgumentException when absentValue equals presentValue")]
    public void Constructor_EqualSentinels_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new OptionalSentinelCodec<int, int>(Codec.Int32LE, Codec.Int32LE, 5, 5));
        bool mentionsAbsent = ex.Message.Contains("absentValue", StringComparison.OrdinalIgnoreCase);
        bool mentionsPresent = ex.Message.Contains("presentValue", StringComparison.OrdinalIgnoreCase);
        Assert.True(mentionsAbsent || mentionsPresent,
            $"Expected absentValue or presentValue in message, got: {ex.Message}");
    }

    // ═══════════════════════════════════════════════════
    //  Round-trip: string-based T (reference type, works cleanly)
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "OptionalSentinelCodec: Round-trip encode null string returns null")]
    public void RoundTrip_NullString_ReturnsNull()
    {
        var codec = new OptionalSentinelCodec<string, int>(
            StringInnerCodec, Codec.Int32LE, 0, 1);

        byte[] encoded = Codec.EncodeToArray(codec, (string?)null);
        Assert.Null(Codec.Decode(codec, encoded));
    }

    [Fact(DisplayName = "OptionalSentinelCodec: Round-trip encode non-null string returns the string")]
    public void RoundTrip_NonNullString_ReturnsString()
    {
        var codec = new OptionalSentinelCodec<string, int>(
            StringInnerCodec, Codec.Int32LE, 0, 1);

        byte[] encoded = Codec.EncodeToArray(codec, "hello");
        Assert.Equal("hello", Codec.Decode(codec, encoded));
    }

    [Fact(DisplayName = "OptionalSentinelCodec: Round-trip multiple string values")]
    public void RoundTrip_MultipleStrings()
    {
        var codec = new OptionalSentinelCodec<string, int>(
            StringInnerCodec, Codec.Int32LE, 0, 1);

        // Interleave null and non-null
        Assert.Null(Codec.Decode(codec, Codec.EncodeToArray(codec, (string?)null)));
        Assert.Equal("a", Codec.Decode(codec, Codec.EncodeToArray(codec, "a")));
        Assert.Null(Codec.Decode(codec, Codec.EncodeToArray(codec, (string?)null)));
        Assert.Equal("longer string here", Codec.Decode(codec, Codec.EncodeToArray(codec, "longer string here")));
    }

    // ═══════════════════════════════════════════════════
    //  Sentinel value validation
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "OptionalSentinelCodec: Decode with wrong sentinel value throws CodecValidationException")]
    public void Decode_WrongSentinel_Throws()
    {
        var codec = new OptionalSentinelCodec<string, int>(
            StringInnerCodec, Codec.Int32LE, 0, 1);

        byte[] data = Codec.EncodeToArray(Codec.Int32LE, 2);
        var ex = Assert.Throws<CodecValidationException>(() => Codec.Decode(codec, data));
        Assert.Equal(CodecErrorCode.InvalidValue, ex.ErrorCode);
    }

    [Fact(DisplayName = "OptionalSentinelCodec: Decode with present sentinel but no body throws InsufficientDataException")]
    public void Decode_PresentSentinel_NoBody_Throws()
    {
        var codec = new OptionalSentinelCodec<string, int>(
            StringInnerCodec, Codec.Int32LE, 0, 1);

        byte[] data = Codec.EncodeToArray(Codec.Int32LE, 1);
        Assert.Throws<InsufficientDataException>(() => Codec.Decode(codec, data));
    }

    // ═══════════════════════════════════════════════════
    //  String inner with int sentinel — encode null produces
    //  just the sentinel bytes
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "OptionalSentinelCodec: String inner with int sentinel - encode null writes only absent sentinel")]
    public void StringInner_IntSentinel_Null_WritesOnlySentinel()
    {
        var codec = new OptionalSentinelCodec<string, int>(
            StringInnerCodec, Codec.Int32LE, 0, 1);

        byte[] encoded = Codec.EncodeToArray(codec, (string?)null);
        Assert.Equal(4, encoded.Length);
        Assert.Equal(0, BitConverter.ToInt32(encoded));
    }

    [Fact(DisplayName = "OptionalSentinelCodec: String inner with int sentinel - encode non-null")]
    public void StringInner_IntSentinel_Present()
    {
        var codec = new OptionalSentinelCodec<string, int>(
            StringInnerCodec, Codec.Int32LE, 0, 1);

        byte[] encoded = Codec.EncodeToArray(codec, "world");
        Assert.Equal("world", Codec.Decode(codec, encoded));
    }

    // ═══════════════════════════════════════════════════
    //  Byte sentinel with int inner — can't exercise through
    //  ICodec<int?> due to value-type T? constraints, but we
    //  verify construction and sentinel equality
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "OptionalSentinelCodec: Byte sentinel with int inner - constructor stores sentinels")]
    public void ByteSentinel_IntInner_ConstructorStoresSentinels()
    {
        // Construction does not throw
        var codec = new OptionalSentinelCodec<int, byte>(
            Codec.Int32LE, Codec.UInt8, (byte)0x00, (byte)0xFF);
        Assert.NotNull(codec);
    }

    [Fact(DisplayName = "OptionalSentinelCodec: Byte sentinel equal absent/present throws")]
    public void ByteSentinel_IntInner_EqualSentinels_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new OptionalSentinelCodec<int, byte>(
                Codec.Int32LE, Codec.UInt8, (byte)0xAA, (byte)0xAA));
    }

    // ═══════════════════════════════════════════════════
    //  String sentinel with string inner
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "OptionalSentinelCodec: String sentinel with string inner - round-trip")]
    public void StringSentinel_StringInner_RoundTrip()
    {
        var codec = new OptionalSentinelCodec<string, string>(
            StringInnerCodec, StringInnerCodec, "ABSENT", "PRESENT");

        byte[] encodedNull = Codec.EncodeToArray(codec, (string?)null);
        Assert.Null(Codec.Decode(codec, encodedNull));

        byte[] encodedPresent = Codec.EncodeToArray(codec, "actual data");
        Assert.Equal("actual data", Codec.Decode(codec, encodedPresent));
    }

    // ═══════════════════════════════════════════════════
    //  Via Codec.Optional factory method (T : class constraint)
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "OptionalSentinelCodec: Via Codec.Optional factory with string and int sentinel")]
    public void CodecFactory_StringInt_RoundTrip()
    {
        var codec = StringInnerCodec.Optional(Codec.Int32LE, 0, 1);

        byte[] encodedNull = Codec.EncodeToArray(codec, (string?)null);
        Assert.Null(Codec.Decode(codec, encodedNull));

        byte[] encodedValue = Codec.EncodeToArray(codec, "factory test");
        Assert.Equal("factory test", Codec.Decode(codec, encodedValue));
    }

    [Fact(DisplayName = "OptionalSentinelCodec: Via Codec.Optional with byte sentinel and string inner")]
    public void CodecFactory_ByteSentinel()
    {
        var codec = StringInnerCodec.Optional(Codec.UInt8, (byte)0x00, (byte)0xFF);

        byte[] encodedNull = Codec.EncodeToArray(codec, (string?)null);
        Assert.Null(Codec.Decode(codec, encodedNull));

        byte[] encodedValue = Codec.EncodeToArray(codec, "hello");
        Assert.Equal("hello", Codec.Decode(codec, encodedValue));
    }
}
