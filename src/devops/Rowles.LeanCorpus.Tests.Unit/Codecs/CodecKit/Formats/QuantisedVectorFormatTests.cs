namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class QuantisedVectorFormatTests
{
    [Fact(DisplayName = "QuantisedVectorFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(QuantisedVectorFormat.V1);
    }

    [Fact(DisplayName = "QuantisedVectorFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var data = new QuantisedVectorFormat.Data
        {
            DocCount = 100,
            Dimension = 768,
            Quantisation = 1,
            QMin = -1.5f,
            QAlpha = 0.01f,
            TailLength = 4,
            Tail = [1, 2, 3, 4]
        };
        byte[] encoded = Codec.EncodeToArray(QuantisedVectorFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "QuantisedVectorFormat: Data class properties are set correctly")]
    public void DataClass_PropertiesSetCorrectly()
    {
        var data = new QuantisedVectorFormat.Data
        {
            DocCount = 100,
            Dimension = 768,
            Quantisation = 1,
            QMin = -1.5f,
            QAlpha = 0.01f,
            TailLength = 4,
            Tail = [1, 2, 3, 4]
        };
        Assert.Equal(100, data.DocCount);
        Assert.Equal(768, data.Dimension);
        Assert.Equal(1, data.Quantisation);
        Assert.Equal(-1.5f, data.QMin);
        Assert.Equal(0.01f, data.QAlpha);
        Assert.Equal(4, data.TailLength);
        Assert.NotNull(data.Tail);
        Assert.Equal(4, data.Tail.Count);
    }

    [Fact(DisplayName = "QuantisedVectorFormat: Data class with empty Tail")]
    public void DataClass_EmptyTail()
    {
        var data = new QuantisedVectorFormat.Data
        {
            DocCount = 50,
            Dimension = 128,
            Quantisation = 0,
            QMin = 0.0f,
            QAlpha = 1.0f,
            TailLength = 0,
            Tail = []
        };
        Assert.Equal(50, data.DocCount);
        Assert.Equal(0, data.TailLength);
        Assert.NotNull(data.Tail);
        Assert.Empty(data.Tail);
    }

    [Fact(DisplayName = "QuantisedVectorFormat: Data class with zero values")]
    public void DataClass_ZeroValues()
    {
        var data = new QuantisedVectorFormat.Data
        {
            DocCount = 0,
            Dimension = 0,
            Quantisation = 0,
            QMin = 0.0f,
            QAlpha = 0.0f,
            TailLength = 0,
            Tail = []
        };
        Assert.Equal(0, data.DocCount);
        Assert.Equal(0, data.Dimension);
        Assert.Equal(0, data.Quantisation);
        Assert.Equal(0.0f, data.QMin);
        Assert.Equal(0.0f, data.QAlpha);
        Assert.Empty(data.Tail);
    }

    [Fact(DisplayName = "QuantisedVectorFormat: V1 Encode with negative QMin")]
    public void V1_Encode_NegativeQMin()
    {
        var data = new QuantisedVectorFormat.Data
        {
            DocCount = 10,
            Dimension = 4,
            Quantisation = 2,
            QMin = -100.0f,
            QAlpha = 0.5f,
            TailLength = 2,
            Tail = [0xFF, 0x00]
        };
        byte[] encoded = Codec.EncodeToArray(QuantisedVectorFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "QuantisedVectorFormat: V1 Encode with large Tail")]
    public void V1_Encode_LargeTail()
    {
        var largeTail = new byte[4096];
        Random.Shared.NextBytes(largeTail);
        var data = new QuantisedVectorFormat.Data
        {
            DocCount = 500,
            Dimension = 1024,
            Quantisation = 3,
            QMin = 0.0f,
            QAlpha = 0.1f,
            TailLength = largeTail.Length,
            Tail = largeTail
        };
        byte[] encoded = Codec.EncodeToArray(QuantisedVectorFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
