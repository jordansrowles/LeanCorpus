using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Contains unit tests for Porter Stemmer Filter.
/// </summary>
[Trait("Category", "Analysis")]
public sealed class PorterStemmerFilterTests
{
    private readonly PorterStemmerFilter _filter = new();

    private string Stem(string word)
    {
        var matSink = new MaterialisingTokenSink();
        _filter.Apply(word.AsSpan(), 0, word.Length, Token.DefaultType, 1, null, matSink);
        return matSink.Tokens[0].Text;
    }

    /// <summary>
    /// Verifies the Stem: Classic Porter Test Vectors scenario.
    /// </summary>
    /// <param name="input">The input value for the test case.</param>
    /// <param name="expected">The expected value for the test case.</param>
    [Theory(DisplayName = "Stem: Classic Porter Test Vectors")]
    [InlineData("caresses", "caress")]
    [InlineData("ponies", "poni")]
    [InlineData("cats", "cat")]
    [InlineData("feed", "feed")]
    [InlineData("agreed", "agre")]
    [InlineData("plastered", "plaster")]
    [InlineData("motoring", "motor")]
    [InlineData("sing", "sing")]
    public void Stem_ClassicPorterTestVectors(string input, string expected)
    {
        Assert.Equal(expected, Stem(input));
    }

    /// <summary>
    /// Verifies the Apply: Empty List No Changes scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Empty List No Changes")]
    public void Apply_EmptyList_NoChanges()
    {
        var tokens = new List<Token>();
        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) _filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);
        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the Apply: Short Words Unchanged scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Short Words Unchanged")]
    public void Apply_ShortWords_Unchanged()
    {
        // Words with 2 or fewer chars should not be stemmed
        Assert.Equal("a", Stem("a"));
        Assert.Equal("an", Stem("an"));
    }

    /// <summary>
    /// Verifies the Apply: Multiple Tokens All Stemmed scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Multiple Tokens All Stemmed")]
    public void Apply_MultipleTokens_AllStemmed()
    {
        var tokens = new List<Token>
        {
            new("running", 0, 7),
            new("cats", 8, 12),
            new("happily", 13, 20)
        };
        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) _filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);

        Assert.Equal("run", tokens[0].Text);
        Assert.Equal("cat", tokens[1].Text);
        Assert.Equal("happili", tokens[2].Text);
    }

    /// <summary>
    /// Verifies the Apply: Already Stemmed Token Unchanged scenario.
    /// </summary>
    [Fact(DisplayName = "Apply: Already Stemmed Token Unchanged")]
    public void Apply_AlreadyStemmed_TokenUnchanged()
    {
        var token = new Token("run", 0, 3);
        var tokens = new List<Token> { token };
        var matSink = new MaterialisingTokenSink();
        foreach (var t in tokens) _filter.Apply(t.Text.AsSpan(), t.StartOffset, t.EndOffset, t.Type, t.PositionIncrement, t.Payload, matSink);
        tokens.Clear();
        tokens.AddRange(matSink.Tokens);
        // Token should still be "run" — no double-stemming
        Assert.Equal("run", tokens[0].Text);
    }
}
