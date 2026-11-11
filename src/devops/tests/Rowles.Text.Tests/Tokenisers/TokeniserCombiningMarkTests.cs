using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.Text.Tests;

/// <summary>
/// Verifies that combining marks continue a word but never start one.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Tokenisers)]
public sealed class TokeniserCombiningMarkTests
{
    private readonly Tokeniser _tokeniser = new();

    /// <summary>
    /// Verifies that a combining mark stays in the token on every scanning path.
    /// </summary>
    /// <param name="input">The text to tokenise.</param>
    /// <param name="expected">The expected tokens as <c>text@start-end</c>, separated by <c>|</c>.</param>
    [Theory(DisplayName = "Tokenise: Combining Marks Continue Words")]
    // Arabic with a trailing FATHA (U+064E, Mn), taken from the search corpus.
    [InlineData("مؤخراَ", "مؤخراَ@0-6")]
    // The same word after an ASCII block, so the SIMD path reaches the mark as a pending word.
    [InlineData("abcdefgh مؤخراَ end",
        "abcdefgh@0-8|مؤخراَ@9-15|end@16-19")]
    // Decomposed Latin acute accent (U+0301) inside and at the end of a word.
    [InlineData("café naïve", "café@0-5|naïve@6-12")]
    // A mark at the end of an eight-character ASCII block run.
    [InlineData("abcdefǵ x", "abcdefǵ@0-8|x@9-10")]
    // Devanagari vowel sign (U+093F, Mc) and an enclosing keycap (U+20E3, Me).
    [InlineData("कि 1⃣", "कि@0-2|1⃣@3-5")]
    // A mark after a space or at the start does not start a token.
    [InlineData("́abc ́ d", "abc@1-4|d@7-8")]
    public void Tokenise_CombiningMarksContinueWords(string input, string expected)
    {
        var sink = new MaterialisingTokenSink();
        _tokeniser.Tokenise(input, sink);
        string actual = string.Join("|", sink.Tokens.Select(t => $"{t.Text}@{t.StartOffset}-{t.EndOffset}"));
        Assert.Equal(expected, actual);

        var offsets = new List<(int Start, int End)>();
        _tokeniser.TokeniseOffsets(input, offsets);
        Assert.Equal(sink.Tokens.Select(t => (t.StartOffset, t.EndOffset)), offsets);
    }

    /// <summary>
    /// Verifies that the non-CJK runs of the CJK tokenisers keep combining marks.
    /// </summary>
    [Fact(DisplayName = "CJK Tokenisers: Combining Marks Continue Non-CJK Words")]
    public void CjkTokenisers_CombiningMarksContinueNonCjkWords()
    {
        const string input = "café 猫";
        foreach (ISpanTokeniser tokeniser in new ISpanTokeniser[] { new CJKBigramTokeniser(), new ChineseLexiconTokeniser(new[] { "\u732B" }) })
        {
            var sink = new MaterialisingTokenSink();
            tokeniser.Tokenise(input, sink);
            Assert.Contains(sink.Tokens, t => t.Text == "café" && t.StartOffset == 0 && t.EndOffset == 5);
        }
    }

    /// <summary>
    /// Verifies that the standard analyser keeps the Arabic diacritic in the indexed term.
    /// </summary>
    [Fact(DisplayName = "Standard Analyser: Keeps Trailing Arabic Diacritic")]
    public void StandardAnalyser_KeepsTrailingArabicDiacritic()
    {
        var sink = new MaterialisingTokenSink();
        new StandardAnalyser().Analyse("مؤخراَ", sink);
        var token = Assert.Single(sink.Tokens);
        Assert.Equal("مؤخراَ", token.Text);
        Assert.Equal(6, token.EndOffset);
    }
}
