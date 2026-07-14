namespace Rowles.LeanCorpus.Tests.Unit.Search.Highlighting;

/// <summary>
/// Unit tests for the <see cref="ReadOnlySet"/> helper class.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class ReadOnlySetTests
{
    [Fact(DisplayName = "ReadOnlySet: Empty is non-null")]
    public void Empty_IsNonNull()
    {
        Assert.NotNull(ReadOnlySet.Empty);
    }

    [Fact(DisplayName = "ReadOnlySet: Empty is an empty set")]
    public void Empty_IsEmptySet()
    {
        Assert.Empty(ReadOnlySet.Empty);
    }

    [Fact(DisplayName = "ReadOnlySet: Empty does not contain any string")]
    public void Empty_DoesNotContainAnyString()
    {
        Assert.False(ReadOnlySet.Empty.Contains("test"));
        Assert.False(ReadOnlySet.Empty.Contains(""));
        Assert.False(ReadOnlySet.Empty.Contains("anything"));
    }

    [Fact(DisplayName = "ReadOnlySet: Empty returns the same instance every time")]
    public void Empty_ReturnsSameInstance()
    {
        Assert.Same(ReadOnlySet.Empty, ReadOnlySet.Empty);
    }
}
