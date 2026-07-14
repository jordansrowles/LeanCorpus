namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Codecs;

[Trait("Category", "CodecKit")]
public sealed class DependentFieldFactoryTests
{
    [Fact(DisplayName = "DependentFieldFactory: Constructor sets FieldName correctly")]
    public void Constructor_SetsFieldName()
    {
        var factory = Codec.From<string, int>("length", _ => Codec.VarInt32);
        Assert.Equal("length", factory.FieldName);
    }

    [Fact(DisplayName = "DependentFieldFactory: Constructor sets Factory correctly")]
    public void Constructor_SetsFactory()
    {
        var factory = Codec.From<string, int>("len", dep => Codec.VarInt32);
        Assert.NotNull(factory.Factory);
    }

    [Fact(DisplayName = "DependentFieldFactory: Constructor throws ArgumentNullException when fieldName is null")]
    public void Constructor_NullFieldName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Codec.From<string, int>(null!, _ => Codec.VarInt32));
    }

    [Fact(DisplayName = "DependentFieldFactory: Constructor throws ArgumentNullException when factory is null")]
    public void Constructor_NullFactory_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Codec.From<string, int>("name", null!));
    }

    [Fact(DisplayName = "DependentFieldFactory: Factory can be invoked with a TDep value and returns an ICodec<TOut>")]
    public void Factory_Invocation_ReturnsCodec()
    {
        var factory = Codec.From<int, int>("source", dep => Codec.VarInt32);

        ICodec<int> codec = factory.Factory(42);

        Assert.NotNull(codec);
        Assert.Same(Codec.VarInt32, codec);
    }

    [Fact(DisplayName = "DependentFieldFactory: Factory produces different codec based on TDep")]
    public void Factory_ProducesDifferentCodecBasedOnDep()
    {
        var factory = Codec.From<int, int>("source", dep =>
            dep > 100 ? Codec.Int32LE : Codec.VarInt32);

        ICodec<int> large = factory.Factory(200);
        ICodec<int> small = factory.Factory(50);

        Assert.Same(Codec.Int32LE, large);
        Assert.Same(Codec.VarInt32, small);
    }

    [Fact(DisplayName = "DependentFieldFactory: Works with string TDep and different TOut")]
    public void Factory_StringDep_DifferentOut()
    {
        var factory = Codec.From<string, byte[]>("payload", dep =>
            Codec.FixedFrame(dep.Length, Codec.BytesOwnedRemaining(), FramePadding.Exact, TrailingDataPolicy.Allow));

        ICodec<byte[]> codec = factory.Factory("hello");

        Assert.NotNull(codec);
    }

    [Fact(DisplayName = "DependentFieldFactory: Can be used in RecordBuilder Field chain")]
    public void Factory_WorksInRecordBuilder()
    {
        var codec = Codec.Record<(int Len, byte[] Data)>()
            .Field("length", r => r.Len, Codec.VarInt32)
            .Field("data", r => r.Data,
                Codec.From<int, byte[]>("length", dep =>
                    Codec.FixedFrame(dep, Codec.BytesOwnedRemaining(), FramePadding.Exact, TrailingDataPolicy.Allow)))
            .Build((int len, byte[] data) => (len, data));

        var original = (Len: 3, Data: new byte[] { 0xAA, 0xBB, 0xCC });
        byte[] encoded = Codec.EncodeToArray(codec, original);
        var decoded = Codec.Decode(codec, encoded);

        Assert.Equal(original.Len, decoded.Len);
        Assert.Equal(original.Data, decoded.Data);
    }
}
