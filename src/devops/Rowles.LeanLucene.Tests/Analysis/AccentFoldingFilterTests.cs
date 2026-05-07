using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Unit tests for the <see cref="AccentFoldingFilter"/> class.
/// Verifies that accented characters are correctly folded to their ASCII equivalents.
/// </summary>
[Trait("Category", "Analysis")]
public class AccentFoldingFilterTests
{
    private readonly AccentFoldingFilter _filter = new();

    /// <summary>
    /// Tests that a list containing accented tokens is transformed so each token's text
    /// becomes its ASCII-folding equivalent.
    /// </summary>
    [Fact(DisplayName = "Apply: Accented tokens are folded to their ASCII equivalents")]
    public void Apply_AccentedTokens_FoldsToAscii()
    {
        // Arrange
        var tokens = new List<Token>
        {
            new("café", 0, 4),
            new("naïve", 5, 10),
            new("résumé", 11, 17)
        };

        // Act
        _filter.Apply(tokens);

        // Assert
        Assert.Equal("cafe", tokens[0].Text);
        Assert.Equal("naive", tokens[1].Text);
        Assert.Equal("resume", tokens[2].Text);
    }

    /// <summary>
    /// Tests that tokens already containing only ASCII characters are left unchanged,
    /// and the original string reference is preserved (no unnecessary new allocations).
    /// </summary>
    [Fact(DisplayName = "Apply: ASCII-only tokens remain unchanged and keep original reference")]
    public void Apply_AsciiTokens_UnchangedReferences()
    {
        // Arrange
        var original = "hello";
        var tokens = new List<Token> { new(original, 0, 5) };

        // Act
        _filter.Apply(tokens);

        // Assert — original reference returned when no change needed
        Assert.Same(original, tokens[0].Text);
    }

    /// <summary>
    /// Tests that passing an empty token list does not cause any errors or modifications.
    /// </summary>
    [Fact(DisplayName = "Apply: Empty token list causes no error and stays empty")]
    public void Apply_EmptyList_NoError()
    {
        var tokens = new List<Token>();
        _filter.Apply(tokens);
        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the static <see cref="AccentFoldingFilter.Fold"/> method
    /// correctly transforms various diacritic‑heavy strings into their plain ASCII forms.
    /// </summary>
    /// <param name="input">The string with diacritics.</param>
    /// <param name="expected">The expected ASCII–folded result.</param>
    [Theory(DisplayName = "Fold: Various diacritics are folded correctly")]
    [InlineData("über", "uber")]
    [InlineData("señor", "senor")]
    [InlineData("Ångström", "Angstrom")]
    public void Fold_VariousDiacritics_FoldsCorrectly(string input, string expected)
    {
        var result = AccentFoldingFilter.Fold(input);
        Assert.Equal(expected, result);
    }
}
