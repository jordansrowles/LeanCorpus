namespace Rowles.LeanCorpus.Tests.Unit.Codecs.CodecKit.Formats;

[Trait("Category", "CodecKit")]
public sealed class FieldLengthsFormatTests
{
    private static FieldLengthsFormat.FieldEntry MakeEntry(string name, int[] lengths)
        => new() { Name = name, Lengths = lengths };

    [Fact(DisplayName = "FieldLengthsFormat: V1 codec is non-null")]
    public void V1_CodecIsNonNull()
    {
        Assert.NotNull(FieldLengthsFormat.V1);
    }

    [Fact(DisplayName = "FieldLengthsFormat: V1 Encode produces non-empty bytes")]
    public void V1_Encode_ProducesNonEmptyBytes()
    {
        var fields = new List<FieldLengthsFormat.FieldEntry>
        {
            MakeEntry("body", [5, 10, 3])
        };
        var data = new FieldLengthsFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(FieldLengthsFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "FieldLengthsFormat: Data class FieldEntry properties are set correctly")]
    public void DataClass_FieldEntryPropertiesSetCorrectly()
    {
        var entry = MakeEntry("body", [5, 10, 3]);
        Assert.Equal("body", entry.Name);
        Assert.NotNull(entry.Lengths);
        Assert.Equal(3, entry.Lengths.Length);
        Assert.Equal(3, entry.DocCount);
    }

    [Fact(DisplayName = "FieldLengthsFormat: Data class with empty Fields list")]
    public void DataClass_EmptyFields()
    {
        var data = new FieldLengthsFormat.Data { Fields = [] };
        Assert.NotNull(data.Fields);
        Assert.Empty(data.Fields);
    }

    [Fact(DisplayName = "FieldLengthsFormat: V1 Encode with multiple fields")]
    public void V1_Encode_MultipleFields()
    {
        var fields = new List<FieldLengthsFormat.FieldEntry>
        {
            MakeEntry("title", [12]),
            MakeEntry("body", [5, 10, 3]),
            MakeEntry("abstract", [7, 8])
        };
        var data = new FieldLengthsFormat.Data { Fields = fields };
        byte[] encoded = Codec.EncodeToArray(FieldLengthsFormat.V1, data);
        Assert.NotNull(encoded);
        Assert.True(encoded.Length > 0);
    }

    [Fact(DisplayName = "FieldLengthsFormat: Data class with empty Lengths array")]
    public void DataClass_EmptyLengths()
    {
        var entry = MakeEntry("x", []);
        Assert.Equal("x", entry.Name);
        Assert.NotNull(entry.Lengths);
        Assert.Empty(entry.Lengths);
        Assert.Equal(0, entry.DocCount);
    }
}
