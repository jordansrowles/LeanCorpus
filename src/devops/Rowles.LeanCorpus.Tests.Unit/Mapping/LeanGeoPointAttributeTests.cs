namespace Rowles.LeanCorpus.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="LeanGeoPointAttribute"/> construction and property defaults.
/// </summary>
[Trait("Category", "Mapping")]
[Trait("Category", "UnitTest")]
public sealed class LeanGeoPointAttributeTests
{
    [Fact(DisplayName = "LeanGeoPoint: Constructor Sets Name")]
    public void Constructor_SetsName()
    {
        var attr = new LeanGeoPointAttribute("location");
        Assert.Equal("location", attr.Name);
    }

    [Fact(DisplayName = "LeanGeoPoint: Required Defaults To False")]
    public void Required_DefaultsToFalse()
    {
        var attr = new LeanGeoPointAttribute("loc");
        Assert.False(attr.Required);
    }

    [Fact(DisplayName = "LeanGeoPoint: Required Settable Via Init")]
    public void Required_SettableViaInit()
    {
        var attr = new LeanGeoPointAttribute("loc") { Required = true };
        Assert.True(attr.Required);
    }

    [Fact(DisplayName = "LeanGeoPoint: Name And Required Settable Together")]
    public void NameAndRequired_SettableTogether()
    {
        var attr = new LeanGeoPointAttribute("geo") { Required = true };

        Assert.Equal("geo", attr.Name);
        Assert.True(attr.Required);
    }
}