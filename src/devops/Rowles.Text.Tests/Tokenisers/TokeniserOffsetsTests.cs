using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Tokenisers;

namespace Rowles.Text.Tests;

/// <summary>
/// Contains unit tests for Tokeniser Offsets.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Tokenisers)]
public class TokeniserOffsetsTests
{
    private readonly Tokeniser _tokeniser = new();

    /// <summary>
    /// Verifies the Tokenise Offsets: Sentence Returns Correct Offsets scenario.
    /// </summary>
    [Fact(DisplayName = "Tokenise Offsets: Sentence Returns Correct Offsets")]
    public void TokeniseOffsets_Sentence_ReturnsCorrectOffsets()
    {
        var offsets = new List<(int Start, int End)>();
        _tokeniser.TokeniseOffsets("The quick brown fox", offsets);

        Assert.Equal(4, offsets.Count);
        Assert.Equal((0, 3), offsets[0]);
        Assert.Equal((4, 9), offsets[1]);
        Assert.Equal((10, 15), offsets[2]);
        Assert.Equal((16, 19), offsets[3]);
    }

    /// <summary>
    /// Verifies the Tokenise Offsets: Empty Input Returns Empty List scenario.
    /// </summary>
    [Fact(DisplayName = "Tokenise Offsets: Empty Input Returns Empty List")]
    public void TokeniseOffsets_EmptyInput_ReturnsEmptyList()
    {
        var offsets = new List<(int Start, int End)>();
        _tokeniser.TokeniseOffsets(ReadOnlySpan<char>.Empty, offsets);

        Assert.Empty(offsets);
    }

    /// <summary>
    /// Verifies the Tokenise Offsets: Only Punctuation Returns Empty List scenario.
    /// </summary>
    [Fact(DisplayName = "Tokenise Offsets: Only Punctuation Returns Empty List")]
    public void TokeniseOffsets_OnlyPunctuation_ReturnsEmptyList()
    {
        var offsets = new List<(int Start, int End)>();
        _tokeniser.TokeniseOffsets("  , . ! ", offsets);

        Assert.Empty(offsets);
    }

    /// <summary>
    /// Verifies the Tokenise Offsets: Clears Existing Entries scenario.
    /// </summary>
    [Fact(DisplayName = "Tokenise Offsets: Clears Existing Entries")]
    public void TokeniseOffsets_ClearsExistingEntries()
    {
        var offsets = new List<(int Start, int End)> { (99, 100) };
        _tokeniser.TokeniseOffsets("hello", offsets);

        Assert.Single(offsets);
        Assert.Equal((0, 5), offsets[0]);
    }

    /// <summary>
    /// Verifies the Tokenise Offsets: Matches Tokenise scenario.
    /// </summary>
    [Fact(DisplayName = "Tokenise Offsets: Matches Tokenise")]
    public void TokeniseOffsets_MatchesTokenise()
    {
        const string input = "Hello, world! This is a test.";
        var matSink = new MaterialisingTokenSink();
        _tokeniser.Tokenise(input, matSink);
        var tokens = matSink.Tokens;
        var offsets = new List<(int Start, int End)>();
        _tokeniser.TokeniseOffsets(input, offsets);

        Assert.Equal(tokens.Count, offsets.Count);
        for (int i = 0; i < tokens.Count; i++)
        {
            Assert.Equal(tokens[i].StartOffset, offsets[i].Start);
            Assert.Equal(tokens[i].EndOffset, offsets[i].End);
        }
    }

    /// <summary>
    /// Verifies that offset-only tokenisation recognises supplementary letters and digits using UTF-16 offsets.
    /// </summary>
    [Fact(DisplayName = "Tokenise Offsets: Supplementary Letters And Digits Use UTF-16 Offsets")]
    public void TokeniseOffsets_SupplementaryLettersAndDigits_UsesUtf16Offsets()
    {
        const string input = "A\U00010400B \U0001D7D8 \uD800x\uDC00";
        var offsets = new List<(int Start, int End)>();

        _tokeniser.TokeniseOffsets(input, offsets);

        Assert.Equal(new[] { (0, 4), (5, 7), (9, 10) }, offsets);
    }
}
