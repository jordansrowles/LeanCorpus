using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Analysis.Tokenisers;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Contains unit tests for Lucene-compatible analysis parity components.
/// </summary>
[Trait("Category", "Analysis")]
public class AnalysisParityTests
{
    /// <summary>
    /// Verifies the Whitespace Tokeniser: Preserves Punctuation And Offsets scenario.
    /// </summary>
    [Fact(DisplayName = "Whitespace Tokeniser: Preserves Punctuation And Offsets")]
    public void WhitespaceTokeniser_PreservesPunctuationAndOffsets()
    {
        var tokeniser = new WhitespaceTokeniser();

        var tokens = tokeniser.Tokenise("Hello,  world!\tagain");

        Assert.Equal(3, tokens.Count);
        Assert.Equal("Hello,", tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(6, tokens[0].EndOffset);
        Assert.Equal("world!", tokens[1].Text);
        Assert.Equal(8, tokens[1].StartOffset);
        Assert.Equal(14, tokens[1].EndOffset);
        Assert.Equal("again", tokens[2].Text);
        Assert.Equal(15, tokens[2].StartOffset);
        Assert.Equal(20, tokens[2].EndOffset);
    }

    /// <summary>
    /// Verifies the Keyword Tokeniser: Empty Input Returns Empty List scenario.
    /// </summary>
    [Fact(DisplayName = "Keyword Tokeniser: Empty Input Returns Empty List")]
    public void KeywordTokeniser_EmptyInput_ReturnsEmptyList()
    {
        var tokeniser = new KeywordTokeniser();

        var tokens = tokeniser.Tokenise(ReadOnlySpan<char>.Empty);

        Assert.Empty(tokens);
    }

    /// <summary>
    /// Verifies the Keyword Tokeniser: Non Empty Input Returns Full Span Token scenario.
    /// </summary>
    [Fact(DisplayName = "Keyword Tokeniser: Non Empty Input Returns Full Span Token")]
    public void KeywordTokeniser_NonEmptyInput_ReturnsFullSpanToken()
    {
        var tokeniser = new KeywordTokeniser();

        var tokens = tokeniser.Tokenise("ID-123 Value");

        Assert.Single(tokens);
        Assert.Equal("ID-123 Value", tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(12, tokens[0].EndOffset);
    }

    /// <summary>
    /// Verifies the Letter Tokeniser: Splits On Digits And Punctuation scenario.
    /// </summary>
    [Fact(DisplayName = "Letter Tokeniser: Splits On Digits And Punctuation")]
    public void LetterTokeniser_SplitsOnDigitsAndPunctuation()
    {
        var tokeniser = new LetterTokeniser();

        var tokens = tokeniser.Tokenise("abc123déf!");

        Assert.Equal(2, tokens.Count);
        Assert.Equal("abc", tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(3, tokens[0].EndOffset);
        Assert.Equal("déf", tokens[1].Text);
        Assert.Equal(6, tokens[1].StartOffset);
        Assert.Equal(9, tokens[1].EndOffset);
    }

    /// <summary>
    /// Verifies the Whitespace Analyser: Applies No Normalisation scenario.
    /// </summary>
    [Fact(DisplayName = "Whitespace Analyser: Applies No Normalisation")]
    public void WhitespaceAnalyser_AppliesNoNormalisation()
    {
        var analyser = new WhitespaceAnalyser();

        var tokens = analyser.Analyse("The, QUICK");

        Assert.Equal(["The,", "QUICK"], tokens.Select(t => t.Text));
    }

    /// <summary>
    /// Verifies the Keyword Analyser: Returns Full Input Token scenario.
    /// </summary>
    [Fact(DisplayName = "Keyword Analyser: Returns Full Input Token")]
    public void KeywordAnalyser_ReturnsFullInputToken()
    {
        var analyser = new KeywordAnalyser();

        var tokens = analyser.Analyse("Mixed CASE, punctuation!");

        Assert.Single(tokens);
        Assert.Equal("Mixed CASE, punctuation!", tokens[0].Text);
    }

    /// <summary>
    /// Verifies the Simple Analyser: Lowercases Letter Tokens And Keeps Stop Words scenario.
    /// </summary>
    [Fact(DisplayName = "Simple Analyser: Lowercases Letter Tokens And Keeps Stop Words")]
    public void SimpleAnalyser_LowercasesLetterTokensAndKeepsStopWords()
    {
        var analyser = new SimpleAnalyser();

        var tokens = analyser.Analyse("The QUICK 123 fox");

        Assert.Equal(["the", "quick", "fox"], tokens.Select(t => t.Text));
    }

    /// <summary>
    /// Verifies the Length Filter: Removes Tokens Outside Inclusive Range scenario.
    /// </summary>
    [Fact(DisplayName = "Length Filter: Removes Tokens Outside Inclusive Range")]
    public void LengthFilter_RemovesTokensOutsideInclusiveRange()
    {
        var tokens = new List<Token> { new("a", 0, 1), new("abc", 2, 5), new("abcdef", 6, 12) };
        var filter = new LengthFilter(2, 4);

        filter.Apply(tokens);

        Assert.Single(tokens);
        Assert.Equal("abc", tokens[0].Text);
    }

    /// <summary>
    /// Verifies the Length Filter: Rejects Invalid Range scenario.
    /// </summary>
    [Fact(DisplayName = "Length Filter: Rejects Invalid Range")]
    public void LengthFilter_RejectsInvalidRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LengthFilter(-1, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LengthFilter(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LengthFilter(5, 4));
    }

    /// <summary>
    /// Verifies the Truncate Token Filter: Truncates Text And End Offset scenario.
    /// </summary>
    [Fact(DisplayName = "Truncate Token Filter: Truncates Text And End Offset")]
    public void TruncateTokenFilter_TruncatesTextAndEndOffset()
    {
        var tokens = new List<Token> { new("abcdef", 10, 16), new("xy", 20, 22) };
        var filter = new TruncateTokenFilter(3);

        filter.Apply(tokens);

        Assert.Equal("abc", tokens[0].Text);
        Assert.Equal(10, tokens[0].StartOffset);
        Assert.Equal(13, tokens[0].EndOffset);
        Assert.Equal("xy", tokens[1].Text);
        Assert.Equal(22, tokens[1].EndOffset);
    }

    /// <summary>
    /// Verifies the Truncate Token Filter: Rejects Invalid Length scenario.
    /// </summary>
    [Fact(DisplayName = "Truncate Token Filter: Rejects Invalid Length")]
    public void TruncateTokenFilter_RejectsInvalidLength()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TruncateTokenFilter(0));
    }

    /// <summary>
    /// Verifies the Unique Token Filter: Removes Global Duplicates scenario.
    /// </summary>
    [Fact(DisplayName = "Unique Token Filter: Removes Global Duplicates")]
    public void UniqueTokenFilter_RemovesGlobalDuplicates()
    {
        var tokens = new List<Token> { new("a", 0, 1), new("b", 2, 3), new("a", 4, 5) };
        var filter = new UniqueTokenFilter();

        filter.Apply(tokens);

        Assert.Equal(["a", "b"], tokens.Select(t => t.Text));
    }

    /// <summary>
    /// Verifies the Unique Token Filter: Same Position Mode Keeps Later Position scenario.
    /// </summary>
    [Fact(DisplayName = "Unique Token Filter: Same Position Mode Keeps Later Position")]
    public void UniqueTokenFilter_SamePositionModeKeepsLaterPosition()
    {
        var tokens = new List<Token>
        {
            new("fast", 0, 4),
            new("quick", 0, 4),
            new("fast", 0, 4),
            new("fast", 5, 9),
        };
        var filter = new UniqueTokenFilter(onlyOnSamePosition: true);

        filter.Apply(tokens);

        Assert.Equal(["fast", "quick", "fast"], tokens.Select(t => t.Text));
        Assert.Equal(5, tokens[2].StartOffset);
    }

    /// <summary>
    /// Verifies the Decimal Digit Filter: Normalises Unicode Digits scenario.
    /// </summary>
    [Fact(DisplayName = "Decimal Digit Filter: Normalises Unicode Digits")]
    public void DecimalDigitFilter_NormalisesUnicodeDigits()
    {
        var tokens = new List<Token> { new("\u0661\u06F2\uFF134x", 0, 5) };
        var filter = new DecimalDigitFilter();

        filter.Apply(tokens);

        Assert.Equal("1234x", tokens[0].Text);
        Assert.Equal(0, tokens[0].StartOffset);
        Assert.Equal(5, tokens[0].EndOffset);
    }

    /// <summary>
    /// Verifies the Reverse String Filter: Reverses Token Text scenario.
    /// </summary>
    [Fact(DisplayName = "Reverse String Filter: Reverses Token Text")]
    public void ReverseStringFilter_ReversesTokenText()
    {
        var tokens = new List<Token> { new("café", 2, 6), new("x", 7, 8) };
        var filter = new ReverseStringFilter();

        filter.Apply(tokens);

        Assert.Equal("éfac", tokens[0].Text);
        Assert.Equal(2, tokens[0].StartOffset);
        Assert.Equal(6, tokens[0].EndOffset);
        Assert.Equal("x", tokens[1].Text);
    }

    /// <summary>
    /// Verifies the Elision Filter: Removes Default French Article scenario.
    /// </summary>
    [Fact(DisplayName = "Elision Filter: Removes Default French Article")]
    public void ElisionFilter_RemovesDefaultFrenchArticle()
    {
        var tokens = new List<Token> { new("l'avion", 0, 7), new("qu\u2019elle", 10, 17) };
        var filter = new ElisionFilter();

        filter.Apply(tokens);

        Assert.Equal("avion", tokens[0].Text);
        Assert.Equal(2, tokens[0].StartOffset);
        Assert.Equal(7, tokens[0].EndOffset);
        Assert.Equal("elle", tokens[1].Text);
        Assert.Equal(13, tokens[1].StartOffset);
        Assert.Equal(17, tokens[1].EndOffset);
    }

    /// <summary>
    /// Verifies the Keyword Marker Filter: Stemmed Analyser Skips Marked Token scenario.
    /// </summary>
    [Fact(DisplayName = "Keyword Marker Filter: Stemmed Analyser Skips Marked Token")]
    public void KeywordMarkerFilter_StemmedAnalyserSkipsMarkedToken()
    {
        var analyser = new StemmedAnalyser(new KeywordMarkerFilter(["running"]));

        var tokens = analyser.Analyse("running jumped");

        Assert.Equal(["running", "jump"], tokens.Select(t => t.Text));
    }

    /// <summary>
    /// Verifies the Keyword Marker Filter: Language Analyser Skips Marked Token scenario.
    /// </summary>
    [Fact(DisplayName = "Keyword Marker Filter: Language Analyser Skips Marked Token")]
    public void KeywordMarkerFilter_LanguageAnalyserSkipsMarkedToken()
    {
        var analyser = new LanguageAnalyser(new Tokeniser(), StopWords.English, new EnglishStemmer(), new KeywordMarkerFilter(["running"]));

        var tokens = analyser.Analyse("running jumped");

        Assert.Equal(["running", "jump"], tokens.Select(t => t.Text));
    }

    /// <summary>
    /// Verifies the Shingle Filter: Emits Unigrams And Shingles scenario.
    /// </summary>
    [Fact(DisplayName = "Shingle Filter: Emits Unigrams And Shingles")]
    public void ShingleFilter_EmitsUnigramsAndShingles()
    {
        var tokens = new List<Token> { new("new", 0, 3), new("york", 4, 8), new("city", 9, 13) };
        var filter = new ShingleFilter(minShingleSize: 2, maxShingleSize: 3);

        filter.Apply(tokens);

        Assert.Equal(["new", "york", "city", "new york", "new york city", "york city"], tokens.Select(t => t.Text));
        Assert.Equal(0, tokens[3].StartOffset);
        Assert.Equal(8, tokens[3].EndOffset);
        Assert.Equal(0, tokens[4].StartOffset);
        Assert.Equal(13, tokens[4].EndOffset);
    }

    /// <summary>
    /// Verifies the Shingle Filter: Rejects Invalid Sizes scenario.
    /// </summary>
    [Fact(DisplayName = "Shingle Filter: Rejects Invalid Sizes")]
    public void ShingleFilter_RejectsInvalidSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShingleFilter(0, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ShingleFilter(3, 2));
    }

