using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Contains unit tests for Tokeniser.
/// </summary>
[Trait("Category", "Analysis")]
public class TokeniserTests
{
    private readonly Tokeniser _tokeniser = new();

    /// <summary>
    /// Verifies the Tokenise: Sentence With Words Returns Tokens With Correct Offsets scenario.
    /// </summary>
    [Fact(DisplayName = "Tokenise: Sentence With Words Returns Tokens With Correct Offsets")]
    public void Tokenise_SentenceWithWords_ReturnsTokensWithCorrectOffsets()
    {
        var matSink = new MaterialisingTokenSink();
        _tokeniser.Tokenise("The quick brown fox", matSink);
        var tokens = matSink.Tokens;

        Assert.Equal(4, tokens.Count);

        Assert.Equal("The", tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(3, tokens[0].EndOffset);

        Assert.Equal("quick", tokens[1].Text);
        Assert.Equal(4, tokens[1].StartOffset);
        Assert.Equal(9, tokens[1].EndOffset);

        Assert.Equal("brown", tokens[2].Text);
        Assert.Equal(10, tokens[2].StartOffset);
        Assert.Equal(15, tokens[2].EndOffset);

        Assert.Equal("fox", tokens[3].Text);
        Assert.Equal(16, tokens[3].StartOffset);
        Assert.Equal(19, tokens[3].EndOffset);
    }

    /// <summary>
    /// Verifies the Tokenise: Input With Punctuation Excludes Punctuation From Tokens scenario.
    /// </summary>
    [Fact(DisplayName = "Tokenise: Input With Punctuation Excludes Punctuation From Tokens")]
    public void Tokenise_InputWithPunctuation_ExcludesPunctuationFromTokens()
    {
        var matSink = new MaterialisingTokenSink();
        _tokeniser.Tokenise("hello, world!", matSink);
        var tokens = matSink.Tokens;

        Assert.Equal(2, tokens.Count);
        Assert.Equal("hello", tokens[0].Text);
        Assert.Equal("world", tokens[1].Text);
    }

    /// <summary>
    /// Verifies the Tokenise: Empty Input Returns Empty List scenario.
    /// </summary>
    [Fact(DisplayName = "Tokenise: Empty Input Returns Empty List")]
    public void Tokenise_EmptyInput_ReturnsEmptyList()
    {
        var matSink = new MaterialisingTokenSink();
        _tokeniser.Tokenise(ReadOnlySpan<char>.Empty, matSink);
        var tokens = matSink.Tokens;

        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the Tokenise: Only Whitespace And Punctuation Returns Empty List scenario.
    /// </summary>
    [Fact(DisplayName = "Tokenise: Only Whitespace And Punctuation Returns Empty List")]
    public void Tokenise_OnlyWhitespaceAndPunctuation_ReturnsEmptyList()
    {
        var matSink = new MaterialisingTokenSink();
        _tokeniser.Tokenise("  , . ! ", matSink);
        var tokens = matSink.Tokens;

        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the Tokenise: Single Word Returns Single Token scenario.
    /// </summary>
    [Fact(DisplayName = "Tokenise: Single Word Returns Single Token")]
    public void Tokenise_SingleWord_ReturnsSingleToken()
    {
        var matSink = new MaterialisingTokenSink();
        _tokeniser.Tokenise("hello", matSink);
        var tokens = matSink.Tokens;

        Assert.Single(tokens);
        Assert.Equal("hello", tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(5, tokens[0].EndOffset);
    }
}
