using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit;

[Trait("Category", "CodecKit")]
public sealed class PrimitiveCodecTests
{
    // ═══════════════════════════════════════════════════
    //  UInt8
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(255)]
    public void UInt8_RoundTrip(byte value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.UInt8, value);
        byte decoded = Codec.Decode(Codec.UInt8, encoded);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "UInt8 truncated input throws InsufficientDataException")]
    public void UInt8_Truncated_Throws()
    {
        Assert.Throws<InsufficientDataException>(() => Codec.Decode(Codec.UInt8, []));
    }

    // ═══════════════════════════════════════════════════
    //  Bool
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Bool true → 0x01, false → 0x00")]
    public void Bool_TrueIs1_FalseIs0()
    {
        Assert.Equal([0x01], Codec.EncodeToArray(Codec.Bool, true));
        Assert.Equal([0x00], Codec.EncodeToArray(Codec.Bool, false));
    }

    [Fact(DisplayName = "Bool decode 0x01 → true, 0x00 → false")]
    public void Bool_Decode_Valid()
    {
        Assert.True(Codec.Decode(Codec.Bool, [0x01]));
        Assert.False(Codec.Decode(Codec.Bool, [0x00]));
    }

    [Fact(DisplayName = "Bool decode 0x02 throws InvalidBooleanException")]
    public void Bool_Decode_0x02_Throws()
    {
        var ex = Assert.Throws<InvalidBooleanException>(() => Codec.Decode(Codec.Bool, [0x02]));
        Assert.Equal(0x02, ex.ActualValue);
    }

    [Fact(DisplayName = "Bool decode 0xFF throws InvalidBooleanException")]
    public void Bool_Decode_0xFF_Throws()
    {
        var ex = Assert.Throws<InvalidBooleanException>(() => Codec.Decode(Codec.Bool, [0xFF]));
        Assert.Equal(0xFF, ex.ActualValue);
    }

    // ═══════════════════════════════════════════════════
    //  Int8
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData(sbyte.MinValue)]
    [InlineData(sbyte.MaxValue)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Int8_RoundTrip(sbyte value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Int8, value);
        byte[] data = Codec.EncodeToArray(Codec.Int8, value);
        sbyte decoded = Codec.Decode(Codec.Int8, data);
        Assert.Equal(value, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  Int16 LE / BE
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)1)]
    [InlineData((short)-1)]
    [InlineData(short.MinValue)]
    [InlineData(short.MaxValue)]
    public void Int16LE_RoundTrip(short value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Int16LE, value);
        short decoded = Codec.Decode(Codec.Int16LE, encoded);
        Assert.Equal(value, decoded);
        Assert.Equal(2, encoded.Length);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)1)]
    [InlineData((short)-1)]
    [InlineData(short.MinValue)]
    [InlineData(short.MaxValue)]
    public void Int16BE_RoundTrip(short value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Int16BE, value);
        short decoded = Codec.Decode(Codec.Int16BE, encoded);
        Assert.Equal(value, decoded);
        Assert.Equal(2, encoded.Length);
    }

    [Fact(DisplayName = "Int16LE truncated input throws")]
    public void Int16LE_Truncated_Throws()
    {
        Assert.Throws<InsufficientDataException>(() => Codec.Decode(Codec.Int16LE, [0x01]));
    }

    // ═══════════════════════════════════════════════════
    //  Int32 LE / BE
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Int32LE_RoundTrip(int value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Int32LE, value);
        int decoded = Codec.Decode(Codec.Int32LE, encoded);
        Assert.Equal(value, decoded);
        Assert.Equal(4, encoded.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Int32BE_RoundTrip(int value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Int32BE, value);
        int decoded = Codec.Decode(Codec.Int32BE, encoded);
        Assert.Equal(value, decoded);
        Assert.Equal(4, encoded.Length);
    }

    // ═══════════════════════════════════════════════════
    //  Int64 LE / BE
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Int64LE_RoundTrip(long value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Int64LE, value);
        long decoded = Codec.Decode(Codec.Int64LE, encoded);
        Assert.Equal(value, decoded);
        Assert.Equal(8, encoded.Length);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(-1L)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Int64BE_RoundTrip(long value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Int64BE, value);
        long decoded = Codec.Decode(Codec.Int64BE, encoded);
        Assert.Equal(value, decoded);
        Assert.Equal(8, encoded.Length);
    }

    // ═══════════════════════════════════════════════════
    //  UInt16 LE / BE
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData((ushort)0)]
    [InlineData(ushort.MaxValue)]
    public void UInt16LE_RoundTrip(ushort value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.UInt16LE, value);
        ushort decoded = Codec.Decode(Codec.UInt16LE, encoded);
        Assert.Equal(value, decoded);
    }

    [Theory]
    [InlineData((ushort)0)]
    [InlineData(ushort.MaxValue)]
    public void UInt16BE_RoundTrip(ushort value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.UInt16BE, value);
        ushort decoded = Codec.Decode(Codec.UInt16BE, encoded);
        Assert.Equal(value, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  UInt32 LE / BE
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    public void UInt32LE_RoundTrip(uint value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.UInt32LE, value);
        uint decoded = Codec.Decode(Codec.UInt32LE, encoded);
        Assert.Equal(value, decoded);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void UInt32BE_RoundTrip(uint value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.UInt32BE, value);
        uint decoded = Codec.Decode(Codec.UInt32BE, encoded);
        Assert.Equal(value, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  UInt64 LE / BE
    // ═══════════════════════════════════════════════════
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(ulong.MaxValue)]
    public void UInt64LE_RoundTrip(ulong value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.UInt64LE, value);
        ulong decoded = Codec.Decode(Codec.UInt64LE, encoded);
        Assert.Equal(value, decoded);
    }
    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(ulong.MaxValue)]
    public void UInt64BE_RoundTrip(ulong value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.UInt64BE, value);
        ulong decoded = Codec.Decode(Codec.UInt64BE, encoded);
        Assert.Equal(value, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  Float32 LE / BE
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(-1f)]
    [InlineData(float.MinValue)]
    [InlineData(float.MaxValue)]
    public void Float32LE_RoundTrip(float value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Float32LE, value);
        float decoded = Codec.Decode(Codec.Float32LE, encoded);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "Float32LE NaN round-trip")]
    public void Float32LE_NaN_RoundTrip()
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Float32LE, float.NaN);
        float decoded = Codec.Decode(Codec.Float32LE, encoded);
        Assert.True(float.IsNaN(decoded));
    }

    [Fact(DisplayName = "Float32LE ±Infinity round-trip")]
    public void Float32LE_Infinity_RoundTrip()
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Float32LE, float.PositiveInfinity);
        float decoded = Codec.Decode(Codec.Float32LE, encoded);
        Assert.True(float.IsPositiveInfinity(decoded));

        encoded = Codec.EncodeToArray(Codec.Float32LE, float.NegativeInfinity);
        decoded = Codec.Decode(Codec.Float32LE, encoded);
        Assert.True(float.IsNegativeInfinity(decoded));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    [InlineData(-1f)]
    public void Float32BE_RoundTrip(float value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Float32BE, value);
        float decoded = Codec.Decode(Codec.Float32BE, encoded);
        Assert.Equal(value, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  Float64 LE / BE
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData(0d)]
    [InlineData(1d)]
    [InlineData(-1d)]
    [InlineData(double.MinValue)]
    [InlineData(double.MaxValue)]
    public void Float64LE_RoundTrip(double value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Float64LE, value);
        double decoded = Codec.Decode(Codec.Float64LE, encoded);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "Float64LE NaN round-trip")]
    public void Float64LE_NaN_RoundTrip()
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Float64LE, double.NaN);
        double decoded = Codec.Decode(Codec.Float64LE, encoded);
        Assert.True(double.IsNaN(decoded));
    }

    [Fact(DisplayName = "Float64LE ±Infinity round-trip")]
    public void Float64LE_Infinity_RoundTrip()
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Float64LE, double.PositiveInfinity);
        double decoded = Codec.Decode(Codec.Float64LE, encoded);
        Assert.True(double.IsPositiveInfinity(decoded));

        encoded = Codec.EncodeToArray(Codec.Float64LE, double.NegativeInfinity);
        decoded = Codec.Decode(Codec.Float64LE, encoded);
        Assert.True(double.IsNegativeInfinity(decoded));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1d)]
    [InlineData(-1d)]
    public void Float64BE_RoundTrip(double value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Float64BE, value);
        double decoded = Codec.Decode(Codec.Float64BE, encoded);
        Assert.Equal(value, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  VarInt32
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(16383)]
    [InlineData(16384)]
    [InlineData(2097151)]
    [InlineData(2097152)]
    [InlineData(268435455)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void VarInt32_RoundTrip(int value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.VarInt32, value);
        int decoded = Codec.Decode(Codec.VarInt32, encoded);
        Assert.Equal(value, decoded);
        Assert.InRange(encoded.Length, 1, 5);
    }

    // ═══════════════════════════════════════════════════
    //  VarInt64
    // ═══════════════════════════════════════════════════

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(127L)]
    [InlineData(128L)]
    [InlineData(16383L)]
    [InlineData(16384L)]
    [InlineData(2097151L)]
    [InlineData(2097152L)]
    [InlineData(268435455L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void VarInt64_RoundTrip(long value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.VarInt64, value);
        long decoded = Codec.Decode(Codec.VarInt64, encoded);
        Assert.Equal(value, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  VarUInt32 / VarUInt64
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "VarUInt32 round-trip 0 and max")]
    public void VarUInt32_RoundTrip()
    {
        byte[] encoded = Codec.EncodeToArray(Codec.VarUInt32, 0u);
        uint decoded = Codec.Decode(Codec.VarUInt32, encoded);
        Assert.Equal(0u, decoded);

        encoded = Codec.EncodeToArray(Codec.VarUInt32, uint.MaxValue);
        decoded = Codec.Decode(Codec.VarUInt32, encoded);
        Assert.Equal(uint.MaxValue, decoded);
    }

    [Fact(DisplayName = "VarUInt64 round-trip 0 and max")]
    public void VarUInt64_RoundTrip()
    {
        byte[] encoded = Codec.EncodeToArray(Codec.VarUInt64, 0UL);
        ulong decoded = Codec.Decode(Codec.VarUInt64, encoded);
        Assert.Equal(0UL, decoded);

        encoded = Codec.EncodeToArray(Codec.VarUInt64, ulong.MaxValue);
        decoded = Codec.Decode(Codec.VarUInt64, encoded);
        Assert.Equal(ulong.MaxValue, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  GuidDotNet / GuidRfc4122
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "GuidDotNetCodec round-trip")]
    public void GuidDotNet_RoundTrip()
    {
        var guid = Guid.NewGuid();
        byte[] encoded = Codec.EncodeToArray(Codec.GuidDotNet, guid);
        Guid decoded = Codec.Decode(Codec.GuidDotNet, encoded);
        Assert.Equal(guid, decoded);
        Assert.Equal(16, encoded.Length);
    }

    [Fact(DisplayName = "GuidRfc4122Codec round-trip")]
    public void GuidRfc4122_RoundTrip()
    {
        var guid = Guid.NewGuid();
        byte[] encoded = Codec.EncodeToArray(Codec.GuidRfc4122, guid);
        Guid decoded = Codec.Decode(Codec.GuidRfc4122, encoded);
        Assert.Equal(guid, decoded);
        Assert.Equal(16, encoded.Length);
    }

    [Fact(DisplayName = "GuidDotNet ≠ GuidRfc4122 encoding for non-trivial GUIDs")]
    public void GuidDotNet_DiffersFrom_Rfc4122()
    {
        var guid = new Guid("01020304-0506-0708-090A-0B0C0D0E0F10");
        byte[] dotNet = Codec.EncodeToArray(Codec.GuidDotNet, guid);
        byte[] rfc = Codec.EncodeToArray(Codec.GuidRfc4122, guid);
        // The two encodings should differ in byte order
        Assert.NotEqual(dotNet, rfc);
    }

    // ═══════════════════════════════════════════════════
    //  Utf8String
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Utf8String empty round-trip")]
    public void Utf8String_Empty()
    {
        var codec = Codec.Utf8String(0);
        byte[] encoded = Codec.EncodeToArray(codec, "");
        string decoded = Codec.Decode(codec, encoded);
        Assert.Equal("", decoded);
    }

    [Fact(DisplayName = "Utf8String ASCII round-trip")]
    public void Utf8String_Ascii()
    {
        var codec = Codec.Utf8String(5);
        byte[] encoded = Codec.EncodeToArray(codec, "hello");
        string decoded = Codec.Decode(codec, encoded);
        Assert.Equal("hello", decoded);
    }

    [Fact(DisplayName = "Utf8String multi-byte UTF-8 round-trip")]
    public void Utf8String_MultiByte()
    {
        var codec = Codec.Utf8String(13); // "héllo wörld" is 13 bytes in UTF-8
        byte[] encoded = Codec.EncodeToArray(codec, "héllo wörld");
        string decoded = Codec.Decode(codec, encoded);
        Assert.Equal("héllo wörld", decoded);
    }

    // ═══════════════════════════════════════════════════
    //  BytesOwned / BytesBorrowed / BytesOwnedRemaining
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "BytesOwned round-trip")]
    public void BytesOwned_RoundTrip()
    {
        var codec = Codec.BytesOwned(4);
        byte[] value = [0x01, 0x02, 0x03, 0x04];
        byte[] encoded = Codec.EncodeToArray(codec, value);
        byte[] decoded = Codec.Decode(codec, encoded);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "BytesBorrowed round-trip")]
    public void BytesBorrowed_RoundTrip()
    {
        var codec = Codec.BytesBorrowed(3);
        byte[] value = [0xAA, 0xBB, 0xCC];
        byte[] encoded = Codec.EncodeToArray<ReadOnlySequence<byte>>(codec, new ReadOnlySequence<byte>(value));
        ReadOnlySequence<byte> decoded = Codec.Decode(codec, encoded);
        Assert.Equal(value, decoded.ToArray());
    }

    [Fact(DisplayName = "BytesOwnedRemaining consumes all remaining data")]
    public void BytesOwnedRemaining_ConsumesAll()
    {
        byte[] payload = [0x01, 0x02, 0x03, 0x04, 0x05];
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwnedRemaining(), TrailingDataPolicy.Allow);
        byte[] decoded = Codec.Decode(codec, Codec.EncodeToArray(codec, payload));
        Assert.Equal(payload, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  MagicCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "MagicCodec matching magic succeeds")]
    public void Magic_Matching_Succeeds()
    {
        var codec = Codec.Magic([0xDE, 0xAD]);
        byte[] data = [0xDE, 0xAD];
        Codec.Decode(codec, data); // should not throw
    }

    [Fact(DisplayName = "MagicCodec mismatched magic throws MagicMismatchException")]
    public void Magic_Mismatched_Throws()
    {
        var codec = Codec.Magic([0xDE, 0xAD]);
        byte[] data = [0xBE, 0xEF];
        var ex = Assert.Throws<MagicMismatchException>(() => Codec.Decode(codec, data));
        Assert.Equal(new byte[] { 0xDE, 0xAD }, ex.Expected);
        Assert.Equal(new byte[] { 0xBE, 0xEF }, ex.Actual);
    }

    // ═══════════════════════════════════════════════════
    //  PaddingCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "PaddingCodec writes exact byte value")]
    public void Padding_WritesExactValue()
    {
        var codec = Codec.Padding(2, 0xAB);
        byte[] encoded = Codec.EncodeToArray(codec, Rowles.LeanCorpus.Codecs.CodecKit.Codecs.Unit.Value);
        Assert.Equal(new byte[] { 0xAB, 0xAB }, encoded);
    }

    // ═══════════════════════════════════════════════════
    //  SkipCodec
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "SkipCodec advances reader by exact byte count")]
    public void Skip_AdvancesReader()
    {
        var codec = Codec.Skip(3);
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05];
        Codec.Decode(codec, data); // consumes first 3 bytes
    }

    // ═══════════════════════════════════════════════════
    //  Utf8BytesBorrowed
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Utf8BytesBorrowed round-trip via Utf8BytesOwned")]
    public void Utf8BytesBorrowed_RoundTrip()
    {
        var owned = Codec.Utf8BytesOwned(5);
        var borrowed = Codec.Utf8BytesBorrowed(5);

        byte[] encoded = Codec.EncodeToArray(owned, new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F });
        ReadOnlySequence<byte> decoded = Codec.Decode(borrowed, encoded);
        Assert.Equal("Hello", System.Text.Encoding.UTF8.GetString(decoded.ToArray()));
    }

    // ═══════════════════════════════════════════════════
    //  Utf8BytesOwned
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Utf8BytesOwned round-trip")]
    public void Utf8BytesOwned_RoundTrip()
    {
        var codec = Codec.Utf8BytesOwned(5);
        byte[] input = new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F };

        byte[] encoded = Codec.EncodeToArray(codec, input);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(input, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  Utf8StringRemaining
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "Utf8StringRemaining round-trip")]
    public void Utf8StringRemaining_RoundTrip()
    {
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow);
        string input = "Hello, UTF-8!";

        byte[] encoded = Codec.EncodeToArray(codec, input);
        string decoded = Codec.Decode(codec, encoded);

        Assert.Equal(input, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  BytesBorrowedRemaining
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "BytesBorrowedRemaining round-trip")]
    public void BytesBorrowedRemaining_RoundTrip()
    {
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesBorrowedRemaining(), TrailingDataPolicy.Allow);
        byte[] input = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };

        byte[] encoded = Codec.EncodeToArray(codec, new ReadOnlySequence<byte>(input));
        ReadOnlySequence<byte> decoded = Codec.Decode(codec, encoded);

        Assert.Equal(input, decoded.ToArray());
    }

    // ═══════════════════════════════════════════════════
    //  BytesOwnedRemaining (standalone)
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "BytesOwnedRemaining wrapped in LengthPrefixed round-trip")]
    public void BytesOwnedRemaining_LengthPrefixed_RoundTrip()
    {
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.BytesOwnedRemaining(), TrailingDataPolicy.Allow);
        byte[] input = new byte[] { 0xAA, 0xBB, 0xCC };

        byte[] encoded = Codec.EncodeToArray(codec, input);
        byte[] decoded = Codec.Decode(codec, encoded);

        Assert.Equal(input, decoded);
    }
}
