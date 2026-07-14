namespace Rowles.LeanCorpus.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="LeanVectorAttribute"/> construction and property defaults.
/// </summary>
[Trait("Category", "Mapping")]
[Trait("Category", "UnitTest")]
public sealed class LeanVectorAttributeTests
{
    [Fact(DisplayName = "LeanVector: Constructor Sets Name")]
    public void Constructor_SetsName()
    {
        var attr = new LeanVectorAttribute("embedding");
        Assert.Equal("embedding", attr.Name);
    }

    [Fact(DisplayName = "LeanVector: Dimension Defaults To Zero")]
    public void Dimension_DefaultsToZero()
    {
        var attr = new LeanVectorAttribute("vec");
        Assert.Equal(0, attr.Dimension);
    }

    [Fact(DisplayName = "LeanVector: Required Defaults To False")]
    public void Required_DefaultsToFalse()
    {
        var attr = new LeanVectorAttribute("vec");
        Assert.False(attr.Required);
    }

    [Fact(DisplayName = "LeanVector: Dimension Settable Via Init")]
    public void Dimension_SettableViaInit()
    {
        var attr = new LeanVectorAttribute("vec") { Dimension = 768 };
        Assert.Equal(768, attr.Dimension);
    }

    [Fact(DisplayName = "LeanVector: Required Settable Via Init")]
    public void Required_SettableViaInit()
    {
        var attr = new LeanVectorAttribute("vec") { Required = true };
        Assert.True(attr.Required);
    }

    [Fact(DisplayName = "LeanVector: All Properties Settable Together")]
    public void AllProperties_SettableTogether()
    {
        var attr = new LeanVectorAttribute("emb")
        {
            Dimension = 384,
            Required = true
        };

        Assert.Equal("emb", attr.Name);
        Assert.Equal(384, attr.Dimension);
        Assert.True(attr.Required);
    }
}