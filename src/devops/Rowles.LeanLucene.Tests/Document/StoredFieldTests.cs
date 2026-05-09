namespace Rowles.LeanLucene.Tests.Document;

/// <summary>
/// Unit tests for <see cref="StoredField"/> construction and property values.
/// </summary>
[Trait("Category", "Document")]
[Trait("Category", "UnitTest")]
public sealed class StoredFieldTests
{
    // ── string overload ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the String Constructor: Sets Name And Value scenario.
    /// </summary>
    [Fact(DisplayName = "String Constructor: Sets Name And Value")]
    public void StringConstructor_SetsNameAndValue()
    {
        var f = new StoredField("author", "Jordan");
        Assert.Equal("author", f.Name);
        Assert.Equal("Jordan", f.Value);
    }

    /// <summary>
    /// Verifies the String Constructor: Throws On Null Value scenario.
    /// </summary>
    [Fact(DisplayName = "String Constructor: Throws On Null Value")]
    public void StringConstructor_ThrowsOnNullValue()
    {
        Assert.Throws<ArgumentNullException>(() => new StoredField("author", (string)null!));
    }

    /// <summary>
    /// Verifies the String Constructor: Is Stored Not Indexed scenario.
    /// </summary>
    [Fact(DisplayName = "String Constructor: Is Stored Not Indexed")]
    public void StringConstructor_IsStoredNotIndexed()
    {
        var f = new StoredField("author", "Jordan");
        Assert.True(f.IsStored);
        Assert.False(f.IsIndexed);
        Assert.Equal(FieldType.Stored, f.FieldType);
    }

    // ── int overload ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Int Constructor: Stores Formatted Value scenario.
    /// </summary>
    [Fact(DisplayName = "Int Constructor: Stores Formatted Value")]
    public void IntConstructor_StoresFormattedValue()
    {
        var f = new StoredField("count", 42);
        Assert.Equal("count", f.Name);
        Assert.Equal("42", f.Value);
        Assert.True(f.IsStored);
        Assert.False(f.IsIndexed);
    }

    /// <summary>
    /// Verifies the Int Constructor: Stores Negative Value scenario.
    /// </summary>
    [Fact(DisplayName = "Int Constructor: Stores Negative Value")]
    public void IntConstructor_StoresNegativeValue()
    {
        var f = new StoredField("delta", -7);
        Assert.Equal("-7", f.Value);
    }

    /// <summary>
    /// Verifies the Int Constructor: Stores Zero scenario.
    /// </summary>
    [Fact(DisplayName = "Int Constructor: Stores Zero")]
    public void IntConstructor_StoresZero()
    {
        var f = new StoredField("zero", 0);
        Assert.Equal("0", f.Value);
    }

    // ── long overload ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Long Constructor: Stores Formatted Value scenario.
    /// </summary>
    [Fact(DisplayName = "Long Constructor: Stores Formatted Value")]
    public void LongConstructor_StoresFormattedValue()
    {
        var f = new StoredField("timestamp", 9_876_543_210L);
        Assert.Equal("timestamp", f.Name);
        Assert.Equal("9876543210", f.Value);
        Assert.True(f.IsStored);
        Assert.False(f.IsIndexed);
    }

    /// <summary>
    /// Verifies the Long Constructor: Stores Large Negative Value scenario.
    /// </summary>
    [Fact(DisplayName = "Long Constructor: Stores Large Negative Value")]
    public void LongConstructor_StoresLargeNegativeValue()
    {
        var f = new StoredField("ts", long.MinValue);
        Assert.Equal(long.MinValue.ToString(), f.Value);
    }

    // ── double overload ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Double Constructor: Stores Formatted Value scenario.
    /// </summary>
    [Fact(DisplayName = "Double Constructor: Stores Formatted Value")]
    public void DoubleConstructor_StoresFormattedValue()
    {
        var f = new StoredField("price", 3.14);
        Assert.Equal("price", f.Name);
        Assert.True(f.IsStored);
        Assert.False(f.IsIndexed);
        // Just verify it round-trips to a non-empty string
        Assert.False(string.IsNullOrEmpty(f.Value));
    }

    /// <summary>
    /// Verifies the Double Constructor: Stores Zero scenario.
    /// </summary>
    [Fact(DisplayName = "Double Constructor: Stores Zero")]
    public void DoubleConstructor_StoresZero()
    {
        var f = new StoredField("val", 0.0);
        Assert.Equal("0", f.Value);
    }
}
