namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class NumericDocValuesFormatTests
{
    private static NumericDocValuesFormat.Field MakeField(string name, double[] values, int[]? presence)
        => new() { Name = name, Values = values, Presence = presence };

    [Fact(DisplayName = "NumericDocValuesFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(NumericDocValuesFormat.V1);
    }

    [Fact(DisplayName = "NumericDocValuesFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var fields = new List<NumericDocValuesFormat.Field>
        {
            MakeField("price", [10.5, 20.0], null)
        };
        var data = new NumericDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(NumericDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "NumericDocValuesFormat: Data class Field properties are set correctly without Presence")]
    public void DataClass_FieldPropertiesSetCorrectly()
    {
        var field = MakeField("price", [10.5, 20.0], null);
        Assert.Equal("price", field.Name);
        Assert.NotNull(field.Values);
        Assert.Equal(2, field.Values.Length);
        Assert.Equal([10.5, 20.0], field.Values);
        Assert.Null(field.Presence);
    }

    [Fact(DisplayName = "NumericDocValuesFormat: Data class Field with Presence set")]
    public void DataClass_FieldWithPresence()
    {
        var field = MakeField("score", [5.0, 10.0, 15.0], [0, 1, 2]);
        Assert.Equal("score", field.Name);
        Assert.NotNull(field.Values);
        Assert.NotNull(field.Presence);
        Assert.Equal([0, 1, 2], field.Presence);
    }

    [Fact(DisplayName = "NumericDocValuesFormat: Data class with empty Fields list")]
    public void DataClass_EmptyFields()
    {
        var data = new NumericDocValuesFormat.Data { Fields = [] };
        Assert.NotNull(data.Fields);
        Assert.Empty(data.Fields);
    }

    [Fact(DisplayName = "NumericDocValuesFormat: V1 Encode with multiple fields and mixed presence")]
    public void V1_Encode_MultipleFieldsMixedPresence()
    {
        var fields = new List<NumericDocValuesFormat.Field>
        {
            MakeField("price", [10.5, 20.0], null),
            MakeField("score", [1.0, 2.0, 3.0], [0, 1, 1]),
            MakeField("count", [100.0], null)
        };
        var data = new NumericDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(NumericDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "NumericDocValuesFormat: Data class Field with null Presence")]
    public void DataClass_FieldNullPresence()
    {
        var field = MakeField("x", [], []);
        Assert.Equal("x", field.Name);
        Assert.NotNull(field.Values);
        Assert.Empty(field.Values);
        Assert.NotNull(field.Presence);
        Assert.Empty(field.Presence);
    }
}
