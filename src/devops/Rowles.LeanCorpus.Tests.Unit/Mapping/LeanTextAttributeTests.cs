namespace Rowles.LeanCorpus.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="LeanTextAttribute"/> construction and property defaults.
/// </summary>
[Trait("Category", "Mapping")]
[Trait("Category", "UnitTest")]
public sealed class LeanTextAttributeTests
{
    [Fact(DisplayName = "LeanText: Constructor Sets Name")]
    public void Constructor_SetsName()
    {
        var attr = new LeanTextAttribute("title");
        Assert.Equal("title", attr.Name);
    }

    [Fact(DisplayName = "LeanText: Stored Defaults To True")]
    public void Stored_DefaultsToTrue()
    {
        var attr = new LeanTextAttribute("body");
        Assert.True(attr.Stored);
    }

    [Fact(DisplayName = "LeanText: Required Defaults To False")]
    public void Required_DefaultsToFalse()
    {
        var attr = new LeanTextAttribute("body");
        Assert.False(attr.Required);
    }

    [Fact(DisplayName = "LeanText: Stored Settable Via Init")]
    public void Stored_SettableViaInit()
    {
        var attr = new LeanTextAttribute("x") { Stored = false };
        Assert.False(attr.Stored);
    }

    [Fact(DisplayName = "LeanText: Required Settable Via Init")]
    public void Required_SettableViaInit()
    {
        var attr = new LeanTextAttribute("x") { Required = true };
        Assert.True(attr.Required);
    }

    [Fact(DisplayName = "LeanText: All Properties Settable Together")]
    public void AllProperties_SettableTogether()
    {
        var attr = new LeanTextAttribute("description")
        {
            Stored = false,
            Required = true
        };

        Assert.Equal("description", attr.Name);
        Assert.False(attr.Stored);
        Assert.True(attr.Required);
    }
}