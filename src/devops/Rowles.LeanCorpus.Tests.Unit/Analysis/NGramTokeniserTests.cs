using Rowles.LeanCorpus.Analysis;
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
        var tokens = Collect(tok, "abc");
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
        var tokens = Collect(tok, "abcd");
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
        var tokens = Collect(tok, "");
        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the NGram: Input Shorter Than Min Gram Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "NGram: Input Shorter Than Min Gram Returns Empty")]
    public void NGram_InputShorterThanMinGram_ReturnsEmpty()
    {
        var tok = new NGramTokeniser(3, 4);
        var tokens = Collect(tok, "ab");
        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the NGram: Offset Values Are Correct scenario.
    /// </summary>
    [Fact(DisplayName = "NGram: Offset Values Are Correct")]
    public void NGram_OffsetValues_AreCorrect()
    {
        var tok = new NGramTokeniser(2, 2);
        var tokens = Collect(tok, "xyz");
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(2, tokens[0].EndOffset);
        Assert.Equal(1, tokens[1].StartOffset);
        Assert.Equal(3, tokens[1].EndOffset);
    }

    /// <summary>
    /// Verifies the NGram enumerator terminates on a single word with no trailing whitespace.
    /// </summary>
    [Fact(DisplayName = "NGram: Enumerator Terminates On Single Word")]
    public void NGram_EnumeratorTerminatesOnSingleWord()
    {
        var tok = new NGramTokeniser(2, 3);
        var tokens = CollectEnum(tok, "abc");
        Assert.Equal(3, tokens.Count); // ab, abc, bc
    }

    /// <summary>
    /// Verifies the NGram span sink and enumerator produce identical tokens.
    /// </summary>
    [Fact(DisplayName = "NGram: Span Sink And Enumerator Match")]
    public void NGram_SpanSinkAndEnumeratorMatch()
    {
        var tok = new NGramTokeniser(2, 3);
        var fromSink = Collect(tok, "abcd");
        var fromEnum = CollectEnum(tok, "abcd");

        Assert.Equal(fromSink.Count, fromEnum.Count);
        for (int i = 0; i < fromSink.Count; i++)
        {
            Assert.Equal(fromSink[i].Text, fromEnum[i].Text);
            Assert.Equal(fromSink[i].StartOffset, fromEnum[i].StartOffset);
            Assert.Equal(fromSink[i].EndOffset, fromEnum[i].EndOffset);
        }
    }

    // ----- splitOnWhitespace = true -----

    /// <summary>
    /// Verifies that NGram with whitespace splitting does not produce cross-word grams.
    /// </summary>
    [Fact(DisplayName = "NGram WordSplit: No Cross-Word Grams")]
    public void NGram_WordSplit_NoCrossWordGrams()
    {
        var tok = new NGramTokeniser(2, 3, splitOnWhitespace: true);
        var tokens = Collect(tok, "hello world");
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
        var tokens = Collect(tok, "ab cd");
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
        var tokens = Collect(tok, "ab cd");
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
        var tokens = Collect(tok, "  ab  ");
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
        var tokens = Collect(tok, "ab\t\r\ncd");
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
        var tokens = Collect(tok, "a abcd");
        var texts = tokens.Select(t => t.Text).ToList();
        Assert.DoesNotContain("a", texts);
        Assert.Contains("abc", texts);
    }

    private static List<Token> Collect(NGramTokeniser tok, string input)
    {
        var sink = new CollectingSpanTokenSink();
        tok.Tokenise(input.AsSpan(), sink);
        return sink.Tokens;
    }

    private static List<Token> CollectEnum(NGramTokeniser tok, string input)
    {
        var tokens = new List<Token>();
        foreach (var st in tok.EnumerateTokens(input.AsSpan()))
            tokens.Add(new Token(st.Text.ToString(), st.StartOffset, st.EndOffset));
        return tokens;
    }

    private sealed class CollectingSpanTokenSink : ISpanTokenSink
    {
        public readonly List<Token> Tokens = new List<Token>();

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
