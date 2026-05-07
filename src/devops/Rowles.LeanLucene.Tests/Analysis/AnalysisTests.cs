using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Analysis.Tokenisers;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Contains unit tests for Analysis.
/// </summary>
[Trait("Category", "Analysis")]
public sealed class AnalysisTests
{
    /// <summary>
    /// Verifies the Tokeniser: Basic Sentence Produces Correct Tokens scenario.
    /// </summary>
    [Fact(DisplayName = "Tokeniser: Basic Sentence Produces Correct Tokens")]
    public void Tokeniser_BasicSentence_ProducesCorrectTokens()
    {
        var tokeniser = new Tokeniser();
        var input = "The quick brown fox".AsSpan();
        var tokens = tokeniser.Tokenise(input);

        var expected = new[] { "The", "quick", "brown", "fox" };
        Assert.Equal(expected.Length, tokens.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], tokens[i].Text.ToString());
            // Verify offsets point back to original text
            Assert.Equal(expected[i],
                input.Slice(tokens[i].StartOffset, tokens[i].EndOffset - tokens[i].StartOffset).ToString());
        }
    }

    /// <summary>
    /// Verifies the Tokeniser: Punctuation Is Excluded From Tokens scenario.
    /// </summary>
    [Fact(DisplayName = "Tokeniser: Punctuation Is Excluded From Tokens")]
    public void Tokeniser_Punctuation_IsExcludedFromTokens()
    {
        var tokeniser = new Tokeniser();
        var tokens = tokeniser.Tokenise("hello, world!".AsSpan());

        Assert.Equal(2, tokens.Count);
        Assert.Equal("hello", tokens[0].Text.ToString());
        Assert.Equal("world", tokens[1].Text.ToString());
    }

    /// <summary>
    /// Verifies the Lowercase Filter: Mutates In Place No Allocation scenario.
    /// </summary>
    [Fact(DisplayName = "Lowercase Filter: Mutates In Place No Allocation")]
    public void LowercaseFilter_MutatesInPlace_NoAllocation()
    {
        var filter = new LowercaseFilter();
        char[] buffer = "Hello WORLD FoO".ToCharArray();
        filter.Apply(buffer.AsSpan());
        Assert.Equal("hello world foo", new string(buffer));
    }

    /// <summary>
    /// Verifies the Stop Word Filter: Removes Common Words scenario.
    /// </summary>
    [Fact(DisplayName = "Stop Word Filter: Removes Common Words")]
    public void StopWordFilter_RemovesCommonWords()
    {
        // "to", "be", "or", "not" are all stop words; only "live" survives.
        var analyser = new StandardAnalyser();
        var tokens = analyser.Analyse("to be or not to live".AsSpan());

        Assert.Single(tokens);
        Assert.Equal("live", tokens[0].Text.ToString());
    }

    /// <summary>
    /// Verifies the Standard Analyser: End-to-end Lowercases Tokenises Filters scenario.
    /// </summary>
    [Fact(DisplayName = "Standard Analyser: End-to-end Lowercases Tokenises Filters")]
    public void StandardAnalyser_EndToEnd_LowercasesTokenisesFilters()
    {
        var analyser = new StandardAnalyser();
        var tokens = analyser.Analyse("Running quickly in THE forest".AsSpan());

        var expected = new[] { "running", "quickly", "forest" };
        Assert.Equal(expected.Length, tokens.Count);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], tokens[i].Text.ToString());
    }
}
