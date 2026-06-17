namespace Rowles.LeanCorpus.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="LeanDocumentAttribute"/> construction and property defaults.
/// </summary>
[Trait("Category", "Mapping")]
[Trait("Category", "UnitTest")]
public sealed class LeanDocumentAttributeTests
{
    [Fact(DisplayName = "LeanDocument: Name Defaults To Null")]
    public void Name_DefaultsToNull()
    {
        var attr = new LeanDocumentAttribute();
        Assert.Null(attr.Name);
    }

    [Fact(DisplayName = "LeanDocument: StrictSchema Defaults To True")]
    public void StrictSchema_DefaultsToTrue()
    {
        var attr = new LeanDocumentAttribute();
        Assert.True(attr.StrictSchema);
    }

    [Fact(DisplayName = "LeanDocument: Name Settable Via Init")]
    public void Name_SettableViaInit()
    {
        var attr = new LeanDocumentAttribute { Name = "CustomDocument" };
        Assert.Equal("CustomDocument", attr.Name);
    }

    [Fact(DisplayName = "LeanDocument: StrictSchema Settable Via Init")]
    public void StrictSchema_SettableViaInit()
    {
        var attr = new LeanDocumentAttribute { StrictSchema = false };
        Assert.False(attr.StrictSchema);
    }

    [Fact(DisplayName = "LeanDocument: All Properties Settable Together")]
    public void AllProperties_SettableTogether()
    {
        var attr = new LeanDocumentAttribute
        {
            Name = "MyDoc",
            StrictSchema = false
        };

        Assert.Equal("MyDoc", attr.Name);
        Assert.False(attr.StrictSchema);
    }
}