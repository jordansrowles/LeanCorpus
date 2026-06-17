namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class NormsFormatTests
{
    private static NormsFormat.Field MakeField(string name, byte[] norms, float[]? boosts)
        => new() { Name = name, Norms = norms, Boosts = boosts };

    [Fact(DisplayName = "NormsFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(NormsFormat.V1);
    }

    [Fact(DisplayName = "NormsFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var fields = new List<NormsFormat.Field>
        {
            MakeField("f1", [1, 2, 3], null)
        };
        var data = new NormsFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(NormsFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "NormsFormat: Data class Field properties are set correctly")]
    public void DataClass_FieldPropertiesSetCorrectly()
    {
        var field = MakeField("f1", [1, 2, 3], null);
        Assert.Equal("f1", field.Name);
        Assert.NotNull(field.Norms);
        Assert.Equal(3, field.Norms.Count);
        Assert.Null(field.Boosts);
    }

    [Fact(DisplayName = "NormsFormat: Data class Field with boosts")]
    public void DataClass_FieldWithBoosts()
    {
        var field = MakeField("f1", [1, 2, 3], [1.0f, 2.0f, 3.0f]);
        Assert.Equal("f1", field.Name);
        Assert.NotNull(field.Norms);
        Assert.NotNull(field.Boosts);
        Assert.Equal([1.0f, 2.0f, 3.0f], field.Boosts);
    }

    [Fact(DisplayName = "NormsFormat: Data class with empty Fields list")]
    public void DataClass_EmptyFields()
    {
        var data = new NormsFormat.Data { Fields = [] };
        Assert.NotNull(data.Fields);
        Assert.Empty(data.Fields);
    }

    [Fact(DisplayName = "NormsFormat: V1 Encode with multiple fields and mixed boosts")]
    public void V1_Encode_MultipleFieldsMixedBoosts()
    {
        var fields = new List<NormsFormat.Field>
        {
            MakeField("noBoost", [5, 6], null),
            MakeField("withBoost", [7, 8, 9], [0.5f, 1.5f, 2.5f])
        };
        var data = new NormsFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(NormsFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "NormsFormat: Data class with empty Norms array")]
    public void DataClass_EmptyNorms()
    {
        var field = MakeField("x", [], null);
        Assert.Equal("x", field.Name);
        Assert.NotNull(field.Norms);
        Assert.Empty(field.Norms);
        Assert.Null(field.Boosts);
    }
}
