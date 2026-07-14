using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Codecs;

[Trait("Category", "CodecKit")]
public sealed class CodecExtensionsTests
{
    // ═══════════════════════════════════════════════════
    //  Decode from byte[]
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecExtensions: Decode from byte[] extension works (round-trip with Int32LE)")]
    public void Decode_ByteArray_RoundTripInt32()
    {
        int value = 12345;
        byte[] encoded = Codec.Int32LE.EncodeToArray(value);
        int decoded = Codec.Int32LE.Decode(encoded);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "CodecExtensions: Decode from byte[] with Bool codec")]
    public void Decode_ByteArray_Bool()
    {
        Assert.True(Codec.Bool.Decode([0x01]));
        Assert.False(Codec.Bool.Decode([0x00]));
    }

    [Fact(DisplayName = "CodecExtensions: Decode from byte[] with Float64LE codec")]
    public void Decode_ByteArray_Float64()
    {
        double value = 3.14159265358979;
        byte[] encoded = Codec.Float64LE.EncodeToArray(value);
        double decoded = Codec.Float64LE.Decode(encoded);
        Assert.Equal(value, decoded, 10);
    }

    // ═══════════════════════════════════════════════════
    //  Decode from ReadOnlySequence
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecExtensions: Decode from ReadOnlySequence extension works")]
    public void Decode_ReadOnlySequence_RoundTripInt32()
    {
        int value = -9876;
        byte[] encoded = Codec.Int32LE.EncodeToArray(value);
        var seq = new ReadOnlySequence<byte>(encoded);
        int decoded = Codec.Int32LE.Decode(seq);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "CodecExtensions: Decode from single-segment ReadOnlySequence works")]
    public void Decode_ReadOnlySequence_SingleSegment()
    {
        var memory = new ReadOnlyMemory<byte>([0x78, 0x56, 0x34, 0x12]);
        var seq = new ReadOnlySequence<byte>(memory); // 0x12345678 LE
        int decoded = Codec.Int32LE.Decode(seq);
        Assert.Equal(0x12345678, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  EncodeToArray
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecExtensions: EncodeToArray extension works")]
    public void EncodeToArray_RoundTrip()
    {
        int value = 42;
        byte[] encoded = Codec.Int32LE.EncodeToArray(value);
        Assert.Equal(4, encoded.Length);
        Assert.Equal(42, BitConverter.ToInt32(encoded));
    }

    [Fact(DisplayName = "CodecExtensions: EncodeToArray with Bool codec")]
    public void EncodeToArray_Bool()
    {
        byte[] encodedTrue = Codec.Bool.EncodeToArray(true);
        Assert.Equal([0x01], encodedTrue);

        byte[] encodedFalse = Codec.Bool.EncodeToArray(false);
        Assert.Equal([0x00], encodedFalse);
    }

    [Fact(DisplayName = "CodecExtensions: EncodeToArray with Float64LE codec")]
    public void EncodeToArray_Float64()
    {
        double value = 2.718281828459045;
        byte[] encoded = Codec.Float64LE.EncodeToArray(value);
        double decoded = Codec.Float64LE.Decode(encoded);
        Assert.Equal(value, decoded, 10);
    }

    // ═══════════════════════════════════════════════════
    //  Equivalence with static Codec methods
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecExtensions: Decode from byte[] is equivalent to Codec.Decode static method")]
    public void Decode_ByteArray_EquivalentToStatic()
    {
        int value = 777;
        byte[] encoded = Codec.EncodeToArray(Codec.Int32LE, value);

        int viaExtension = Codec.Int32LE.Decode(encoded);
        int viaStatic = Codec.Decode(Codec.Int32LE, encoded);

        Assert.Equal(viaStatic, viaExtension);
    }

    [Fact(DisplayName = "CodecExtensions: Decode from ReadOnlySequence is equivalent to Codec.Decode static method")]
    public void Decode_ReadOnlySequence_EquivalentToStatic()
    {
        int value = 888;
        byte[] encoded = Codec.EncodeToArray(Codec.Int32LE, value);
        var seq = new ReadOnlySequence<byte>(encoded);

        int viaExtension = Codec.Int32LE.Decode(seq);
        int viaStatic = Codec.Decode(Codec.Int32LE, seq);

        Assert.Equal(viaStatic, viaExtension);
    }

    [Fact(DisplayName = "CodecExtensions: EncodeToArray is equivalent to Codec.EncodeToArray static method")]
    public void EncodeToArray_EquivalentToStatic()
    {
        int value = 999;
        byte[] viaExtension = Codec.Int32LE.EncodeToArray(value);
        byte[] viaStatic = Codec.EncodeToArray(Codec.Int32LE, value);

        Assert.Equal(viaStatic, viaExtension);
    }

    // ═══════════════════════════════════════════════════
    //  Explicit options and registry parameters
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecExtensions: Decode from byte[] with explicit options parameter works")]
    public void Decode_ByteArray_WithOptions()
    {
        var options = new CodecOptions { MaxNestingDepth = 5 };
        int value = 42;
        byte[] encoded = Codec.Int32LE.EncodeToArray(value);
        int decoded = Codec.Int32LE.Decode(encoded, options);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "CodecExtensions: Decode from byte[] with explicit registry parameter works")]
    public void Decode_ByteArray_WithRegistry()
    {
        var registry = CodecRegistry.Default;
        int value = 42;
        byte[] encoded = Codec.Int32LE.EncodeToArray(value);
        int decoded = Codec.Int32LE.Decode(encoded, registry: registry);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "CodecExtensions: Decode from byte[] with both options and registry works")]
    public void Decode_ByteArray_WithOptionsAndRegistry()
    {
        var options = new CodecOptions { MaxNestingDepth = 10 };
        var registry = CodecRegistry.Default;
        int value = 42;
        byte[] encoded = Codec.Int32LE.EncodeToArray(value);
        int decoded = Codec.Int32LE.Decode(encoded, options, registry);
        Assert.Equal(value, decoded);
    }

    [Fact(DisplayName = "CodecExtensions: EncodeToArray with explicit options works")]
    public void EncodeToArray_WithOptions()
    {
        var options = new CodecOptions { MaxNestingDepth = 3 };
        int value = 100;
        byte[] encoded = Codec.Int32LE.EncodeToArray(value, options);
        Assert.Equal(4, encoded.Length);
        Assert.Equal(100, BitConverter.ToInt32(encoded));
    }

    // ═══════════════════════════════════════════════════
    //  Error cases
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecExtensions: Decode empty array with Int32LE throws InsufficientDataException")]
    public void Decode_EmptyArray_ThrowsInsufficientData()
    {
        Assert.Throws<InsufficientDataException>(() => Codec.Int32LE.Decode([]));
    }

    [Fact(DisplayName = "CodecExtensions: Decode truncated array with Int32LE throws InsufficientDataException")]
    public void Decode_TruncatedArray_ThrowsInsufficientData()
    {
        Assert.Throws<InsufficientDataException>(() => Codec.Int32LE.Decode([0x01, 0x02]));
    }

    [Fact(DisplayName = "CodecExtensions: Decode from empty ReadOnlySequence with Int32LE throws InsufficientDataException")]
    public void Decode_EmptySequence_ThrowsInsufficientData()
    {
        var seq = new ReadOnlySequence<byte>([]);
        Assert.Throws<InsufficientDataException>(() => Codec.Int32LE.Decode(seq));
    }

    // ═══════════════════════════════════════════════════
    //  Multiple codec types
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "CodecExtensions: Works with Bool codec via all three extension methods")]
    public void AllMethods_Bool()
    {
        // EncodeToArray
        byte[] encoded = Codec.Bool.EncodeToArray(true);
        Assert.Equal([0x01], encoded);

        // Decode from byte[]
        Assert.True(Codec.Bool.Decode(encoded));

        // Decode from ReadOnlySequence
        Assert.True(Codec.Bool.Decode(new ReadOnlySequence<byte>(encoded)));
    }

    [Fact(DisplayName = "CodecExtensions: Works with Int32LE codec via all three extension methods")]
    public void AllMethods_Int32()
    {
        int value = -1234567;

        byte[] encoded = Codec.Int32LE.EncodeToArray(value);
        Assert.Equal(value, Codec.Int32LE.Decode(encoded));
        Assert.Equal(value, Codec.Int32LE.Decode(new ReadOnlySequence<byte>(encoded)));
    }

    [Fact(DisplayName = "CodecExtensions: Works with Float64LE codec via all three extension methods")]
    public void AllMethods_Float64()
    {
        double value = double.Epsilon;

        byte[] encoded = Codec.Float64LE.EncodeToArray(value);
        Assert.Equal(value, Codec.Float64LE.Decode(encoded));
        Assert.Equal(value, Codec.Float64LE.Decode(new ReadOnlySequence<byte>(encoded)));
    }
}
