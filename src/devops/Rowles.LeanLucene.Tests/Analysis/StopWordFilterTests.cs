using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Contains unit tests for Stop Word Filter.
/// </summary>
[Trait("Category", "Analysis")]
public class StopWordFilterTests
{
    private readonly StopWordFilter _filter = new();

    /// <summary>
    /// Verifies the Apply: All Stop Words Returns Empty List scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: All Stop Words Returns Empty List")]
    public void Apply_AllStopWords_ReturnsEmptyList()
    {
        var tokens = new List<Token>
        {
            new("the", 0, 3),
            new("is", 4, 6),
            new("a", 7, 8),
        };

        _filter.Apply(tokens);

        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the Apply: No Stop Words Returns All Tokens scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: No Stop Words Returns All Tokens")]
    public void Apply_NoStopWords_ReturnsAllTokens()
    {
        var tokens = new List<Token>
        {
            new("quick", 0, 5),
            new("fox", 6, 9),
        };

        _filter.Apply(tokens);

        Assert.Equal(2, tokens.Count);
    }

    /// <summary>
    /// Verifies the Apply: Mixed Tokens Removes Only Stop Words scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Mixed Tokens Removes Only Stop Words")]
    public void Apply_MixedTokens_RemovesOnlyStopWords()
    {
        var tokens = new List<Token>
        {
            new("the", 0, 3),
            new("quick", 4, 9),
            new("brown", 10, 15),
            new("fox", 16, 19),
        };

        _filter.Apply(tokens);

        Assert.Equal(3, tokens.Count);
        Assert.Equal("quick", tokens[0].Text);
        Assert.Equal("brown", tokens[1].Text);
        Assert.Equal("fox", tokens[2].Text);
    }
}
