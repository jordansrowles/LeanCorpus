namespace Rowles.LeanCorpus.Tests.Unit.Mapping;

/// <summary>
/// Unit tests for <see cref="LeanNumericAttribute"/> construction and property defaults.
/// </summary>
[Trait("Category", "Mapping")]
[Trait("Category", "UnitTest")]
public sealed class LeanNumericAttributeTests
{
    [Fact(DisplayName = "LeanNumeric: Constructor Sets Name")]
    public void Constructor_SetsName()
    {
        var attr = new LeanNumericAttribute("price");
        Assert.Equal("price", attr.Name);
    }

    [Fact(DisplayName = "LeanNumeric: Stored Defaults To True")]
    public void Stored_DefaultsToTrue()
    {
        var attr = new LeanNumericAttribute("count");
        Assert.True(attr.Stored);
    }

    [Fact(DisplayName = "LeanNumeric: Required Defaults To False")]
    public void Required_DefaultsToFalse()
    {
        var attr = new LeanNumericAttribute("qty");
        Assert.False(attr.Required);
    }

    [Fact(DisplayName = "LeanNumeric: Encoding Defaults To None")]
    public void Encoding_DefaultsToNone()
    {
        var attr = new LeanNumericAttribute("val");
        Assert.Equal(LeanNumericEncoding.None, attr.Encoding);
    }

    [Fact(DisplayName = "LeanNumeric: Stored Settable Via Init")]
    public void Stored_SettableViaInit()
    {
        var attr = new LeanNumericAttribute("x") { Stored = false };
        Assert.False(attr.Stored);
    }

    [Fact(DisplayName = "LeanNumeric: Required Settable Via Init")]
    public void Required_SettableViaInit()
    {
        var attr = new LeanNumericAttribute("x") { Required = true };
        Assert.True(attr.Required);
    }

    [Fact(DisplayName = "LeanNumeric: Encoding Settable Via Init")]
    public void Encoding_SettableViaInit()
    {
        var attr = new LeanNumericAttribute("ts") { Encoding = LeanNumericEncoding.UnixMilliseconds };
        Assert.Equal(LeanNumericEncoding.UnixMilliseconds, attr.Encoding);
    }

    [Fact(DisplayName = "LeanNumeric: All Properties Settable Together")]
    public void AllProperties_SettableTogether()
    {
        var attr = new LeanNumericAttribute("amount")
        {
            Stored = false,
            Required = true,
            Encoding = LeanNumericEncoding.DecimalAsString
        };

        Assert.Equal("amount", attr.Name);
        Assert.False(attr.Stored);
        Assert.True(attr.Required);
        Assert.Equal(LeanNumericEncoding.DecimalAsString, attr.Encoding);
    }
}