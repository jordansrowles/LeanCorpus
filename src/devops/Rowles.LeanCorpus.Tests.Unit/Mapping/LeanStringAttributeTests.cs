namespace Rowles.LeanCorpus.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="LeanStringAttribute"/> construction and property defaults.
/// </summary>
[Trait("Category", "Mapping")]
[Trait("Category", "UnitTest")]
public sealed class LeanStringAttributeTests
{
    [Fact(DisplayName = "LeanString: Constructor Sets Name")]
    public void Constructor_SetsName()
    {
        var attr = new LeanStringAttribute("id");
        Assert.Equal("id", attr.Name);
    }

    [Fact(DisplayName = "LeanString: Stored Defaults To True")]
    public void Stored_DefaultsToTrue()
    {
        var attr = new LeanStringAttribute("key");
        Assert.True(attr.Stored);
    }

    [Fact(DisplayName = "LeanString: Required Defaults To False")]
    public void Required_DefaultsToFalse()
    {
        var attr = new LeanStringAttribute("key");
        Assert.False(attr.Required);
    }

    [Fact(DisplayName = "LeanString: Stored Settable Via Init")]
    public void Stored_SettableViaInit()
    {
        var attr = new LeanStringAttribute("x") { Stored = false };
        Assert.False(attr.Stored);
    }

    [Fact(DisplayName = "LeanString: Required Settable Via Init")]
    public void Required_SettableViaInit()
    {
        var attr = new LeanStringAttribute("x") { Required = true };
        Assert.True(attr.Required);
    }

    [Fact(DisplayName = "LeanString: All Properties Settable Together")]
    public void AllProperties_SettableTogether()
    {
        var attr = new LeanStringAttribute("category")
        {
            Stored = false,
            Required = true
        };

        Assert.Equal("category", attr.Name);
        Assert.False(attr.Stored);
        Assert.True(attr.Required);
    }
}