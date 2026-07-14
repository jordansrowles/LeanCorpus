namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Codecs;

[Trait("Category", "CodecKit")]
public sealed class FieldValuesTests
{
    private record TwoFieldRecord(int Count, string Name);

    private record MixedTypesRecord(string Label, int Value, bool IsActive);

    private record SingleFieldRecord(string Id);

    private record NullableFieldRecord(string? MaybeNull);

    private static readonly ICodec<string> StringCodec =
        Codec.LengthPrefixed(Codec.VarInt32, Codec.Utf8StringRemaining(), TrailingDataPolicy.Allow);

    [Fact(DisplayName = "FieldValues: Indexer returns correct value for known field name")]
    public void Indexer_ReturnsCorrectValue()
    {
        var codec = Codec.Record<TwoFieldRecord>()
            .Field("count", r => r.Count, Codec.VarInt32)
            .Field("name", r => r.Name, StringCodec)
            .Build(f => new TwoFieldRecord((int)(f["count"]!), (string)(f["name"]!)));

        var original = new TwoFieldRecord(42, "hello");
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original, decoded);
    }

    [Fact(DisplayName = "FieldValues: Indexer returns correct value for specific known field")]
    public void Indexer_SpecificFieldValue()
    {
        var codec = Codec.Record<TwoFieldRecord>()
            .Field("count", r => r.Count, Codec.VarInt32)
            .Field("name", r => r.Name, StringCodec)
            .Build(f => new TwoFieldRecord((int)(f["count"]!), (string)(f["name"]!)));

        var original = new TwoFieldRecord(99, "world");
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(99, decoded.Count);
        Assert.Equal("world", decoded.Name);
    }

    [Fact(DisplayName = "FieldValues: Indexer returns null for a field that had a null value")]
    public void Indexer_ReturnsNullForNullValue()
    {
        var codec = Codec.Record<NullableFieldRecord>()
            .Field("maybeNull", r => r.MaybeNull, Codec.Optional(StringCodec, Codec.Bool))
            .Build(f => new NullableFieldRecord((string?)f["maybeNull"]));

        var withNull = new NullableFieldRecord(null);
        byte[] encoded = Codec.EncodeToArray(codec, withNull);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Null(decoded.MaybeNull);
    }

    [Fact(DisplayName = "FieldValues: Indexer throws ArgumentException for unknown field name")]
    public void Indexer_UnknownField_Throws()
    {
        var codec = Codec.Record<TwoFieldRecord>()
            .Field("count", r => r.Count, Codec.VarInt32)
            .Field("name", r => r.Name, StringCodec)
            .Build(f =>
            {
                var _ = f["nonexistent"];
                return new TwoFieldRecord(0, "");
            });

        // Encode a valid record, then decode triggers the builder which accesses nonexistent field
        var original = new TwoFieldRecord(42, "test");
        byte[] validData = Codec.EncodeToArray(codec, original);
        var ex = Assert.Throws<UserCodeException>(() => Codec.Decode(codec, validData));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Fact(DisplayName = "FieldValues: Indexer throws ArgumentException for empty string name")]
    public void Indexer_EmptyName_Throws()
    {
        var codec = Codec.Record<TwoFieldRecord>()
            .Field("count", r => r.Count, Codec.VarInt32)
            .Field("name", r => r.Name, StringCodec)
            .Build(f =>
            {
                var _ = f[""];
                return new TwoFieldRecord(0, "");
            });

        var original = new TwoFieldRecord(42, "test");
        byte[] validData = Codec.EncodeToArray(codec, original);
        var ex = Assert.Throws<UserCodeException>(() => Codec.Decode(codec, validData));
        Assert.IsType<ArgumentException>(ex.InnerException);
    }

    [Fact(DisplayName = "FieldValues: Works with different types (string, int, bool)")]
    public void Indexer_MixedTypes()
    {
        var codec = Codec.Record<MixedTypesRecord>()
            .Field("label", r => r.Label, StringCodec)
            .Field("value", r => r.Value, Codec.VarInt32)
            .Field("isActive", r => r.IsActive, Codec.Bool)
            .Build(f => new MixedTypesRecord(
                (string)(f["label"]!),
                (int)(f["value"]!),
                (bool)(f["isActive"]!)));

        var original = new MixedTypesRecord("test", 100, true);
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original, decoded);
    }

    [Fact(DisplayName = "FieldValues: Works with a single field")]
    public void Indexer_SingleField()
    {
        var codec = Codec.Record<SingleFieldRecord>()
            .Field("id", r => r.Id, StringCodec)
            .Build(f => new SingleFieldRecord((string)(f["id"]!)));

        var original = new SingleFieldRecord("abc123");
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original, decoded);
    }
}
