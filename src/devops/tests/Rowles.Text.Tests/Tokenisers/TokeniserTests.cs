using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.Text.Tests;

/// <summary>
/// Contains unit tests for Tokeniser.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Tokenisers)]
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

    /// <summary>
    /// Verifies that a supplementary-plane letter remains part of a word and offsets count UTF-16 code units.
    /// </summary>
    [Fact(DisplayName = "Tokenise: Supplementary Letter Between BMP Letters Is One Token")]
    public void Tokenise_SupplementaryLetterBetweenBmpLetters_IsOneTokenWithUtf16Offsets()
    {
        const string input = "A\U00010400B";
        var matSink = new MaterialisingTokenSink();

        _tokeniser.Tokenise(input, matSink);

        var token = Assert.Single(matSink.Tokens);
        Assert.Equal(input, token.Text);
        Assert.Equal(0, token.StartOffset);
        Assert.Equal(input.Length, token.EndOffset);
        Assert.Equal(Token.DefaultType, token.Type);
    }

    /// <summary>
    /// Verifies that a supplementary-plane decimal digit is tokenised and classified as a number.
    /// </summary>
    [Fact(DisplayName = "Tokenise: Supplementary Decimal Digit Is A Number")]
    public void Tokenise_SupplementaryDecimalDigit_IsAOneTokenNumber()
    {
        const string input = "\U0001D7D8";
        var matSink = new MaterialisingTokenSink();

        _tokeniser.Tokenise(input, matSink);

        var token = Assert.Single(matSink.Tokens);
        Assert.Equal(input, token.Text);
        Assert.Equal(0, token.StartOffset);
        Assert.Equal(input.Length, token.EndOffset);
        Assert.Equal("number", token.Type);
    }

    /// <summary>
    /// Verifies that supplementary letters at both token boundaries are included.
    /// </summary>
    [Fact(DisplayName = "Tokenise: Leading And Trailing Supplementary Letters Stay In The Token")]
    public void Tokenise_LeadingAndTrailingSupplementaryLetters_StaysOneToken()
    {
        const string input = "\U00010400word\U00010400";
        var matSink = new MaterialisingTokenSink();

        _tokeniser.Tokenise(input, matSink);

        var token = Assert.Single(matSink.Tokens);
        Assert.Equal(input, token.Text);
        Assert.Equal(0, token.StartOffset);
        Assert.Equal(input.Length, token.EndOffset);
    }

    /// <summary>
    /// Verifies the invalid UTF-16 policy: unpaired surrogates are delimiters and are not emitted.
    /// </summary>
    [Fact(DisplayName = "Tokenise: Unpaired Surrogates Separate Valid Text")]
    public void Tokenise_UnpairedHighAndLowSurrogates_AreDelimiters()
    {
        const string input = "\uD800a\uDC00";
        var matSink = new MaterialisingTokenSink();

        _tokeniser.Tokenise(input, matSink);

        var token = Assert.Single(matSink.Tokens);
        Assert.Equal("a", token.Text);
        Assert.Equal(1, token.StartOffset);
        Assert.Equal(2, token.EndOffset);
    }
}
