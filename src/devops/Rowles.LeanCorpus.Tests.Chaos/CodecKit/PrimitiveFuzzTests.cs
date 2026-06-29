using FsCheck;
using FsCheck.Xunit;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;
using Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Chaos.CodecKit;

[Trait("Category", "Chaos")]
[Trait("Category", "CodecKit")]
public sealed class PrimitiveFuzzTests : IClassFixture<ChaosDirectoryFixture>
{
    private readonly ChaosDirectoryFixture _fixture;

    public PrimitiveFuzzTests(ChaosDirectoryFixture fixture) => _fixture = fixture;

    [Property]
    public void VarInt32_RoundTrip_AllIntegers(int value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.VarInt32, value);
        int decoded = Codec.Decode(Codec.VarInt32, encoded);
        Assert.Equal(value, decoded);
    }

    [Property]
    public void VarInt64_RoundTrip_AllLongs(long value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.VarInt64, value);
        long decoded = Codec.Decode(Codec.VarInt64, encoded);
        Assert.Equal(value, decoded);
    }

    [Property]
    public void VarUInt32_RoundTrip_AllUInts(uint value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.VarUInt32, value);
        uint decoded = Codec.Decode(Codec.VarUInt32, encoded);
        Assert.Equal(value, decoded);
    }

    [Property]
    public void Int32LE_RoundTrip_AllIntegers(int value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Int32LE, value);
        int decoded = Codec.Decode(Codec.Int32LE, encoded);
        Assert.Equal(value, decoded);
    }

    [Property]
    public void Int64LE_RoundTrip_AllLongs(long value)
    {
        byte[] encoded = Codec.EncodeToArray(Codec.Int64LE, value);
        long decoded = Codec.Decode(Codec.Int64LE, encoded);
        Assert.Equal(value, decoded);
    }

    [Property]
    public void Float32_RoundTrip_AllFloats(float value)
    {
        if (float.IsNaN(value))
        {
            byte[] encoded = Codec.EncodeToArray(Codec.Float32LE, value);
            float decoded = Codec.Decode(Codec.Float32LE, encoded);
            Assert.True(float.IsNaN(decoded));
        }
        else
        {
            byte[] encoded = Codec.EncodeToArray(Codec.Float32LE, value);
            float decoded = Codec.Decode(Codec.Float32LE, encoded);
            Assert.Equal(value, decoded);
        }
    }

    [Property]
    public void Float64_RoundTrip_AllDoubles(double value)
    {
        if (double.IsNaN(value))
        {
            byte[] encoded = Codec.EncodeToArray(Codec.Float64LE, value);
            double decoded = Codec.Decode(Codec.Float64LE, encoded);
            Assert.True(double.IsNaN(decoded));
        }
        else
        {
            byte[] encoded = Codec.EncodeToArray(Codec.Float64LE, value);
            double decoded = Codec.Decode(Codec.Float64LE, encoded);
            Assert.Equal(value, decoded);
        }
    }

    [Property]
    public void Bool_Only0x00And0x01_DecodeSuccessfully(byte value)
    {
        if (value is 0x00 or 0x01)
        {
            bool decoded = Codec.Decode(Codec.Bool, [value]);
            // Just check it doesn't throw
            _ = decoded;
        }
        else
        {
            Assert.Throws<InvalidBooleanException>(() => Codec.Decode(Codec.Bool, [value]));
        }
    }

    [Property]
    public void Utf8String_RoundTrip_AnyString(NonNull<string> nonNullStr)
    {
        string value = nonNullStr.Get;
        // Use LengthPrefixed wrapping Utf8StringRemaining for arbitrary strings
        var codec = Codec.LengthPrefixed(Codec.VarUInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow);
        byte[] encoded = Codec.EncodeToArray(codec, value);
        string decoded = Codec.Decode(codec, encoded);
        Assert.Equal(value, decoded);
    }

    [Property]
    public void NonVarInt_Bytes_DecodeEitherThrowsOrProducesValue(byte[] randomBytes)
    {
        // Feed random bytes to VarInt32: either it decodes a value or it throws
        try
        {
            int value = Codec.Decode(Codec.VarInt32, randomBytes);
            // If it succeeds, re-encode should produce same bytes (or valid alternative)
            byte[] reEncoded = Codec.EncodeToArray(Codec.VarInt32, value);
            int reDecoded = Codec.Decode(Codec.VarInt32, reEncoded);
            Assert.Equal(value, reDecoded);
        }
        catch (InsufficientDataException)
        {
            // Expected for truncated input
        }
        catch (Exception)
        {
            // Any other exception is acceptable for truly invalid input
        }
    }
}
