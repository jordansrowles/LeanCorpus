namespace Rowles.LeanCorpus.Tests.Core.Search.Queries;
[Category(TestCategory.Unit)]
[Area(TestArea.Search)]
public sealed class IntervalsSourceTests
{
    [Fact(DisplayName = "IntervalsQuery: stores source and includes boost in equality")]
    public void IntervalsQuery_StoresSourceAndIncludesBoostInEquality()
    {
        var source = new IntervalsTermSource("body", "alpha");
        var query = new IntervalsQuery(source) { Boost = 2.0f };
        var equivalent = new IntervalsQuery(new IntervalsTermSource("body", "alpha")) { Boost = 2.0f };

        Assert.Same(source, query.Source);
        Assert.Equal("body", query.Field);
        Assert.Equal(query, equivalent);
        Assert.Equal(query.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(query, new IntervalsQuery(source));
        Assert.False(query.Equals((object?)null));
        Assert.False(query.Equals(new TermQuery("body", "alpha")));
        Assert.Throws<ArgumentNullException>(() => new IntervalsQuery(null!));
    }

    [Fact(DisplayName = "IntervalsSource: shared field accepts empty and matching lists")]
    public void IntervalsSource_SharedFieldAcceptsEmptyAndMatchingLists()
    {
        Assert.Equal(string.Empty, IntervalsSource.GetSharedField([]));

        var alpha = new IntervalsTermSource("body", "alpha");
        var beta = new IntervalsTermSource("body", "beta");

        Assert.Equal("body", IntervalsSource.GetSharedField([alpha, beta]));
        Assert.Throws<ArgumentException>(() => IntervalsSource.GetSharedField(
            [alpha, new IntervalsTermSource("title", "alpha")]));
    }

    [Fact(DisplayName = "IntervalsTermSource: stores term, cache, and equality values")]
    public void IntervalsTermSource_StoresTermCacheAndEqualityValues()
    {
        var source = new IntervalsTermSource("body", "alpha");
        source.CachedQualifiedTerm = "body\0alpha";

        var equivalent = new IntervalsTermSource("body", "alpha");
        var differentTerm = new IntervalsTermSource("body", "beta");
        var differentField = new IntervalsTermSource("title", "alpha");

        Assert.Equal("body", source.Field);
        Assert.Equal("alpha", source.Term);
        Assert.Equal("body\0alpha", source.CachedQualifiedTerm);
        Assert.Equal(source, equivalent);
        Assert.True(source.Equals((IntervalsSource)equivalent));
        Assert.False(source.Equals((IntervalsSource?)null));
        Assert.Equal(source.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(source, differentTerm);
        Assert.NotEqual(source, differentField);
        Assert.False(source.Equals("not an interval source"));
    }

    [Fact(DisplayName = "IntervalsPhraseSource: copies ordered terms and compares them")]
    public void IntervalsPhraseSource_CopiesOrderedTermsAndComparesThem()
    {
        var terms = new[] { "alpha", "beta" };
        var source = new IntervalsPhraseSource("body", terms);
        terms[0] = "changed";

        var equivalent = new IntervalsPhraseSource("body", "alpha", "beta");
        var reversed = new IntervalsPhraseSource("body", "beta", "alpha");

        Assert.Equal("body", source.Field);
        Assert.Equal(["alpha", "beta"], source.Terms);
        Assert.Equal(source, equivalent);
        Assert.Equal(source.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(source, reversed);
        Assert.False(source.Equals("not an interval source"));
    }

    [Fact(DisplayName = "IntervalsCompositeSources: expose children and constraints")]
    public void IntervalsCompositeSources_ExposeChildrenAndConstraints()
    {
        var alpha = new IntervalsTermSource("body", "alpha");
        var beta = new IntervalsTermSource("body", "beta");

        var or = new IntervalsOrSource(alpha, beta);
        var ordered = new IntervalsOrderedSource(2, alpha, beta);
        var unordered = new IntervalsUnorderedSource(3, alpha, beta);

        Assert.Equal("body", or.Field);
        Assert.Equal([alpha, beta], or.Sources);
        Assert.Equal("body", ordered.Field);
        Assert.Equal(2, ordered.MaxGaps);
        Assert.Equal([alpha, beta], ordered.Sources);
        Assert.Equal("body", unordered.Field);
        Assert.Equal(3, unordered.MaxGaps);
        Assert.Equal([alpha, beta], unordered.Sources);

        Assert.Equal(or, new IntervalsOrSource(
            new IntervalsTermSource("body", "alpha"),
            new IntervalsTermSource("body", "beta")));
        Assert.NotEqual(or, new IntervalsOrSource(beta, alpha));
        Assert.Equal(ordered, new IntervalsOrderedSource(2,
            new IntervalsTermSource("body", "alpha"),
            new IntervalsTermSource("body", "beta")));
        Assert.NotEqual(ordered, new IntervalsOrderedSource(1, alpha, beta));
        Assert.Equal(unordered, new IntervalsUnorderedSource(3,
            new IntervalsTermSource("body", "alpha"),
            new IntervalsTermSource("body", "beta")));
        Assert.NotEqual(unordered, new IntervalsUnorderedSource(2, alpha, beta));
        Assert.False(or.Equals("not an interval source"));
        Assert.False(ordered.Equals("not an interval source"));
        Assert.False(unordered.Equals("not an interval source"));
    }

    [Fact(DisplayName = "IntervalsWrapperSources: expose children and compare them")]
    public void IntervalsWrapperSources_ExposeChildrenAndCompareThem()
    {
        var alpha = new IntervalsTermSource("body", "alpha");
        var beta = new IntervalsTermSource("body", "beta");

        var containing = new IntervalsContainingSource(alpha, beta);
        var containedBy = new IntervalsContainedBySource(alpha, beta);
        var notContaining = new IntervalsNotContainingSource(alpha, beta);

        Assert.Same(alpha, containing.Outer);
        Assert.Same(beta, containing.Inner);
        Assert.Equal("body", containing.Field);
        Assert.Same(alpha, containedBy.Inner);
        Assert.Same(beta, containedBy.Outer);
        Assert.Equal("body", containedBy.Field);
        Assert.Same(alpha, notContaining.Outer);
        Assert.Same(beta, notContaining.Inner);
        Assert.Equal("body", notContaining.Field);

        Assert.Equal(containing, new IntervalsContainingSource(
            new IntervalsTermSource("body", "alpha"),
            new IntervalsTermSource("body", "beta")));
        Assert.NotEqual(containing, new IntervalsContainingSource(beta, alpha));
        Assert.Equal(containedBy, new IntervalsContainedBySource(
            new IntervalsTermSource("body", "alpha"),
            new IntervalsTermSource("body", "beta")));
        Assert.Equal(notContaining, new IntervalsNotContainingSource(
            new IntervalsTermSource("body", "alpha"),
            new IntervalsTermSource("body", "beta")));
        Assert.NotEqual(notContaining, new IntervalsNotContainingSource(beta, alpha));
        Assert.False(containing.Equals("not an interval source"));
        Assert.False(notContaining.Equals("not an interval source"));
    }

    [Fact(DisplayName = "IntervalsSources: reject invalid inputs and mismatched fields")]
    public void IntervalsSources_RejectInvalidInputsAndMismatchedFields()
    {
        var body = new IntervalsTermSource("body", "alpha");
        var title = new IntervalsTermSource("title", "alpha");

        Assert.Throws<ArgumentException>(() => new IntervalsTermSource("", "alpha"));
        Assert.Throws<ArgumentException>(() => new IntervalsTermSource("body", ""));
        Assert.Throws<ArgumentException>(() => new IntervalsPhraseSource("", "alpha"));
        Assert.Throws<ArgumentNullException>(() => new IntervalsPhraseSource("body", (string[]?)null!));
        Assert.Throws<ArgumentException>(() => new IntervalsPhraseSource("body", ""));
        Assert.Throws<ArgumentException>(() => new IntervalsPhraseSource("body", "alpha", " "));
        Assert.Throws<ArgumentNullException>(() => new IntervalsOrSource((IntervalsSource[]?)null!));
        Assert.Throws<ArgumentException>(() => new IntervalsOrSource());
        Assert.Throws<ArgumentNullException>(() => new IntervalsOrderedSource(0, (IntervalsSource[]?)null!));
        Assert.Throws<ArgumentException>(() => new IntervalsOrderedSource(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntervalsOrderedSource(-1, body));
        Assert.Throws<ArgumentNullException>(() => new IntervalsUnorderedSource(0, (IntervalsSource[]?)null!));
        Assert.Throws<ArgumentException>(() => new IntervalsUnorderedSource(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IntervalsUnorderedSource(-1, body));

        Assert.Throws<ArgumentException>(() => new IntervalsOrSource(body, title));
        Assert.Throws<ArgumentException>(() => new IntervalsOrderedSource(0, body, title));
        Assert.Throws<ArgumentException>(() => new IntervalsUnorderedSource(0, body, title));
        Assert.Throws<ArgumentException>(() => new IntervalsContainingSource(body, title));
        Assert.Throws<ArgumentException>(() => new IntervalsContainedBySource(body, title));
        Assert.Throws<ArgumentException>(() => new IntervalsNotContainingSource(body, title));
        Assert.Throws<ArgumentNullException>(() => new IntervalsContainingSource(null!, body));
        Assert.Throws<ArgumentNullException>(() => new IntervalsContainingSource(body, null!));
        Assert.Throws<ArgumentNullException>(() => new IntervalsNotContainingSource(null!, body));
        Assert.Throws<ArgumentNullException>(() => new IntervalsNotContainingSource(body, null!));
    }
}
