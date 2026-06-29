namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class SortedSetDocValuesFormatTests
{
    private static SortedSetDocValuesFormat.Field MakeField(string name, int docCount, string[] terms, int[] docStarts, int[] ordinals)
        => new() { Name = name, DocCount = docCount, Terms = terms, DocStarts = docStarts, Ordinals = ordinals };

    [Fact(DisplayName = "SortedSetDocValuesFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(SortedSetDocValuesFormat.V1);
    }

    [Fact(DisplayName = "SortedSetDocValuesFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var fields = new List<SortedSetDocValuesFormat.Field>
        {
            MakeField("tags", 2, ["red", "blue"], [0, 2], [0, 1, 0])
        };
        var data = new SortedSetDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(SortedSetDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "SortedSetDocValuesFormat: Data class Field properties are set correctly")]
    public void DataClass_FieldPropertiesSetCorrectly()
    {
        var field = MakeField("tags", 2, ["red", "blue"], [0, 2], [0, 1, 0]);
        Assert.Equal("tags", field.Name);
        Assert.Equal(2, field.DocCount);
        Assert.NotNull(field.Terms);
        Assert.Equal(["red", "blue"], field.Terms);
        Assert.NotNull(field.DocStarts);
        Assert.Equal([0, 2], field.DocStarts);
        Assert.NotNull(field.Ordinals);
        Assert.Equal([0, 1, 0], field.Ordinals);
    }

    [Fact(DisplayName = "SortedSetDocValuesFormat: Data class with empty Fields list")]
    public void DataClass_EmptyFields()
    {
        var data = new SortedSetDocValuesFormat.Data { Fields = [] };
        Assert.NotNull(data.Fields);
        Assert.Empty(data.Fields);
    }

    [Fact(DisplayName = "SortedSetDocValuesFormat: V1 Encode with multiple fields")]
    public void V1_Encode_MultipleFields()
    {
        var fields = new List<SortedSetDocValuesFormat.Field>
        {
            MakeField("tags", 1, ["x", "y"], [0], [0, 1]),
            MakeField("categories", 2, ["a", "b", "c"], [0, 2], [0, 2, 1])
        };
        var data = new SortedSetDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(SortedSetDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "SortedSetDocValuesFormat: Data class Field with minimal data")]
    public void DataClass_FieldMinimalData()
    {
        var field = MakeField("x", 0, [], [], []);
        Assert.Equal("x", field.Name);
        Assert.Equal(0, field.DocCount);
        Assert.NotNull(field.Terms);
        Assert.Empty(field.Terms);
        Assert.NotNull(field.DocStarts);
        Assert.Empty(field.DocStarts);
        Assert.NotNull(field.Ordinals);
        Assert.Empty(field.Ordinals);
    }

    [Fact(DisplayName = "SortedSetDocValuesFormat: V1 Encode with single term")]
    public void V1_Encode_SingleTerm()
    {
        var fields = new List<SortedSetDocValuesFormat.Field>
        {
            MakeField("flag", 1, ["yes"], [0], [0, 0, 0])
        };
        var data = new SortedSetDocValuesFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(SortedSetDocValuesFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }
}
