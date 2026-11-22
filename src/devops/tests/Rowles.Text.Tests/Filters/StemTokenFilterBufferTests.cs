using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>
/// The span stemmer buffer contract: <see cref="StemTokenFilter"/> retries with larger buffers up to
/// <c>8 * length + 64</c> characters and fails loudly beyond that, rather than emitting the word
/// unstemmed (PR #131 review).
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Filters)]
public sealed class StemTokenFilterBufferTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(40)]
    public void ExpandingStemmer_GetsALargeEnoughBuffer(int wordLength)
    {
        string word = new('q', wordLength);
        var tokens = Analyse(new ExpandingStemmer(static length => length * 3), word);

        Assert.Single(tokens);
        Assert.Equal(new string('q', wordLength * 3), tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(wordLength, tokens[0].EndOffset);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(300)]
    public void StemAtTheDocumentedBound_IsAccepted(int wordLength)
    {
        int bound = (wordLength * 8) + 64;
        var tokens = Analyse(new ExpandingStemmer(_ => bound), new string('b', wordLength));

        Assert.Equal(bound, Assert.Single(tokens).Text.Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(300)]
    public void StemBeyondTheBound_Throws_NamingTheStemmer(int wordLength)
    {
        int tooLong = (wordLength * 8) + 65;
        var exception = Assert.Throws<InvalidOperationException>(
            () => Analyse(new ExpandingStemmer(_ => tooLong), new string('c', wordLength)));

        Assert.Contains(nameof(ExpandingStemmer), exception.Message, StringComparison.Ordinal);
        Assert.Contains($"{wordLength}-character token", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StemmerThatNeverFits_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Analyse(new NeverFitsStemmer(), "word"));
        Assert.Contains(nameof(NeverFitsStemmer), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GermanSharpS_StaysWellWithinTheBound()
    {
        // The largest built-in expansion is German ß -> ss, twice the word length.
        string word = new('ß', 200);
        var tokens = Analyse(new GermanStemmer(), word);
        Assert.Equal(new GermanStemmer().Stem(word), Assert.Single(tokens).Text);
    }

    private static List<Token> Analyse(ISpanStemmer stemmer, string text)
    {
        var analyser = new LanguageAnalyser(new WhitespaceTokeniser(), stopWords: [], stemmer); // No stop words: every token reaches the stemmer.
        var sink = new MaterialisingTokenSink();
        analyser.Analyse(text, sink);
        return sink.Tokens;
    }

    /// <summary>Writes <c>length(word)</c> copies of the word's first character, or -1 if they do not fit.</summary>
    private sealed class ExpandingStemmer(Func<int, int> length) : IShareableSpanStemmer
    {
        public int Stem(ReadOnlySpan<char> word, Span<char> output)
        {
            int needed = length(word.Length);
            if (output.Length < needed)
                return -1;
            output[..needed].Fill(word.IsEmpty ? 'x' : word[0]);
            return needed;
        }
    }

    private sealed class NeverFitsStemmer : IShareableSpanStemmer
    {
        public int Stem(ReadOnlySpan<char> word, Span<char> output) => -1;
    }
}
