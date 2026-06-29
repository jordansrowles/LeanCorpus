using System.Text.RegularExpressions;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;

namespace Rowles.LeanCorpus.Tests.Unit.Analysis.Tokenisers;

[Trait("Category", "Analysis")]
public class PatternTokeniserTests
{
    [Fact(DisplayName = "PatternTokeniser: splits on comma pattern")]
    public void SplitsOnCommaPattern()
    {
        var tokeniser = new PatternTokeniser(@"[^,]+");
        var sink = new MaterialisingTokenSink();

        tokeniser.Tokenise("alpha,beta,gamma", sink);

        var tokens = sink.Tokens;
        Assert.Equal(3, tokens.Count);
        Assert.Equal("alpha", tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(5, tokens[0].EndOffset);
        Assert.Equal("beta", tokens[1].Text);
        Assert.Equal(6, tokens[1].StartOffset);
        Assert.Equal(10, tokens[1].EndOffset);
        Assert.Equal("gamma", tokens[2].Text);
        Assert.Equal(11, tokens[2].StartOffset);
        Assert.Equal(16, tokens[2].EndOffset);
    }

    [Fact(DisplayName = "PatternTokeniser: splits on whitespace pattern")]
    public void SplitsOnWhitespacePattern()
    {
        var tokeniser = new PatternTokeniser(@"\S+");
        var sink = new MaterialisingTokenSink();

        tokeniser.Tokenise("the quick  brown", sink);

        var tokens = sink.Tokens;
        Assert.Equal(3, tokens.Count);
        Assert.Equal("the", tokens[0].Text);
        Assert.Equal("quick", tokens[1].Text);
        Assert.Equal("brown", tokens[2].Text);
    }

    [Fact(DisplayName = "PatternTokeniser: empty input produces no tokens")]
    public void EmptyInputProducesNoTokens()
    {
        var tokeniser = new PatternTokeniser(@"[^,]+");
        var sink = new MaterialisingTokenSink();

        tokeniser.Tokenise(ReadOnlySpan<char>.Empty, sink);

        Assert.Empty(sink.Tokens);
    }

    [Fact(DisplayName = "PatternTokeniser: input with no matches produces no tokens")]
    public void InputWithNoMatchesProducesNoTokens()
    {
        var tokeniser = new PatternTokeniser(@"[0-9]+");
        var sink = new MaterialisingTokenSink();

        tokeniser.Tokenise("no numbers here", sink);

        Assert.Empty(sink.Tokens);
    }

    [Fact(DisplayName = "PatternTokeniser: pre-compiled regex constructor works")]
    public void PreCompiledRegexConstructorWorks()
    {
        var regex = new Regex(@"\b\w+\b", RegexOptions.Compiled);
        var tokeniser = new PatternTokeniser(regex);
        var sink = new MaterialisingTokenSink();

        tokeniser.Tokenise("hello world", sink);

        var tokens = sink.Tokens;
        Assert.Equal(2, tokens.Count);
        Assert.Equal("hello", tokens[0].Text);
        Assert.Equal("world", tokens[1].Text);
    }

    [Fact(DisplayName = "PatternTokeniser: skips zero-length matches")]
    public void SkipsZeroLengthMatches()
    {
        // Pattern that can match zero-length (e.g. word boundaries)
        var tokeniser = new PatternTokeniser(@"\w*");
        var sink = new MaterialisingTokenSink();

        tokeniser.Tokenise("ab cd", sink);

        // "ab", "cd" — zero-length matches between them are skipped
        var tokens = sink.Tokens;
        Assert.Equal(2, tokens.Count);
        Assert.Equal("ab", tokens[0].Text);
        Assert.Equal("cd", tokens[1].Text);
    }

    [Fact(DisplayName = "PatternTokeniser: default position increment is 1")]
    public void DefaultPositionIncrementIsOne()
    {
        var tokeniser = new PatternTokeniser(@"[^,]+");
        var sink = new MaterialisingTokenSink();

        tokeniser.Tokenise("a,b,c", sink);

        Assert.All(sink.Tokens, t => Assert.Equal(1, t.PositionIncrement));
    }
}
