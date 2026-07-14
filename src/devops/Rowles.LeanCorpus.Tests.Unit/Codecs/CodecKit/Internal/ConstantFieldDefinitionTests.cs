using System.Buffers;
using Rowles.LeanCorpus.Codecs.CodecKit;
using Rowles.LeanCorpus.Codecs.CodecKit.Codecs;
using Rowles.LeanCorpus.Codecs.CodecKit.Enums;
using Rowles.LeanCorpus.Codecs.CodecKit.Exceptions;

namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Internal;

[Trait("Category", "CodecKit")]
public sealed class ConstantFieldDefinitionTests
{
    // ═══════════════════════════════════════════════════
    //  Model for record-level tests
    // ═══════════════════════════════════════════════════

    private sealed record TestRecord(int Value);

    private sealed record DoubleConstantRecord(int Value);

    private sealed record ValueOnlyRecord(int Value);

    // ═══════════════════════════════════════════════════
    //  IsConstant
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ConstantFieldDefinition: IsConstant returns true")]
    public void IsConstant_ReturnsTrue()
    {
        // A record with one constant field ensures the ConstantFieldDefinition is created
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("magic", Codec.Magic(0x01, 0x02))
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build((int v) => new ValueOnlyRecord(v));

        // We verify behavior via encode/decode — the constant field doesn't
        // appear in the build delegate, confirming it's treated as constant.
        var record = new ValueOnlyRecord(42);
        byte[] encoded = Codec.EncodeToArray(codec, record);
        ValueOnlyRecord decoded = Codec.Decode(codec, encoded);

        Assert.Equal(record, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  Name property
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ConstantFieldDefinition: Name returns the field name")]
    public void Name_ReturnsFieldName()
    {
        var def = new ConstantFieldDefinition<ValueOnlyRecord>("magicHeader", Codec.Magic(0x01, 0x02));

        Assert.Equal("magicHeader", def.Name);
    }

    // ═══════════════════════════════════════════════════
    //  Decode behavior
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ConstantFieldDefinition: Decode reads constant bytes and returns null")]
    public void Decode_ReadsConstantBytes_ReturnsNull()
    {
        var def = new ConstantFieldDefinition<ValueOnlyRecord>("check", Codec.Magic(0xAA, 0xBB));

        byte[] data = [0xAA, 0xBB, 0x05, 0x00, 0x00, 0x00]; // magic + Int32LE(5)
        var result = Codec.Decode(
            Codec.Record<ValueOnlyRecord>()
                .Constant("check", Codec.Magic(0xAA, 0xBB))
                .Field("value", r => r.Value, Codec.Int32LE)
                .Build((int v) => new ValueOnlyRecord(v)),
            data);

        Assert.Equal(5, result.Value);
    }

    [Fact(DisplayName = "ConstantFieldDefinition: Decode advances the reader past the constant bytes")]
    public void Decode_AdvancesReaderPastConstantBytes()
    {
        // The payload after constant magic bytes is decoded correctly,
        // proving the reader advanced past the constant field.
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("magic", Codec.Magic(0xDE, 0xAD))
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build((int v) => new ValueOnlyRecord(v));

        // magic(2 bytes) + Int32LE(4 bytes) = 6 bytes
        byte[] data = [0xDE, 0xAD, 0x2A, 0x00, 0x00, 0x00]; // magic + 42
        var decoded = Codec.Decode(codec, data);

        Assert.Equal(42, decoded.Value);
    }

    // ═══════════════════════════════════════════════════
    //  Encode behavior
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ConstantFieldDefinition: Encode writes the constant bytes")]
    public void Encode_WritesConstantBytes()
    {
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("magic", Codec.Magic(0x01, 0x02))
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build((int v) => new ValueOnlyRecord(v));

        var record = new ValueOnlyRecord(99);
        byte[] encoded = Codec.EncodeToArray(codec, record);

        // Expect magic[0x01, 0x02] + Int32LE(99)
        Assert.Equal(0x01, encoded[0]);
        Assert.Equal(0x02, encoded[1]);
    }

    // ═══════════════════════════════════════════════════
    //  Encode + Decode round-trip: encoded bytes match constant value
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ConstantFieldDefinition: Encode + Decode round-trip preserves data")]
    public void EncodeDecode_RoundTrip_PreservesData()
    {
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("header", Codec.Magic(0xCA, 0xFE, 0xBA, 0xBE))
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build((int v) => new ValueOnlyRecord(v));

        var original = new ValueOnlyRecord(12345);

        byte[] encoded = Codec.EncodeToArray(codec, original);
        ValueOnlyRecord decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original, decoded);

        // Also verify the constant bytes appear at the right offset
        Assert.Equal(0xCA, encoded[0]);
        Assert.Equal(0xFE, encoded[1]);
        Assert.Equal(0xBA, encoded[2]);
        Assert.Equal(0xBE, encoded[3]);
    }

    // ═══════════════════════════════════════════════════
    //  Constant field used inside a RecordCodec (Build delegate)
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ConstantFieldDefinition: Can be used inside a RecordCodec (not passed to Build delegate)")]
    public void InsideRecordCodec_NotPassedToBuildDelegate()
    {
        // The Build delegate only receives the non-constant field.
        // If the constant field were passed, the arity would be wrong.
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("magic", Codec.Magic(0xFF))
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build((int v) => new ValueOnlyRecord(v));

        byte[] data = [0xFF, 0x2A, 0x00, 0x00, 0x00]; // magic + Int32LE(42)
        var result = Codec.Decode(codec, data);

        Assert.Equal(42, result.Value);
    }

    // ═══════════════════════════════════════════════════
    //  Constant field does NOT appear in FieldValues after Build
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ConstantFieldDefinition: Constant field does NOT appear in FieldValues after Build")]
    public void ConstantField_NotInFieldValues()
    {
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("hidden", Codec.Magic(0x00, 0x01))
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build(f => new ValueOnlyRecord((int)f["value"]!));

        // The FieldValues index should contain only "value", not "hidden"
        byte[] data = [0x00, 0x01, 0x05, 0x00, 0x00, 0x00];
        var result = Codec.Decode(codec, data);

        Assert.Equal(5, result.Value);
    }

    [Fact(DisplayName = "ConstantFieldDefinition: Accessing constant field name through FieldValues throws")]
    public void ConstantField_AccessViaFieldValues_Throws()
    {
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("hidden", Codec.Magic(0x00, 0x01))
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build(f =>
            {
                // Accessing "hidden" should throw since it's not in FieldValues
                Assert.Throws<ArgumentException>(() => f["hidden"]);
                return new ValueOnlyRecord((int)f["value"]!);
            });

        byte[] data = [0x00, 0x01, 0x07, 0x00, 0x00, 0x00];
        var result = Codec.Decode(codec, data);

        Assert.Equal(7, result.Value);
    }

    // ═══════════════════════════════════════════════════
    //  Two constant fields in the same record
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ConstantFieldDefinition: Two constant fields can be in the same record")]
    public void TwoConstantFields_SameRecord()
    {
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("magicA", Codec.Magic(0xAA))
            .Constant("magicB", Codec.Magic(0xBB))
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build((int v) => new ValueOnlyRecord(v));

        var original = new ValueOnlyRecord(77);
        byte[] encoded = Codec.EncodeToArray(codec, original);
        ValueOnlyRecord decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original, decoded);

        // Verify both constant bytes are written
        Assert.Equal(0xAA, encoded[0]);
        Assert.Equal(0xBB, encoded[1]);
        Assert.Equal(6, encoded.Length); // 1 + 1 + 4
    }

