using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis;

/// <summary>
/// Contains unit tests for EdgeNGram Tokeniser.
/// </summary>
[Trait("Category", "Analysis")]
public sealed class EdgeNGramTokeniserTests
{
    /// <summary>
    /// Verifies the EdgeNGram: Emits Edge N Grams scenario.
    /// </summary>
    [Fact(DisplayName = "EdgeNGram: Emits Edge N Grams")]
    public void EdgeNGram_EmitsEdgeNGrams()
    {
        var tok = new EdgeNGramTokeniser(1, 3);
        var tokens = Collect(tok, "hello");
        var texts = tokens.Select(t => t.Text).ToList();
        Assert.Contains("h", texts);
        Assert.Contains("he", texts);
        Assert.Contains("hel", texts);
        // Should NOT contain "hell" (max=3) or "ello"
        Assert.DoesNotContain("hell", texts);
        Assert.DoesNotContain("ello", texts);
    }

    /// <summary>
    /// Verifies the EdgeNGram: Multiple Words Each Word Has Edge N Grams scenario.
    /// </summary>
    [Fact(DisplayName = "EdgeNGram: Multiple Words Each Word Has Edge N Grams")]
    public void EdgeNGram_MultipleWords_EachWordHasEdgeNGrams()
    {
        var tok = new EdgeNGramTokeniser(1, 2);
        var tokens = Collect(tok, "hi me");
        var texts = tokens.Select(t => t.Text).ToList();
        Assert.Contains("h", texts);
        Assert.Contains("hi", texts);
        Assert.Contains("m", texts);
        Assert.Contains("me", texts);
    }

    /// <summary>
    /// Verifies the EdgeNGram: Empty Input Returns Empty scenario.
    /// </summary>
    [Fact(DisplayName = "EdgeNGram: Empty Input Returns Empty")]
    public void EdgeNGram_EmptyInput_ReturnsEmpty()
    {
        var tok = new EdgeNGramTokeniser(1, 3);
        var tokens = Collect(tok, "");
        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies that the span sink and enumerator produce identical tokens.
    /// </summary>
    [Fact(DisplayName = "EdgeNGram: Span Sink And Enumerator Match")]
    public void EdgeNGram_SpanSinkAndEnumeratorMatch()
    {
        var tok = new EdgeNGramTokeniser(1, 3);
        var fromSink = Collect(tok, "hello world");
        var fromEnum = CollectEnum(tok, "hello world");

        Assert.Equal(fromSink.Count, fromEnum.Count);
        for (int i = 0; i < fromSink.Count; i++)
        {
            Assert.Equal(fromSink[i].Text, fromEnum[i].Text);
            Assert.Equal(fromSink[i].StartOffset, fromEnum[i].StartOffset);
            Assert.Equal(fromSink[i].EndOffset, fromEnum[i].EndOffset);
        }
    }

    /// <summary>
    /// Verifies that tokens from short words are skipped when shorter than MinGram.
    /// </summary>
    [Fact(DisplayName = "EdgeNGram: Short Words Skipped Below MinGram")]
    public void EdgeNGram_ShortWordsSkippedBelowMinGram()
    {
        var tok = new EdgeNGramTokeniser(3, 5);
        var tokens = Collect(tok, "a ab abcdef");
        var texts = tokens.Select(t => t.Text).ToList();
        // "a" and "ab" are too short; "abcdef" yields "abc", "abcd", "abcde"
        Assert.DoesNotContain("a", texts);
        Assert.DoesNotContain("ab", texts);
        Assert.Contains("abc", texts);
        Assert.Contains("abcd", texts);
        Assert.Contains("abcde", texts);
    }

    private static List<Token> Collect(EdgeNGramTokeniser tok, string input)
    {
        var sink = new CollectingSpanTokenSink();
        tok.Tokenise(input.AsSpan(), sink);
        return sink.Tokens;
    }

    private static List<Token> CollectEnum(EdgeNGramTokeniser tok, string input)
    {
        var tokens = new List<Token>();
        foreach (var st in tok.EnumerateTokens(input.AsSpan()))
            tokens.Add(new Token(st.Text.ToString(), st.StartOffset, st.EndOffset));
        return tokens;
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
