namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class BKDFormatTests
{
    [Fact(DisplayName = "BKDFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(BKDFormat.V1);
    }

    [Fact(DisplayName = "BKDFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var data = new BKDFormat.Data { FieldCount = 3, FieldsData = [10, 20, 30] };
        byte[] encoded = Codec.EncodeToArray(BKDFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "BKDFormat: Data class properties are set correctly")]
    public void DataClass_PropertiesSetCorrectly()
    {
        var data = new BKDFormat.Data { FieldCount = 5, FieldsData = [1, 2, 3, 4, 5] };
        Assert.Equal(5, data.FieldCount);
        Assert.NotNull(data.FieldsData);
        Assert.Equal(5, data.FieldsData.Count);
    }

    [Fact(DisplayName = "BKDFormat: Data class with empty FieldsData")]
    public void DataClass_EmptyFieldsData()
    {
        var data = new BKDFormat.Data { FieldCount = 0, FieldsData = [] };
        Assert.Equal(0, data.FieldCount);
        Assert.NotNull(data.FieldsData);
        Assert.Empty(data.FieldsData);
    }

    [Fact(DisplayName = "BKDFormat: V1 Encode with single byte FieldsData")]
    public void V1_Encode_SingleByteFieldsData()
    {
        var data = new BKDFormat.Data { FieldCount = 1, FieldsData = [42] };
        byte[] encoded = Codec.EncodeToArray(BKDFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "BKDFormat: V1 Encode with large FieldsData")]
    public void V1_Encode_LargeFieldsData()
    {
        var largeData = new byte[1024];
        Random.Shared.NextBytes(largeData);
        var data = new BKDFormat.Data { FieldCount = 5, FieldsData = largeData };
        byte[] encoded = Codec.EncodeToArray(BKDFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
