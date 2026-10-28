using Rowles.LeanCorpus.Search;

namespace Rowles.LeanCorpus.Tests.Core.Search;

/// <summary>
/// Unit tests for <see cref="BooleanClause"/> equality and hash code.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class BooleanClauseTests
{
    private static TermQuery Q(string term) => new("body", term);

    [Fact(DisplayName = "BooleanClause.Equals(object): Null Returns False")]
    public void Equals_Object_Null_ReturnsFalse()
        => Assert.False(new BooleanClause(Q("a"), Occur.Must).Equals((object?)null));

    [Fact(DisplayName = "BooleanClause.Equals(object): Wrong Type Returns False")]
    public void Equals_Object_WrongType_ReturnsFalse()
        => Assert.False(new BooleanClause(Q("a"), Occur.Must).Equals("not a clause"));

    [Fact(DisplayName = "BooleanClause.Equals: Null Other Returns False")]
    public void Equals_NullOther_ReturnsFalse()
        => Assert.False(new BooleanClause(Q("a"), Occur.Must).Equals((BooleanClause?)null));

    [Fact(DisplayName = "BooleanClause.Equals: Different Query Returns False")]
    public void Equals_DifferentQuery_ReturnsFalse()
    {
        var a = new BooleanClause(Q("a"), Occur.Must);
        var b = new BooleanClause(Q("b"), Occur.Must);
        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "BooleanClause.Equals: Different Occur Returns False")]
    public void Equals_DifferentOccur_ReturnsFalse()
    {
        var a = new BooleanClause(Q("a"), Occur.Must);
        var b = new BooleanClause(Q("a"), Occur.Should);
        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "BooleanClause.Equals: Same Values Returns True")]
    public void Equals_SameValues_ReturnsTrue()
    {
        var a = new BooleanClause(Q("a"), Occur.Must);
        var b = new BooleanClause(Q("a"), Occur.Must);
        Assert.Equal(a, b);
    }

    [Fact(DisplayName = "BooleanClause.GetHashCode: Equal Instances Have Same Hash")]
    public void GetHashCode_EqualInstances_SameHash()
    {
        var a = new BooleanClause(Q("a"), Occur.Must);
        var b = new BooleanClause(Q("a"), Occur.Must);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }
}
