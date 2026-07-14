using Xunit;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.LeanCorpus.Tests.AOTSmoke;

public class TokeniserSmokeTests
{
    [Fact]
    public void PatternTokeniser_BackslashSPlus()
    {
        var tokeniser = new PatternTokeniser(@"\S+");
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("the quick brown fox", sink);
        Assert.Equal(4, sink.Count);
    }

    [Fact]
    public void PatternTokeniser_CompiledRegex()
    {
        var regex = new System.Text.RegularExpressions.Regex(@"[^,]+",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant,
            System.TimeSpan.FromSeconds(1));
        var tokeniser = new PatternTokeniser(regex);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("alpha,beta,gamma", sink);
        Assert.Equal(3, sink.Count);
    }

    [Fact]
    public void CJKBigramTokeniser_ProducesBigrams()
    {
        var tokeniser = new CJKBigramTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("\u4F60\u597D\u4E16\u754C", sink);
        Assert.True(sink.Count >= 2);
    }

    [Fact]
    public void EdgeNGramTokeniser_ProducesGrams()
    {
        var tokeniser = new EdgeNGramTokeniser(minGram: 2, maxGram: 4);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("hello", sink);
        Assert.True(sink.Count >= 2);
    }

    [Fact]
    public void NGramTokeniser_ProducesGrams()
    {
        var tokeniser = new NGramTokeniser(minGram: 2, maxGram: 3);
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("abcd", sink);
        Assert.True(sink.Count >= 3);
    }

    [Fact]
    public void WhitespaceTokeniser_SplitsOnWhitespace()
    {
        var tokeniser = new WhitespaceTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise(" a  b c ", sink);
        Assert.Equal(3, sink.Count);
    }

    [Fact]
    public void LetterTokeniser_SplitsOnNonLetters()
    {
        var tokeniser = new LetterTokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("hello, world!", sink);
        Assert.Equal(2, sink.Count);
    }

    [Fact]
    public void StandardTokeniser_SplitsOnWordBoundaries()
    {
        var tokeniser = new Tokeniser();
        var sink = new CountingTokenSink();
        tokeniser.Tokenise("Hello, world! This is a test.", sink);
        Assert.True(sink.Count >= 5);
    }
}
