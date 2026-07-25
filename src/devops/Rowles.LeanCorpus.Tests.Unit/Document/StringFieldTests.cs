namespace Rowles.LeanCorpus.Tests.Unit.Document;

/// <summary>
/// Unit tests for <see cref="StringField"/> construction and property values.
/// </summary>
[Trait("Category", "Document")]
[Trait("Category", "UnitTest")]
public sealed class StringFieldTests
{
    [Fact(DisplayName = "StringField: Null Value Throws ArgumentNullException")]
    public void NullValue_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => new StringField("tag", null!));

    [Fact(DisplayName = "StringField: Stored False Sets IsStored False")]
    public void StoredFalse_SetsIsStoredFalse()
    {
        var f = new StringField("tag", "value", stored: false);
        Assert.False(f.IsStored);
    }

    [Fact(DisplayName = "StringField: FieldType Returns String")]
    public void FieldType_ReturnsString()
    {
        var f = new StringField("tag", "value");
        Assert.Equal(FieldType.String, f.FieldType);
    }

    [Fact(DisplayName = "StringField: IsIndexed Returns True")]
    public void IsIndexed_ReturnsTrue()
    {
        var f = new StringField("tag", "value");
        Assert.True(f.IsIndexed);
    }

    [Fact(DisplayName = "StringField: Default Constructor Stores Value")]
    public void DefaultConstructor_StoresValue()
    {
        var f = new StringField("category", "books");
        Assert.Equal("category", f.Name);
        Assert.Equal("books", f.Value);
        Assert.True(f.IsStored);
    }

    [Fact(DisplayName = "StringField: Explicit DocValues Preserves Selection")]
    public void ExplicitDocValues_PreservesSelection()
    {
        var field = new StringField("tag", "value", stored: false, boost: 1.0f,
            StringDocValues.Sorted);

        Assert.Equal(StringDocValues.Sorted, field.DocValues);
        Assert.True(field.StoreDocValues);
    }

    [Fact(DisplayName = "StringField: Legacy DocValues True Preserves All Representations")]
    public void LegacyDocValuesTrue_PreservesAllRepresentations()
    {
        var field = new StringField("tag", "value", stored: false, boost: 1.0f,
            storeDocValues: true);

        Assert.Equal(StringDocValues.Sorted | StringDocValues.SortedSet | StringDocValues.Binary,
            field.DocValues);
    }
}
