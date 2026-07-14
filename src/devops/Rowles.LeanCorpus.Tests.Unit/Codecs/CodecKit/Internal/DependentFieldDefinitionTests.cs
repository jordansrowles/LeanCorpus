using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Internal;

[Trait("Category", "CodecKit")]
public sealed class DependentFieldDefinitionTests
{
    private sealed record TypedPayload(string Type, object Payload);

    private sealed record MultiDependentRecord(string Type, object PayloadA, object PayloadB);

    private sealed record ChainedDependentRecord(string Type, string SubType, object Payload);

    private static readonly ICodec<string> StringCodec =
        Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow);

    [Fact(DisplayName = "DependentFieldDefinition: IsConstant returns false")]
    public void IsConstant_ReturnsFalse()
    {
        var def = new DependentFieldDefinition<TypedPayload, string, object>(
            "payload",
            r => r.Payload,
            type => type == "int"
                ? (ICodec<object>)Codec.Int32LE.Map(v => (object)v, v => (int)v)
                : StringCodec.Map(v => (object)v, v => (string)v),
            dependencyIndex: 0,
            depGetter: r => r.Type);

        Assert.False(def.IsConstant);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Name returns the field name")]
    public void Name_ReturnsFieldName()
    {
        var def = new DependentFieldDefinition<TypedPayload, string, object>(
            "payload",
            r => r.Payload,
            type => type == "int"
                ? (ICodec<object>)Codec.Int32LE.Map(v => (object)v, v => (int)v)
                : StringCodec.Map(v => (object)v, v => (string)v),
            dependencyIndex: 0,
            depGetter: r => r.Type);

        Assert.Equal("payload", def.Name);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Round-trip encode/decode with int payload")]
    public void RoundTrip_IntPayload()
    {
        var codec = Codec.Record<TypedPayload>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("type", type =>
                    type == "int"
                        ? Codec.Int32LE.Map(v => (object)v, v => (int)v)
                        : StringCodec.Map(v => (object)v, v => (string)v)))
            .Build((string t, object p) => new TypedPayload(t, p));

        var original = new TypedPayload("int", 42);
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original.Type, decoded.Type);
        Assert.Equal(original.Payload, decoded.Payload);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Round-trip with text payload")]
    public void RoundTrip_TextPayload()
    {
        var codec = Codec.Record<TypedPayload>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("type", type =>
                    type == "int"
                        ? Codec.Int32LE.Map(v => (object)v, v => (int)v)
                        : StringCodec.Map(v => (object)v, v => (string)v)))
            .Build((string t, object p) => new TypedPayload(t, p));

        var original = new TypedPayload("text", "hello");
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original.Type, decoded.Type);
        Assert.Equal(original.Payload, decoded.Payload);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Factory is called with the dependency value")]
    public void FactoryCalled_WithDependencyValue()
    {
        string? capturedDep = null;

        var codec = Codec.Record<TypedPayload>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("type", type =>
                {
                    capturedDep = type;
                    return Codec.Int32LE.Map(v => (object)v, v => (int)v);
                }))
            .Build((string t, object p) => new TypedPayload(t, p));

        // Generate valid encoded data using the record codec
        var record = new TypedPayload("int", 42);
        byte[] data = Codec.EncodeToArray(codec, record);
        Codec.Decode(codec, data);

        Assert.Equal("int", capturedDep);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Decode factory exception wraps in UserCodeException")]
    public void Decode_FactoryThrows_WrapsInUserCodeException()
    {
        var codec = Codec.Record<TypedPayload>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("type", type =>
                    throw new InvalidOperationException("oops")))
            .Build((string t, object p) => new TypedPayload(t, p));

        byte[] data = [0x04, 0x74, 0x65, 0x78, 0x74]; // VarInt32(4) + "text"

        var ex = Assert.Throws<UserCodeException>(() => Codec.Decode(codec, data));
        Assert.Contains("oops", ex.Message);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Encode dispatches to correct codec based on dep value")]
    public void Encode_UsesCorrectCodecPerDependency()
    {
        var codec = Codec.Record<TypedPayload>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("type", type =>
                    type == "int"
                        ? Codec.Int32LE.Map(v => (object)v, v => (int)v)
                        : StringCodec.Map(v => (object)v, v => (string)v)))
            .Build((string t, object p) => new TypedPayload(t, p));

        byte[] encodedInt = Codec.EncodeToArray(codec, new TypedPayload("int", 99));
        Assert.True(encodedInt.Length >= 7);

        byte[] encodedText = Codec.EncodeToArray(codec, new TypedPayload("txt", "hi"));
        Assert.True(encodedText.Length >= 5);

        var decodedInt = Codec.Decode(codec, encodedInt);
        Assert.Equal(99, decodedInt.Payload);

        var decodedText = Codec.Decode(codec, encodedText);
        Assert.Equal("hi", decodedText.Payload);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Two dependent fields in same record work")]
    public void TwoDependentFields_SameRecord()
    {
        var codec = Codec.Record<MultiDependentRecord>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payloadA", r => r.PayloadA,
                Codec.From<string, object>("type", type =>
                    type == "int"
                        ? Codec.Int32LE.Map(v => (object)v, v => (int)v)
                        : StringCodec.Map(v => (object)v, v => (string)v)))
            .Field("payloadB", r => r.PayloadB,
                Codec.From<string, object>("type", type =>
                    type == "int"
                        ? Codec.Int32LE.Map(v => (object)v, v => (int)v)
                        : StringCodec.Map(v => (object)v, v => (string)v)))
            .Build((string t, object a, object b) => new MultiDependentRecord(t, a, b));

        var original = new MultiDependentRecord("int", 10, 20);
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original.Type, decoded.Type);
        Assert.Equal(original.PayloadA, decoded.PayloadA);
        Assert.Equal(original.PayloadB, decoded.PayloadB);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Two dependent fields with different types from same dep")]
    public void TwoDependentFields_DifferentTypes()
    {
        var codec = Codec.Record<MultiDependentRecord>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payloadA", r => r.PayloadA,
                Codec.From<string, object>("type", type =>
                    type == "int"
                        ? Codec.Int32LE.Map(v => (object)v, v => (int)v)
                        : StringCodec.Map(v => (object)v, v => (string)v)))
            .Field("payloadB", r => r.PayloadB,
                Codec.From<string, object>("type", type =>
                    type == "int"
                        ? Codec.Int32BE.Map(v => (object)v, v => (int)v)
                        : Codec.Utf8String(5).Map(v => (object)v, v => (string)v)))
            .Build((string t, object a, object b) => new MultiDependentRecord(t, a, b));

        var original = new MultiDependentRecord("int", 0x01020304, 0x01020304);
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original.PayloadA, decoded.PayloadA);
        Assert.Equal(original.PayloadB, decoded.PayloadB);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Dependent field can depend on another dependent field")]
    public void DependentField_DependsOnDependentField()
    {
        var codec = Codec.Record<ChainedDependentRecord>()
            .Field("type", r => r.Type, StringCodec)
            .Field("subType", r => r.SubType,
                Codec.From<string, string>("type", type =>
                    type == "int" ? StringCodec : StringCodec))
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("subType", subType =>
                    subType == "i32"
                        ? Codec.Int32LE.Map(v => (object)v, v => (int)v)
                        : Codec.Int64LE.Map(v => (object)v, v => (long)v)))
            .Build((string t, string s, object p) => new ChainedDependentRecord(t, s, p));

        var original = new ChainedDependentRecord("int", "i32", 42);
        byte[] encoded = Codec.EncodeToArray(codec, original);
        Assert.True(encoded.Length >= 10);

        var decoded = Codec.Decode(codec, encoded);
        Assert.Equal(original.Type, decoded.Type);
        Assert.Equal(original.SubType, decoded.SubType);
        Assert.Equal(original.Payload, decoded.Payload);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Int payload with negative values round-trips")]
    public void RoundTrip_NegativeIntPayload()
    {
        var codec = Codec.Record<TypedPayload>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("type", type =>
                    Codec.Int32LE.Map(v => (object)v, v => (int)v)))
            .Build((string t, object p) => new TypedPayload(t, p));

        var original = new TypedPayload("int", -12345);
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original, decoded);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Text payload with special characters round-trips")]
    public void RoundTrip_SpecialCharsTextPayload()
    {
        var codec = Codec.Record<TypedPayload>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("type", type => StringCodec.Map(v => (object)v, v => (string)v)))
            .Build((string t, object p) => new TypedPayload(t, p));

        var original = new TypedPayload("text", "héllo wörld 🔥");
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original, decoded);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Encode factory exception wraps in UserCodeException")]
    public void Encode_FactoryThrows_WrapsInUserCodeException()
    {
        var codec = Codec.Record<TypedPayload>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("type", type =>
                    throw new ArgumentException("bad factory")))
            .Build((string t, object p) => new TypedPayload(t, p));

        var record = new TypedPayload("text", "should not matter");

        var ex = Assert.Throws<UserCodeException>(() =>
            Codec.EncodeToArray(codec, record));

        Assert.Contains("bad factory", ex.Message);
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Normal encode succeeds without exceptions")]
    public void Encode_NormalRecord_Succeeds()
    {
        var codec = Codec.Record<TypedPayload>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("type", type =>
                    Codec.Int32LE.Map(v => (object)v, v => (int)v)))
            .Build((string t, object p) => new TypedPayload(t, p));

        var record = new TypedPayload("int", 42);
        byte[] encoded = Codec.EncodeToArray(codec, record);

        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "DependentFieldDefinition: Empty string dependency value works")]
    public void EmptyStringDependency()
    {
        var codec = Codec.Record<TypedPayload>()
            .Field("type", r => r.Type, StringCodec)
            .Field("payload", r => r.Payload,
                Codec.From<string, object>("type", type =>
                    Codec.Int32LE.Map(v => (object)v, v => (int)v)))
            .Build((string t, object p) => new TypedPayload(t, p));

        var original = new TypedPayload("", 42);
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original, decoded);
    }
}
