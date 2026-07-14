namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class BinaryDocValuesFormatTests
{
    private static BinaryDocValuesFormat.Field MakeField(string name, int docCount, int[] docStarts, byte[][] values)
        => new() { Name = name, DocCount = docCount, DocStarts = docStarts, Values = values };

    [Fact(DisplayName = "BinaryDocValuesFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(BinaryDocValuesFormat.V1);
    }

    [Fact(DisplayName = "BinaryDocValuesFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var fields = new List<BinaryDocValuesFormat.Field>
        {
            MakeField("f1", 2, [0, 1], [[1, 2], [3]])
        };
        var data = new BinaryDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(BinaryDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "BinaryDocValuesFormat: Data class Field properties are set correctly")]
    public void DataClass_FieldPropertiesSetCorrectly()
    {
        var field = MakeField("f1", 2, [0, 1], [[1, 2], [3]]);
        Assert.Equal("f1", field.Name);
        Assert.Equal(2, field.DocCount);
        Assert.NotNull(field.DocStarts);
        Assert.Equal([0, 1], field.DocStarts);
        Assert.NotNull(field.Values);
        Assert.Equal(2, field.Values.Count);
        Assert.NotNull(field.Values[0]);
        Assert.Equal([1, 2], field.Values[0]);
        Assert.NotNull(field.Values[1]);
        Assert.Equal([3], field.Values[1]);
    }

    [Fact(DisplayName = "BinaryDocValuesFormat: Data class with empty Fields list")]
    public void DataClass_EmptyFields()
    {
        var data = new BinaryDocValuesFormat.Data { Fields = [] };
        Assert.NotNull(data.Fields);
        Assert.Empty(data.Fields);
    }

    [Fact(DisplayName = "BinaryDocValuesFormat: V1 Encode with multiple fields")]
    public void V1_Encode_MultipleFields()
    {
        var fields = new List<BinaryDocValuesFormat.Field>
        {
            MakeField("a", 1, [0], [[10]]),
            MakeField("b", 2, [0, 2], [[20, 21], [30]])
        };
        var data = new BinaryDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(BinaryDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "BinaryDocValuesFormat: Data class with empty values")]
    public void DataClass_EmptyValues()
    {
        var field = MakeField("x", 0, [], []);
        Assert.Equal("x", field.Name);
        Assert.Equal(0, field.DocCount);
        Assert.NotNull(field.DocStarts);
        Assert.Empty(field.DocStarts);
        Assert.NotNull(field.Values);
        Assert.Empty(field.Values);
    }

    [Fact(DisplayName = "BinaryDocValuesFormat: V1 Encode with large values")]
    public void V1_Encode_LargeValues()
    {
        var largeValues = new byte[1024];
        Random.Shared.NextBytes(largeValues);
        var fields = new List<BinaryDocValuesFormat.Field>
        {
            MakeField("big", 1, [0], [largeValues])
        };
        var data = new BinaryDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(BinaryDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
