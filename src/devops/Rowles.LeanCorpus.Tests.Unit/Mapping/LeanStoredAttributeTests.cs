namespace Rowles.LeanCorpus.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="LeanStoredAttribute"/> construction and property defaults.
/// </summary>
[Trait("Category", "Mapping")]
[Trait("Category", "UnitTest")]
public sealed class LeanStoredAttributeTests
{
    [Fact(DisplayName = "LeanStored: Constructor Sets Name")]
    public void Constructor_SetsName()
    {
        var attr = new LeanStoredAttribute("blob");
        Assert.Equal("blob", attr.Name);
    }

    [Fact(DisplayName = "LeanStored: Required Defaults To False")]
    public void Required_DefaultsToFalse()
    {
        var attr = new LeanStoredAttribute("data");
        Assert.False(attr.Required);
    }

    [Fact(DisplayName = "LeanStored: Required Settable Via Init")]
    public void Required_SettableViaInit()
    {
        var attr = new LeanStoredAttribute("data") { Required = true };
        Assert.True(attr.Required);
    }

    [Fact(DisplayName = "LeanStored: Name And Required Settable Together")]
    public void NameAndRequired_SettableTogether()
    {
        var attr = new LeanStoredAttribute("payload") { Required = true };

        Assert.Equal("payload", attr.Name);
        Assert.True(attr.Required);
    }
}