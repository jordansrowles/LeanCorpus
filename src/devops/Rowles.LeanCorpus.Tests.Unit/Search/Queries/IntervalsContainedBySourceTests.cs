namespace Rowles.LeanCorpus.Tests.Unit.Search.Queries;

/// <summary>
/// Unit tests for <see cref="IntervalsContainedBySource"/>.
/// </summary>
[Trait("Category", "Search")]
[Trait("Category", "UnitTest")]
public sealed class IntervalsContainedBySourceTests
{
    private static readonly IntervalsSource Inner = new IntervalsTermSource("field", "inner");
    private static readonly IntervalsSource Outer = new IntervalsTermSource("field", "outer");

    [Fact(DisplayName = "IntervalsContainedBySource: Constructor sets Inner and Outer")]
    public void Constructor_SetsInnerAndOuter()
    {
        var source = new IntervalsContainedBySource(Inner, Outer);
        Assert.Same(Inner, source.Inner);
        Assert.Same(Outer, source.Outer);
    }

    [Fact(DisplayName = "IntervalsContainedBySource: Field delegates to Inner")]
    public void Field_DelegatesToInner()
    {
        var source = new IntervalsContainedBySource(Inner, Outer);
        Assert.Equal(Inner.Field, source.Field);
    }

    [Fact(DisplayName = "IntervalsContainedBySource: Constructor throws on null inner")]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new IntervalsContainedBySource(null!, Outer));
    }

    [Fact(DisplayName = "IntervalsContainedBySource: Constructor throws on null outer")]
    public void Constructor_NullOuter_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new IntervalsContainedBySource(Inner, null!));
    }

    [Fact(DisplayName = "IntervalsContainedBySource: Constructor throws when fields differ")]
    public void Constructor_DifferentFields_Throws()
    {
        var otherField = new IntervalsTermSource("other", "term");
        var ex = Assert.Throws<ArgumentException>(() =>
            new IntervalsContainedBySource(Inner, otherField));
        Assert.Contains("same field", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "IntervalsContainedBySource: Equal sources are equal")]
    public void EqualSources_AreEqual()
    {
        var a = new IntervalsContainedBySource(Inner, Outer);
        var b = new IntervalsContainedBySource(
            new IntervalsTermSource("field", "inner"),
            new IntervalsTermSource("field", "outer"));
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact(DisplayName = "IntervalsContainedBySource: Different inner are not equal")]
    public void DifferentInner_NotEqual()
    {
        var a = new IntervalsContainedBySource(Inner, Outer);
        var b = new IntervalsContainedBySource(
            new IntervalsTermSource("field", "other"), Outer);
        Assert.NotEqual(a, b);
    }

    [Fact(DisplayName = "IntervalsContainedBySource: Different outer are not equal")]
    public void DifferentOuter_NotEqual()
    {
        var a = new IntervalsContainedBySource(Inner, Outer);
        var b = new IntervalsContainedBySource(
            Inner, new IntervalsTermSource("field", "other"));
        Assert.NotEqual(a, b);
    }
}
