namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class TermDictionaryFormatTests
{
    [Fact(DisplayName = "TermDictionaryFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(TermDictionaryFormat.V1);
    }

    [Fact(DisplayName = "TermDictionaryFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var data = new TermDictionaryFormat.Data
        {
            FstLength = 4,
            FstBlob = [0x46, 0x53, 0x54, 0x31] // "FST1"
        };
        byte[] encoded = Codec.EncodeToArray(TermDictionaryFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "TermDictionaryFormat: Data class properties are set correctly")]
    public void DataClass_PropertiesSetCorrectly()
    {
        var data = new TermDictionaryFormat.Data
        {
            FstLength = 4,
            FstBlob = [0x46, 0x53, 0x54, 0x31]
        };
        Assert.Equal(4, data.FstLength);
        Assert.NotNull(data.FstBlob);
        Assert.Equal(4, data.FstBlob.Count);
    }

    [Fact(DisplayName = "TermDictionaryFormat: Data class with empty FstBlob")]
    public void DataClass_EmptyFstBlob()
    {
        var data = new TermDictionaryFormat.Data
        {
            FstLength = 0,
            FstBlob = []
        };
        Assert.Equal(0, data.FstLength);
        Assert.NotNull(data.FstBlob);
        Assert.Empty(data.FstBlob);
    }

    [Fact(DisplayName = "TermDictionaryFormat: V1 Encode with single byte")]
    public void V1_Encode_SingleByte()
    {
        var data = new TermDictionaryFormat.Data
        {
            FstLength = 1,
            FstBlob = [0x00]
        };
        byte[] encoded = Codec.EncodeToArray(TermDictionaryFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "TermDictionaryFormat: V1 Encode with large blob")]
    public void V1_Encode_LargeBlob()
    {
        var largeBlob = new byte[4096];
        Random.Shared.NextBytes(largeBlob);
        var data = new TermDictionaryFormat.Data
        {
            FstLength = largeBlob.Length,
            FstBlob = largeBlob
        };
        byte[] encoded = Codec.EncodeToArray(TermDictionaryFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "TermDictionaryFormat: V1 Encode with arbitrary bytes")]
    public void V1_Encode_ArbitraryBytes()
    {
        var data = new TermDictionaryFormat.Data
        {
            FstLength = 8,
            FstBlob = [0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE]
        };
        byte[] encoded = Codec.EncodeToArray(TermDictionaryFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
