namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class HnswFormatTests
{
    [Fact(DisplayName = "HnswFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(HnswFormat.V1);
    }

    [Fact(DisplayName = "HnswFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var data = new HnswFormat.Data { Dimension = 128, Normalised = true, Body = [1, 2, 3] };
        byte[] encoded = Codec.EncodeToArray(HnswFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "HnswFormat: Data class properties are set correctly")]
    public void DataClass_PropertiesSetCorrectly()
    {
        var data = new HnswFormat.Data { Dimension = 64, Normalised = false, Body = [10, 20] };
        Assert.Equal(64, data.Dimension);
        Assert.False(data.Normalised);
        Assert.NotNull(data.Body);
        Assert.Equal(2, data.Body.Count);
    }

    [Fact(DisplayName = "HnswFormat: Data class with Normalised=true")]
    public void DataClass_NormalisedTrue()
    {
        var data = new HnswFormat.Data { Dimension = 256, Normalised = true, Body = [] };
        Assert.Equal(256, data.Dimension);
        Assert.True(data.Normalised);
        Assert.NotNull(data.Body);
        Assert.Empty(data.Body);
    }

    [Fact(DisplayName = "HnswFormat: V1 Encode with single byte Body")]
    public void V1_Encode_SingleBody()
    {
        var data = new HnswFormat.Data { Dimension = 4, Normalised = true, Body = [0xFF] };
        byte[] encoded = Codec.EncodeToArray(HnswFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "HnswFormat: V1 Encode with Dimension=0")]
    public void V1_Encode_ZeroDimension()
    {
        var data = new HnswFormat.Data { Dimension = 0, Normalised = false, Body = [] };
        byte[] encoded = Codec.EncodeToArray(HnswFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
