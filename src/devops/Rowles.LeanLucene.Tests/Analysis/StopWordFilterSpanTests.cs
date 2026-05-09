using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Contains unit tests for Stop Word Filter Span.
/// </summary>
[Trait("Category", "Analysis")]
public class StopWordFilterSpanTests
{
    private readonly StopWordFilter _filter = new();

    /// <summary>
    /// Verifies the Is Stop Word: Span Matches String Overload scenario.
    /// </summary>
    /// <param name="word">The word value for the test case.</param>
    /// <param name="expected">The expected value for the test case.</param>
    [Theory(DisplayName = "Is Stop Word: Span Matches String Overload")]
    [InlineData("the", true)]
    [InlineData("and", true)]
    [InlineData("is", true)]
    [InlineData("THE", true)]
    [InlineData("hello", false)]
    [InlineData("world", false)]
    public void IsStopWord_Span_MatchesStringOverload(string word, bool expected)
    {
        Assert.Equal(expected, _filter.IsStopWord(word.AsSpan()));
        Assert.Equal(expected, _filter.IsStopWord(word));
    }

    /// <summary>
    /// Verifies the Is Stop Word: Empty Span Returns False scenario.
    /// </summary>
    [Fact(DisplayName = "Is Stop Word: Empty Span Returns False")]
    public void IsStopWord_EmptySpan_ReturnsFalse()
    {
        Assert.False(_filter.IsStopWord(ReadOnlySpan<char>.Empty));
    }
}
