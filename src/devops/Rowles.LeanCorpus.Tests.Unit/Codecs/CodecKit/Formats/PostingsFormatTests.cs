namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class PostingsFormatTests
{
    [Fact(DisplayName = "PostingsFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(PostingsFormat.V1);
    }

    [Fact(DisplayName = "PostingsFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var data = new PostingsFormat.Data
        {
            DocFreq = 5,
            SkipOffset = 100,
            HasFreqs = true,
            HasPositions = true,
            HasPayloads = false,
            Body = [1, 2]
        };
        byte[] encoded = Codec.EncodeToArray(PostingsFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "PostingsFormat: Data class properties are set correctly")]
    public void DataClass_PropertiesSetCorrectly()
    {
        var data = new PostingsFormat.Data
        {
            DocFreq = 5,
            SkipOffset = 100L,
            HasFreqs = true,
            HasPositions = true,
            HasPayloads = false,
            Body = [1, 2]
        };
        Assert.Equal(5, data.DocFreq);
        Assert.Equal(100L, data.SkipOffset);
        Assert.True(data.HasFreqs);
        Assert.True(data.HasPositions);
        Assert.False(data.HasPayloads);
        Assert.NotNull(data.Body);
        Assert.Equal(2, data.Body.Count);
    }

    [Fact(DisplayName = "PostingsFormat: Data class with all bools false")]
    public void DataClass_AllBoolsFalse()
    {
        var data = new PostingsFormat.Data
        {
            DocFreq = 0,
            SkipOffset = 0,
            HasFreqs = false,
            HasPositions = false,
            HasPayloads = false,
            Body = [10]
        };
        Assert.Equal(0, data.DocFreq);
        Assert.Equal(0L, data.SkipOffset);
        Assert.False(data.HasFreqs);
        Assert.False(data.HasPositions);
        Assert.False(data.HasPayloads);
        Assert.NotNull(data.Body);
        Assert.Equal([10], data.Body);
    }

    [Fact(DisplayName = "PostingsFormat: Data class with all bools true")]
    public void DataClass_AllBoolsTrue()
    {
        var data = new PostingsFormat.Data
        {
            DocFreq = 1,
            SkipOffset = 1,
            HasFreqs = true,
            HasPositions = true,
            HasPayloads = true,
            Body = [0]
        };
        Assert.True(data.HasFreqs);
        Assert.True(data.HasPositions);
        Assert.True(data.HasPayloads);
    }

    [Fact(DisplayName = "PostingsFormat: V1 Encode with empty Body")]
    public void V1_Encode_EmptyBody()
    {
        var data = new PostingsFormat.Data
        {
            DocFreq = 3,
            SkipOffset = 50,
            HasFreqs = true,
            HasPositions = false,
            HasPayloads = true,
            Body = []
        };
        byte[] encoded = Codec.EncodeToArray(PostingsFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "PostingsFormat: V1 Encode with large offsets")]
    public void V1_Encode_LargeOffsets()
    {
        var data = new PostingsFormat.Data
        {
            DocFreq = int.MaxValue,
            SkipOffset = long.MaxValue,
            HasFreqs = true,
            HasPositions = false,
            HasPayloads = false,
            Body = [0xDE, 0xAD, 0xBE, 0xEF]
        };
        byte[] encoded = Codec.EncodeToArray(PostingsFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
