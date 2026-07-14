namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class TermVectorsFormatTests
{
    [Fact(DisplayName = "TermVectorsFormat: V2 codec is non-null")]
    public void V2_CodecIsNonNull()
    {
        Assert.NotNull(TermVectorsFormat.V2);
    }

    [Fact(DisplayName = "TermVectorsFormat: V2 Encode produces non-empty bytes")]
    public void V2_Encode_ProducesNonEmptyBytes()
    {
        var data = new TermVectorsFormat.Data
        {
            DocCount = 5,
            Body = [1, 2, 3]
        };
        byte[] encoded = Codec.EncodeToArray(TermVectorsFormat.V2, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "TermVectorsFormat: Data class properties are set correctly")]
    public void DataClass_PropertiesSetCorrectly()
    {
        var data = new TermVectorsFormat.Data
        {
            DocCount = 5,
            Body = [1, 2, 3]
        };
        Assert.Equal(5, data.DocCount);
        Assert.NotNull(data.Body);
        Assert.Equal(3, data.Body.Count);
    }

    [Fact(DisplayName = "TermVectorsFormat: Data class with DocCount=0")]
    public void DataClass_DocCountZero()
    {
        var data = new TermVectorsFormat.Data
        {
            DocCount = 0,
            Body = [10, 20, 30, 40]
        };
        Assert.Equal(0, data.DocCount);
        Assert.NotNull(data.Body);
        Assert.Equal(4, data.Body.Count);
    }

    [Fact(DisplayName = "TermVectorsFormat: Data class with empty Body")]
    public void DataClass_EmptyBody()
    {
        var data = new TermVectorsFormat.Data
        {
            DocCount = 3,
            Body = []
        };
        Assert.Equal(3, data.DocCount);
        Assert.NotNull(data.Body);
        Assert.Empty(data.Body);
    }

    [Fact(DisplayName = "TermVectorsFormat: V2 Encode with minimal data")]
    public void V2_Encode_MinimalData()
    {
        var data = new TermVectorsFormat.Data
        {
            DocCount = 0,
            Body = []
        };
        byte[] encoded = Codec.EncodeToArray(TermVectorsFormat.V2, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "TermVectorsFormat: V2 Encode with large Body")]
    public void V2_Encode_LargeBody()
    {
        var largeBody = new byte[2048];
        Random.Shared.NextBytes(largeBody);
        var data = new TermVectorsFormat.Data
        {
            DocCount = 100,
            Body = largeBody
        };
        byte[] encoded = Codec.EncodeToArray(TermVectorsFormat.V2, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
