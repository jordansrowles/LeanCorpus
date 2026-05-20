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

    /// <summary>
    /// Verifies the NGram span sink emits the same tokens as the legacy list path.
    /// </summary>
    [Fact(DisplayName = "NGram: Span Sink Matches Legacy Tokens")]
    public void NGram_SpanSink_MatchesLegacyTokens()
    {
        var tok = new NGramTokeniser(2, 3);
        var expected = tok.Tokenise("abcd".AsSpan());
        var sink = new CollectingSpanTokenSink();

        tok.Tokenise("abcd".AsSpan(), sink);

        AssertEqualTokens(expected, sink.Tokens);
    }

    // ----- splitOnWhitespace = true -----

    /// <summary>
    /// Verifies that NGram with whitespace splitting does not produce cross-word grams.
    /// </summary>
    [Fact(DisplayName = "NGram WordSplit: No Cross-Word Grams")]
    public void NGram_WordSplit_NoCrossWordGrams()
    {
        var tok = new NGramTokeniser(2, 3, splitOnWhitespace: true);
        var tokens = tok.Tokenise("hello world".AsSpan());
        var texts = tokens.Select(t => t.Text).ToList();

        // grams that span the space must not appear
        Assert.DoesNotContain("o w", texts);
        Assert.DoesNotContain("lo ", texts);
        Assert.DoesNotContain(" wo", texts);
    }

    /// <summary>
    /// Verifies that NGram with whitespace splitting produces grams from each word independently.
    /// </summary>
    [Fact(DisplayName = "NGram WordSplit: Grams From Each Word")]
    public void NGram_WordSplit_GramsFromEachWord()
    {
        var tok = new NGramTokeniser(2, 2, splitOnWhitespace: true);
        var tokens = tok.Tokenise("ab cd".AsSpan());
        var texts = tokens.Select(t => t.Text).ToList();
        Assert.Contains("ab", texts);
        Assert.Contains("cd", texts);
        Assert.Equal(2, texts.Count);
    }

    /// <summary>
    /// Verifies that NGram with whitespace splitting reports correct absolute offsets.
    /// </summary>
    [Fact(DisplayName = "NGram WordSplit: Absolute Offsets Correct")]
    public void NGram_WordSplit_AbsoluteOffsetsCorrect()
    {
        var tok = new NGramTokeniser(2, 2, splitOnWhitespace: true);
        // "ab cd" — "ab" at 0-2, "cd" at 3-5
        var tokens = tok.Tokenise("ab cd".AsSpan());
        var ab = tokens.Single(t => t.Text == "ab");
        var cd = tokens.Single(t => t.Text == "cd");
        Assert.Equal(0, ab.StartOffset);
        Assert.Equal(2, ab.EndOffset);
        Assert.Equal(3, cd.StartOffset);
        Assert.Equal(5, cd.EndOffset);
    }

    /// <summary>
    /// Verifies that NGram with whitespace splitting handles leading and trailing whitespace.
    /// </summary>
    [Fact(DisplayName = "NGram WordSplit: Leading And Trailing Whitespace")]
    public void NGram_WordSplit_LeadingAndTrailingWhitespace()
    {
        var tok = new NGramTokeniser(2, 2, splitOnWhitespace: true);
        var tokens = tok.Tokenise("  ab  ".AsSpan());
        var texts = tokens.Select(t => t.Text).ToList();
        Assert.Equal(["ab"], texts);
    }

    /// <summary>
    /// Verifies that NGram with whitespace splitting handles consecutive whitespace characters.
    /// </summary>
    [Fact(DisplayName = "NGram WordSplit: Consecutive Whitespace")]
    public void NGram_WordSplit_ConsecutiveWhitespace()
    {
        var tok = new NGramTokeniser(2, 2, splitOnWhitespace: true);
        var tokens = tok.Tokenise("ab\t\r\ncd".AsSpan());
        var texts = tokens.Select(t => t.Text).ToList();
        Assert.Contains("ab", texts);
        Assert.Contains("cd", texts);
        Assert.Equal(2, texts.Count);
    }

    /// <summary>
    /// Verifies that NGram with whitespace splitting skips words shorter than MinGram.
    /// </summary>
    [Fact(DisplayName = "NGram WordSplit: Word Shorter Than MinGram Is Skipped")]
    public void NGram_WordSplit_WordShorterThanMinGram_IsSkipped()
    {
        var tok = new NGramTokeniser(3, 4, splitOnWhitespace: true);
        // "a" is shorter than minGram=3; "abcd" should produce grams
        var tokens = tok.Tokenise("a abcd".AsSpan());
        var texts = tokens.Select(t => t.Text).ToList();
        Assert.DoesNotContain("a", texts);
        Assert.Contains("abc", texts);
    }

    /// <summary>
    /// Verifies the buffer overload matches the allocating path when splitOnWhitespace is true.
    /// </summary>
    [Fact(DisplayName = "NGram WordSplit: Buffer Matches Allocating Path")]
    public void NGram_WordSplit_BufferMatchesAllocatingPath()
    {
        var tok = new NGramTokeniser(2, 3, splitOnWhitespace: true);
        var expected = tok.Tokenise("hello world".AsSpan());
        var actual = new List<Token> { new("stale", 0, 5) };

        tok.Tokenise("hello world".AsSpan(), actual);

        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Text, actual[i].Text);
            Assert.Equal(expected[i].StartOffset, actual[i].StartOffset);
            Assert.Equal(expected[i].EndOffset, actual[i].EndOffset);
        }
    }

    /// <summary>
    /// Verifies the whitespace-split NGram span sink matches the legacy list path.
    /// </summary>
    [Fact(DisplayName = "NGram WordSplit: Span Sink Matches Legacy Tokens")]
    public void NGram_WordSplit_SpanSinkMatchesLegacyTokens()
    {
        var tok = new NGramTokeniser(2, 3, splitOnWhitespace: true);
        var expected = tok.Tokenise("hello world".AsSpan());
        var sink = new CollectingSpanTokenSink();

        tok.Tokenise("hello world".AsSpan(), sink);

        AssertEqualTokens(expected, sink.Tokens);
    }

    private static void AssertEqualTokens(List<Token> expected, List<Token> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Text, actual[i].Text);
            Assert.Equal(expected[i].StartOffset, actual[i].StartOffset);
            Assert.Equal(expected[i].EndOffset, actual[i].EndOffset);
            Assert.Equal(expected[i].PositionIncrement, actual[i].PositionIncrement);
        }
    }

    private sealed class CollectingSpanTokenSink : ISpanTokenSink
    {
        public List<Token> Tokens { get; } = [];

        public void Add(
            ReadOnlySpan<char> text,
            int startOffset,
            int endOffset,
            string type = Token.DefaultType,
            int positionIncrement = 1,
            byte[]? payload = null)
        {
            Tokens.Add(new Token(text.ToString(), startOffset, endOffset, type, positionIncrement, payload));
        }
    }
}
