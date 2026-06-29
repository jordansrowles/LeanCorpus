namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class StoredFieldsFormatTests
{
    [Fact(DisplayName = "StoredFieldsFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(StoredFieldsFormat.V1);
    }

    [Fact(DisplayName = "StoredFieldsFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var data = new StoredFieldsFormat.Data
        {
            BlockSize = 32768,
            Compression = 1,
            Blocks = [1, 2, 3]
        };
        byte[] encoded = Codec.EncodeToArray(StoredFieldsFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "StoredFieldsFormat: Data class properties are set correctly")]
    public void DataClass_PropertiesSetCorrectly()
    {
        var data = new StoredFieldsFormat.Data
        {
            BlockSize = 32768,
            Compression = 1,
            Blocks = [1, 2, 3]
        };
        Assert.Equal(32768, data.BlockSize);
        Assert.Equal(1, data.Compression);
        Assert.NotNull(data.Blocks);
        Assert.Equal(3, data.Blocks.Count);
    }

    [Fact(DisplayName = "StoredFieldsFormat: Data class with Compression=0")]
    public void DataClass_CompressionZero()
    {
        var data = new StoredFieldsFormat.Data
        {
            BlockSize = 4096,
            Compression = 0,
            Blocks = [10, 20]
        };
        Assert.Equal(4096, data.BlockSize);
        Assert.Equal(0, data.Compression);
        Assert.NotNull(data.Blocks);
        Assert.Equal([10, 20], data.Blocks);
    }

    [Fact(DisplayName = "StoredFieldsFormat: Data class with empty Blocks")]
    public void DataClass_EmptyBlocks()
    {
        var data = new StoredFieldsFormat.Data
        {
            BlockSize = 8192,
            Compression = 2,
            Blocks = []
        };
        Assert.Equal(8192, data.BlockSize);
        Assert.Equal(2, data.Compression);
        Assert.NotNull(data.Blocks);
        Assert.Empty(data.Blocks);
    }

    [Fact(DisplayName = "StoredFieldsFormat: V1 Encode with minimal data")]
    public void V1_Encode_MinimalData()
    {
        var data = new StoredFieldsFormat.Data
        {
            BlockSize = 0,
            Compression = 0,
            Blocks = []
        };
        byte[] encoded = Codec.EncodeToArray(StoredFieldsFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "StoredFieldsFormat: V1 Encode with large Blocks")]
    public void V1_Encode_LargeBlocks()
    {
        var largeBlocks = new byte[8192];
        Random.Shared.NextBytes(largeBlocks);
        var data = new StoredFieldsFormat.Data
        {
            BlockSize = 65536,
            Compression = 3,
            Blocks = largeBlocks
        };
        byte[] encoded = Codec.EncodeToArray(StoredFieldsFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
