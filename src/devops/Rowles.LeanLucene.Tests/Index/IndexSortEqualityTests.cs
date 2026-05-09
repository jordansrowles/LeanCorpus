namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Unit tests for <see cref="IndexSort"/> equality, hash code, and string representation.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "UnitTest")]
public sealed class IndexSortEqualityTests
{
    // ── Equals ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Equals: Same Fields Returns True scenario.
    /// </summary>
    [Fact(DisplayName = "Equals: Same Fields Returns True")]
    public void Equals_SameFields_ReturnsTrue()
    {
        var a = new IndexSort(SortField.Numeric("price"));
        var b = new IndexSort(SortField.Numeric("price"));

        Assert.True(a.Equals(b));
        Assert.True(a.Equals((object)b));
    }

    /// <summary>
    /// Verifies the Equals: Null Returns False scenario.
    /// </summary>
    [Fact(DisplayName = "Equals: Null Returns False")]
    public void Equals_Null_ReturnsFalse()
    {
        var a = new IndexSort(SortField.Numeric("price"));
        Assert.False(a.Equals(null));
    }

    /// <summary>
    /// Verifies the Equals: Different Field Name Returns False scenario.
    /// </summary>
    [Fact(DisplayName = "Equals: Different Field Name Returns False")]
    public void Equals_DifferentFieldName_ReturnsFalse()
    {
        var a = new IndexSort(SortField.Numeric("price"));
        var b = new IndexSort(SortField.Numeric("quantity"));

        Assert.False(a.Equals(b));
    }

    /// <summary>
    /// Verifies the Equals: Different Sort Type Returns False scenario.
    /// </summary>
    [Fact(DisplayName = "Equals: Different Sort Type Returns False")]
    public void Equals_DifferentSortType_ReturnsFalse()
    {
        var a = new IndexSort(SortField.Numeric("field"));
        var b = new IndexSort(SortField.String("field"));

        Assert.False(a.Equals(b));
    }

    /// <summary>
    /// Verifies the Equals: Different Descending Flag Returns False scenario.
    /// </summary>
    [Fact(DisplayName = "Equals: Different Descending Flag Returns False")]
    public void Equals_DifferentDescendingFlag_ReturnsFalse()
    {
        var a = new IndexSort(SortField.Numeric("price", descending: false));
        var b = new IndexSort(SortField.Numeric("price", descending: true));

        Assert.False(a.Equals(b));
    }

    /// <summary>
    /// Verifies the Equals: Different Field Count Returns False scenario.
    /// </summary>
    [Fact(DisplayName = "Equals: Different Field Count Returns False")]
    public void Equals_DifferentFieldCount_ReturnsFalse()
    {
        var a = new IndexSort(SortField.Numeric("price"));
        var b = new IndexSort(SortField.Numeric("price"), SortField.String("name"));

        Assert.False(a.Equals(b));
    }

    /// <summary>
    /// Verifies the Equals: Multi Field Same Returns True scenario.
    /// </summary>
    [Fact(DisplayName = "Equals: Multi Field Same Returns True")]
    public void Equals_MultiFieldSame_ReturnsTrue()
    {
        var a = new IndexSort(SortField.Numeric("price"), SortField.String("name"));
        var b = new IndexSort(SortField.Numeric("price"), SortField.String("name"));

        Assert.True(a.Equals(b));
    }

    // ── GetHashCode ──────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the Get Hash Code: Equal Sorts Have Equal Hash Codes scenario.
    /// </summary>
    [Fact(DisplayName = "GetHashCode: Equal Sorts Have Equal Hash Codes")]
    public void GetHashCode_EqualSorts_HaveEqualHashCodes()
    {
        var a = new IndexSort(SortField.Numeric("price"));
        var b = new IndexSort(SortField.Numeric("price"));

        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    /// <summary>
    /// Verifies the Get Hash Code: Different Sorts Typically Have Different Hash Codes scenario.
    /// </summary>
    [Fact(DisplayName = "GetHashCode: Different Sorts Typically Have Different Hash Codes")]
    public void GetHashCode_DifferentSorts_TypicallyHaveDifferentHashCodes()
    {
        var a = new IndexSort(SortField.Numeric("price"));
        var b = new IndexSort(SortField.String("name"));

        // Not a hard guarantee but should hold in practice
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    // ── ToString ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the To String: Contains Field Name scenario.
    /// </summary>
    [Fact(DisplayName = "ToString: Contains Field Name")]
    public void ToString_ContainsFieldName()
    {
        var sort = new IndexSort(SortField.Numeric("price"));
        Assert.Contains("price", sort.ToString());
    }

    /// <summary>
    /// Verifies the To String: Contains Descending Marker When Descending scenario.
    /// </summary>
    [Fact(DisplayName = "ToString: Contains Descending Marker When Descending")]
    public void ToString_ContainsDescendingMarker_WhenDescending()
    {
        var sort = new IndexSort(SortField.Numeric("price", descending: true));
        Assert.Contains("DESC", sort.ToString());
    }

    /// <summary>
    /// Verifies the To String: Does Not Contain Descending Marker When Ascending scenario.
    /// </summary>
    [Fact(DisplayName = "ToString: Does Not Contain Descending Marker When Ascending")]
    public void ToString_DoesNotContainDescendingMarker_WhenAscending()
    {
        var sort = new IndexSort(SortField.Numeric("price", descending: false));
        Assert.DoesNotContain("DESC", sort.ToString());
    }

    /// <summary>
    /// Verifies the To String: Multi Field Joined With Comma scenario.
    /// </summary>
    [Fact(DisplayName = "ToString: Multi Field Joined With Comma")]
    public void ToString_MultiField_JoinedWithComma()
    {
        var sort = new IndexSort(SortField.Numeric("price"), SortField.String("name"));
        var str = sort.ToString();
        Assert.Contains("price", str);
        Assert.Contains("name", str);
        Assert.Contains(",", str);
    }
}
