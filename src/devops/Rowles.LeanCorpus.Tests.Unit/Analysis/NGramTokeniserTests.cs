using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Contains unit tests for NGram Tokeniser.
/// </summary>
[Trait("Category", "Analysis")]
public sealed class NGramTokeniserTests
{
    /// <summary>
    /// Verifies the NGram: Single Char Tokens scenario.
    /// </summary>
    [Fact(DisplayName = "NGram: Single Char Tokens")]
    public void NGram_SingleCharTokens()
    {
        var tok = new NGramTokeniser(1, 1);
        var tokens = tok.Tokenise("abc".AsSpan());
        Assert.Equal(3, tokens.Count);
        Assert.Equal("a", tokens[0].Text);
        Assert.Equal("b", tokens[1].Text);
        Assert.Equal("c", tokens[2].Text);
    }

    /// <summary>
    /// Verifies the NGram: Bigrams And Trigrams scenario.
    /// </summary>
    [Fact(DisplayName = "NGram: Bigrams And Trigrams")]
    public void NGram_BigramsAndTrigrams()
    {
        var tok = new NGramTokeniser(2, 3);
        var tokens = tok.Tokenise("abcd".AsSpan());
        // Expected: ab, abc, bc, bcd, cd
        var texts = tokens.Select(t => t.Text).ToList();
        Assert.Contains("ab", texts);
        Assert.Contains("abc", texts);
        Assert.Contains("bc", texts);
        Assert.Contains("bcd", texts);
        Assert.Contains("cd", texts);
    }

    /// <summary>
    /// Verifies the NGram: Empty Input Returns Empty List scenario.
    /// </summary>
    [Fact(DisplayName = "NGram: Empty Input Returns Empty List")]
    public void NGram_EmptyInput_ReturnsEmptyList()
    {
        var tok = new NGramTokeniser(1, 2);
        var tokens = tok.Tokenise(string.Empty.AsSpan());
        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the NGram: Input Shorter Than Min Gram Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "NGram: Input Shorter Than Min Gram Returns Empty")]
    public void NGram_InputShorterThanMinGram_ReturnsEmpty()
    {
        var tok = new NGramTokeniser(3, 4);
        var tokens = tok.Tokenise("ab".AsSpan());
        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the NGram: Offset Values Are Correct scenario.
    /// </summary>
    [Fact(DisplayName = "NGram: Offset Values Are Correct")]
    public void NGram_OffsetValues_AreCorrect()
    {
        var tok = new NGramTokeniser(2, 2);
        var tokens = tok.Tokenise("xyz".AsSpan());
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(2, tokens[0].EndOffset);
        Assert.Equal(1, tokens[1].StartOffset);
        Assert.Equal(3, tokens[1].EndOffset);
    }

    /// <summary>
    /// Verifies the NGram: Destination Buffer Matches Allocating Path scenario.
    /// </summary>
    [Fact(DisplayName = "NGram: Destination Buffer Matches Allocating Path")]
    public void NGram_DestinationBuffer_MatchesAllocatingPath()
    {
        var tok = new NGramTokeniser(2, 3);
        var expected = tok.Tokenise("abcd".AsSpan());
        var actual = new List<Token> { new("stale", 0, 5) };

        tok.Tokenise("abcd".AsSpan(), actual);

        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Text, actual[i].Text);
            Assert.Equal(expected[i].StartOffset, actual[i].StartOffset);
            Assert.Equal(expected[i].EndOffset, actual[i].EndOffset);
        }
    }
}
