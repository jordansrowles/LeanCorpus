namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class SortedNumericDocValuesFormatTests
{
    private static SortedNumericDocValuesFormat.Field MakeField(string name, int docCount, int[] docStarts, double[] values)
        => new() { Name = name, DocCount = docCount, DocStarts = docStarts, Values = values };

    [Fact(DisplayName = "SortedNumericDocValuesFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(SortedNumericDocValuesFormat.V1);
    }

    [Fact(DisplayName = "SortedNumericDocValuesFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var fields = new List<SortedNumericDocValuesFormat.Field>
        {
            MakeField("vals", 2, [0, 2], [1.0, 2.0, 3.0])
        };
        var data = new SortedNumericDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(SortedNumericDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "SortedNumericDocValuesFormat: Data class Field properties are set correctly")]
    public void DataClass_FieldPropertiesSetCorrectly()
    {
        var field = MakeField("vals", 2, [0, 2], [1.0, 2.0, 3.0]);
        Assert.Equal("vals", field.Name);
        Assert.Equal(2, field.DocCount);
        Assert.NotNull(field.DocStarts);
        Assert.Equal([0, 2], field.DocStarts);
        Assert.NotNull(field.Values);
        Assert.Equal([1.0, 2.0, 3.0], field.Values);
    }

    [Fact(DisplayName = "SortedNumericDocValuesFormat: Data class with empty Fields list")]
    public void DataClass_EmptyFields()
    {
        var data = new SortedNumericDocValuesFormat.Data { Fields = [] };
        Assert.NotNull(data.Fields);
        Assert.Empty(data.Fields);
    }

    [Fact(DisplayName = "SortedNumericDocValuesFormat: V1 Encode with multiple fields")]
    public void V1_Encode_MultipleFields()
    {
        var fields = new List<SortedNumericDocValuesFormat.Field>
        {
            MakeField("a", 1, [0], [10.0]),
            MakeField("b", 2, [0, 3], [1.0, 2.0, 3.0])
        };
        var data = new SortedNumericDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(SortedNumericDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "SortedNumericDocValuesFormat: Data class Field with minimal data")]
    public void DataClass_FieldMinimalData()
    {
        var field = MakeField("x", 0, [], []);
        Assert.Equal("x", field.Name);
        Assert.Equal(0, field.DocCount);
        Assert.NotNull(field.DocStarts);
        Assert.Empty(field.DocStarts);
        Assert.NotNull(field.Values);
        Assert.Empty(field.Values);
    }

    [Fact(DisplayName = "SortedNumericDocValuesFormat: V1 Encode with single doc")]
    public void V1_Encode_SingleDoc()
    {
        var fields = new List<SortedNumericDocValuesFormat.Field>
        {
            MakeField("single", 1, [0], [99.9])
        };
        var data = new SortedNumericDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(SortedNumericDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
