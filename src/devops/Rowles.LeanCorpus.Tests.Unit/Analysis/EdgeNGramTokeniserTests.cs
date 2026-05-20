using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
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
        var tokens = tok.Tokenise("hello".AsSpan());
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
        var tokens = tok.Tokenise("hi me".AsSpan());
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
        var tokens = tok.Tokenise(string.Empty.AsSpan());
        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the EdgeNGram: Destination Buffer Matches Allocating Path scenario.
    /// </summary>
    [Fact(DisplayName = "EdgeNGram: Destination Buffer Matches Allocating Path")]
    public void EdgeNGram_DestinationBuffer_MatchesAllocatingPath()
    {
        var tok = new EdgeNGramTokeniser(1, 3);
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
    /// Verifies the EdgeNGram span sink emits the same tokens as the legacy list path.
    /// </summary>
    [Fact(DisplayName = "EdgeNGram: Span Sink Matches Legacy Tokens")]
    public void EdgeNGram_SpanSink_MatchesLegacyTokens()
    {
        var tok = new EdgeNGramTokeniser(1, 3);
        var expected = tok.Tokenise("hello world".AsSpan());
        var sink = new CollectingSpanTokenSink();

        tok.Tokenise("hello world".AsSpan(), sink);

        Assert.Equal(expected.Count, sink.Tokens.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Text, sink.Tokens[i].Text);
            Assert.Equal(expected[i].StartOffset, sink.Tokens[i].StartOffset);
            Assert.Equal(expected[i].EndOffset, sink.Tokens[i].EndOffset);
            Assert.Equal(expected[i].PositionIncrement, sink.Tokens[i].PositionIncrement);
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
