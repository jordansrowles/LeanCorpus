namespace Rowles.LeanCorpus.Tests.Core.Document;

/// <summary>
/// Unit tests for <see cref="TextField"/> construction and property values.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Document)]
public sealed class TextFieldTests
{
    [Fact(DisplayName = "TextField: Null Value Throws ArgumentNullException")]
    public void NullValue_ThrowsArgumentNullException()
        => Assert.Throws<ArgumentNullException>(() => new TextField("body", null!));

    [Fact(DisplayName = "TextField: Stored False Sets IsStored False")]
    public void StoredFalse_SetsIsStoredFalse()
    {
        var f = new TextField("body", "hello world", stored: false);
        Assert.False(f.IsStored);
    }

    [Fact(DisplayName = "TextField: FieldType Returns Text")]
    public void FieldType_ReturnsText()
    {
        var f = new TextField("body", "hello world");
        Assert.Equal(FieldType.Text, f.FieldType);
    }

    [Fact(DisplayName = "TextField: IsIndexed Returns True")]
    public void IsIndexed_ReturnsTrue()
    {
        var f = new TextField("body", "hello world");
        Assert.True(f.IsIndexed);
    }

    [Fact(DisplayName = "TextField: Default Constructor Stores Value")]
    public void DefaultConstructor_StoresValue()
    {
        var f = new TextField("body", "the quick brown fox");
        Assert.Equal("body", f.Name);
        Assert.Equal("the quick brown fox", f.Value);
        Assert.True(f.IsStored);
    }
}