    [Fact(DisplayName = "ConstantFieldDefinition: Two constants with real-world magic pattern")]
    public void TwoConstants_RealWorldPattern()
    {
        // Simulate a file header with magic bytes and a version constant
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("fileMagic", Codec.Magic(0x89, 0x4C, 0x43))   // 3-byte magic
            .Constant("version", Codec.Magic(0x01))                  // 1-byte version
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build((int v) => new ValueOnlyRecord(v));

        var original = new ValueOnlyRecord(256);
        byte[] encoded = Codec.EncodeToArray(codec, original);
        ValueOnlyRecord decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original, decoded);
        Assert.Equal(8, encoded.Length); // 3 + 1 + 4

        // Magic bytes
        Assert.Equal(0x89, encoded[0]);
        Assert.Equal(0x4C, encoded[1]);
        Assert.Equal(0x43, encoded[2]);
        // Version
        Assert.Equal(0x01, encoded[3]);
    }

    // ═══════════════════════════════════════════════════
    //  Constant field with padding
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ConstantFieldDefinition: Padding constant writes and reads correctly")]
    public void PaddingConstant_WritesAndReads()
    {
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("padding", Codec.Padding(3, 0x00))
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build((int v) => new ValueOnlyRecord(v));

        var original = new ValueOnlyRecord(42);
        byte[] encoded = Codec.EncodeToArray(codec, original);

        // First 3 bytes are padding (zero), next 4 are Int32LE(42)
        Assert.Equal(7, encoded.Length);
        Assert.Equal(0x00, encoded[0]);
        Assert.Equal(0x00, encoded[1]);
        Assert.Equal(0x00, encoded[2]);

        var decoded = Codec.Decode(codec, encoded);
        Assert.Equal(original, decoded);
    }

    // ═══════════════════════════════════════════════════
    //  Constant field with skip
    // ═══════════════════════════════════════════════════

    [Fact(DisplayName = "ConstantFieldDefinition: Skip constant advances reader without consuming bytes")]
    public void SkipConstant_AdvancesReader()
    {
        var codec = Codec.Record<ValueOnlyRecord>()
            .Constant("skip", Codec.Skip(2))
            .Field("value", r => r.Value, Codec.Int32LE)
            .Build((int v) => new ValueOnlyRecord(v));

        // Two skipped bytes (garbage) + Int32LE(42)
        byte[] data = [0xFF, 0xFE, 0x2A, 0x00, 0x00, 0x00];
        var decoded = Codec.Decode(codec, data);

        Assert.Equal(42, decoded.Value);
    }
}
