namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class SortedDocValuesFormatTests
{
    private static SortedDocValuesFormat.Field MakeField(string name, string[] terms, int[] ordinals, int[]? presence)
        => new() { Name = name, Terms = terms, Ordinals = ordinals, Presence = presence };

    [Fact(DisplayName = "SortedDocValuesFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(SortedDocValuesFormat.V1);
    }

    [Fact(DisplayName = "SortedDocValuesFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var fields = new List<SortedDocValuesFormat.Field>
        {
            MakeField("cat", ["a", "b"], [0, 1, 0], null)
        };
        var data = new SortedDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(SortedDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "SortedDocValuesFormat: Data class Field properties are set correctly without Presence")]
    public void DataClass_FieldPropertiesSetCorrectly()
    {
        var field = MakeField("cat", ["a", "b"], [0, 1, 0], null);
        Assert.Equal("cat", field.Name);
        Assert.NotNull(field.Terms);
        Assert.Equal(["a", "b"], field.Terms);
        Assert.NotNull(field.Ordinals);
        Assert.Equal([0, 1, 0], field.Ordinals);
        Assert.Null(field.Presence);
    }

    [Fact(DisplayName = "SortedDocValuesFormat: Data class Field with Presence set")]
    public void DataClass_FieldWithPresence()
    {
        var field = MakeField("cat", ["a", "b"], [0, 1, 0], [0, 1, 2]);
        Assert.Equal("cat", field.Name);
        Assert.NotNull(field.Presence);
        Assert.Equal([0, 1, 2], field.Presence);
    }

    [Fact(DisplayName = "SortedDocValuesFormat: Data class with empty Fields list")]
    public void DataClass_EmptyFields()
    {
        var data = new SortedDocValuesFormat.Data { Fields = [] };
        Assert.NotNull(data.Fields);
        Assert.Empty(data.Fields);
    }

    [Fact(DisplayName = "SortedDocValuesFormat: V1 Encode with multiple fields and mixed presence")]
    public void V1_Encode_MultipleFieldsMixedPresence()
    {
        var fields = new List<SortedDocValuesFormat.Field>
        {
            MakeField("color", ["red", "blue"], [0, 1, 1, 0], null),
            MakeField("size", ["S", "M", "L"], [0, 2, 1], [0, 1, 2])
        };
        var data = new SortedDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(SortedDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "SortedDocValuesFormat: Data class Field with empty Terms and empty Presence")]
    public void DataClass_FieldEmptyTerms()
    {
        var field = MakeField("x", [], [], null);
        Assert.Equal("x", field.Name);
        Assert.NotNull(field.Terms);
        Assert.Empty(field.Terms);
        Assert.NotNull(field.Ordinals);
        Assert.Empty(field.Ordinals);
        Assert.Null(field.Presence);
    }
}