    /// <summary>
    /// Verifies the Word Delimiter Filter: Splits Compound Token scenario.
    /// </summary>
    [Fact(DisplayName = "Word Delimiter Filter: Splits Compound Token")]
    public void WordDelimiterFilter_SplitsCompoundToken()
    {
        var tokens = new List<Token> { new("WiFi4Schools_test", 10, 27) };
        var filter = new WordDelimiterFilter();

        filter.Apply(tokens);

        Assert.Equal(["Wi", "Fi", "4", "Schools", "test"], tokens.Select(t => t.Text));
        Assert.Equal(10, tokens[0].StartOffset);
        Assert.Equal(12, tokens[0].EndOffset);
        Assert.Equal(14, tokens[2].StartOffset);
        Assert.Equal(15, tokens[2].EndOffset);
        Assert.Equal(23, tokens[4].StartOffset);
        Assert.Equal(27, tokens[4].EndOffset);
    }

    /// <summary>
    /// Verifies the Word Delimiter Filter: Preserves Original And Concatenates scenario.
    /// </summary>
    [Fact(DisplayName = "Word Delimiter Filter: Preserves Original And Concatenates")]
    public void WordDelimiterFilter_PreservesOriginalAndConcatenates()
    {
        var tokens = new List<Token> { new("abc-123-def", 0, 11) };
        var filter = new WordDelimiterFilter(preserveOriginal: true, concatenateWords: true, concatenateNumbers: true);

        filter.Apply(tokens);

        Assert.Equal(["abc-123-def", "abc", "123", "def", "abcdef"], tokens.Select(t => t.Text));
        Assert.Equal(0, tokens[4].StartOffset);
        Assert.Equal(11, tokens[4].EndOffset);
    }
}
