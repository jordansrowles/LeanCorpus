namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class VectorFormatTests
{
    [Fact(DisplayName = "VectorFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(VectorFormat.V1);
    }

    [Fact(DisplayName = "VectorFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var data = new VectorFormat.Data
        {
            DocCount = 2,
            Dimension = 3,
            Format = 0,
            Values = [1f, 2f, 3f, 4f, 5f, 6f]
        };
        byte[] encoded = Codec.EncodeToArray(VectorFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "VectorFormat: Data class properties are set correctly")]
    public void DataClass_PropertiesSetCorrectly()
    {
        var data = new VectorFormat.Data
        {
            DocCount = 2,
            Dimension = 3,
            Format = 0,
            Values = [1f, 2f, 3f, 4f, 5f, 6f]
        };
        Assert.Equal(2, data.DocCount);
        Assert.Equal(3, data.Dimension);
        Assert.Equal(0, data.Format);
        Assert.NotNull(data.Values);
        Assert.Equal(6, data.Values.Length);
        Assert.Equal([1f, 2f, 3f, 4f, 5f, 6f], data.Values);
    }

    [Fact(DisplayName = "VectorFormat: Data class with empty Values")]
    public void DataClass_EmptyValues()
    {
        var data = new VectorFormat.Data
        {
            DocCount = 0,
            Dimension = 0,
            Format = 0,
            Values = []
        };
        Assert.Equal(0, data.DocCount);
        Assert.Equal(0, data.Dimension);
        Assert.NotNull(data.Values);
        Assert.Empty(data.Values);
    }

    [Fact(DisplayName = "VectorFormat: V1 Encode with Format=1")]
    public void V1_Encode_FormatOne()
    {
        var data = new VectorFormat.Data
        {
            DocCount = 1,
            Dimension = 4,
            Format = 1,
            Values = [0.5f, 1.5f, 2.5f, 3.5f]
        };
        byte[] encoded = Codec.EncodeToArray(VectorFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "VectorFormat: Data class with zero values")]
    public void DataClass_ZeroValues()
    {
        var data = new VectorFormat.Data
        {
            DocCount = 1,
            Dimension = 1,
            Format = 0,
            Values = [0.0f]
        };
        Assert.Equal(1, data.DocCount);
        Assert.Equal(1, data.Dimension);
        Assert.Equal([0.0f], data.Values);
    }

    [Fact(DisplayName = "VectorFormat: V1 Encode with single doc multi-dim")]
    public void V1_Encode_SingleDocMultiDim()
    {
        var data = new VectorFormat.Data
        {
            DocCount = 1,
            Dimension = 128,
            Format = 2,
            Values = Enumerable.Range(0, 128).Select(i => (float)i).ToArray()
        };
        byte[] encoded = Codec.EncodeToArray(VectorFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
