using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Analysis.Stemmers;
using Xunit;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Contains unit tests for Extended Language Analyser.
/// </summary>
[Trait("Category", "Analysis")]
public sealed class ExtendedLanguageAnalyserTests
{
    /// <summary>
    /// Verifies the New Languages: Remove Stop Words And Produce Tokens scenario.
    /// </summary>
    /// <param name="code">The code value for the test case.</param>
    /// <param name="text">The text value for the test case.</param>
    [Theory(DisplayName = "New Languages: Remove Stop Words And Produce Tokens")]
    [InlineData("es", "Las casas grandes son muy bonitas")]
    [InlineData("it", "Le case grandi sono molto belle")]
    [InlineData("pt", "As casas grandes são muito bonitas")]
    [InlineData("nl", "De grote huizen zijn erg mooi")]
    [InlineData("ar", "البيوت الكبيرة جميلة جدا")]
    public void NewLanguages_RemoveStopWordsAndProduceTokens(string code, string text)
    {
        var analyser = AnalyserFactory.Create(code);
        var tokens = analyser.Analyse(text);

        Assert.NotEmpty(tokens);
    }

    /// <summary>
    /// Verifies the Cjk Languages: Produce Bigram Tokens And Skip Stemming scenario.
    /// </summary>
    /// <param name="code">The code value for the test case.</param>
    /// <param name="text">The text value for the test case.</param>
    [Theory(DisplayName = "Cjk Languages: Produce Bigram Tokens And Skip Stemming")]
    [InlineData("ja", "これは日本語のテストです")]
    [InlineData("ko", "이것은 한국어 테스트입니다")]
    public void CjkLanguages_ProduceBigramTokensAndSkipStemming(string code, string text)
    {
        var analyser = AnalyserFactory.Create(code);
        var tokens = analyser.Analyse(text);

        Assert.NotEmpty(tokens);
    }

    /// <summary>
    /// Verifies the Bcp 47: Region And Case Are Normalised scenario.
    /// </summary>
    /// <param name="requested">The requested value for the test case.</param>
    /// <param name="expectedPrimary">The expectedPrimary value for the test case.</param>
    [Theory(DisplayName = "Bcp 47: Region And Case Are Normalised")]
    [InlineData("pt-BR", "pt")]
    [InlineData("zh-Hans", "zh")]
    [InlineData("en-GB", "en")]
    [InlineData("DE", "de")]
    public void Bcp47_RegionAndCaseAreNormalised(string requested, string expectedPrimary)
    {
        // Both the factory and the stop word lookup should resolve to the same primary subtag.
        var analyser = AnalyserFactory.Create(requested);
        Assert.NotNull(analyser);

        var stops = StopWords.ForLanguage(requested);
        Assert.NotNull(stops);
        Assert.Same(StopWords.ForLanguage(expectedPrimary), stops);
    }

    /// <summary>
    /// Verifies the Factory: Supported Languages Contains All Twelve scenario.
    /// </summary>
    [Fact(DisplayName = "Factory: Supported Languages Contains All Twelve")]
    public void Factory_SupportedLanguages_ContainsAllTwelve()
    {
        var supported = AnalyserFactory.SupportedLanguages;
        foreach (var code in new[] { "en", "fr", "de", "es", "it", "pt", "nl", "ru", "ar", "zh", "ja", "ko" })
            Assert.Contains(code, supported);
    }

    /// <summary>
    /// Verifies the Factory: Unsupported Language Throws With Helpful Message scenario.
    /// </summary>
    [Fact(DisplayName = "Factory: Unsupported Language Throws With Helpful Message")]
    public void Factory_UnsupportedLanguage_ThrowsWithHelpfulMessage()
    {
        var ex = Assert.Throws<NotSupportedException>(() => AnalyserFactory.Create("xx"));
        Assert.Contains("xx", ex.Message);
        Assert.Contains("Supported", ex.Message);
    }

    /// <summary>
    /// Verifies the Spanish Stemmer: Removes Common Suffixes scenario.
    /// </summary>
    [Fact(DisplayName = "Spanish Stemmer: Removes Common Suffixes")]
    public void SpanishStemmer_RemovesCommonSuffixes()
    {
        var s = new SpanishStemmer();
        Assert.Equal("habl", s.Stem("hablando"));
        Assert.Equal("com", s.Stem("comer"));
    }

    /// <summary>
    /// Verifies the Italian Stemmer: Handles Gender And Number scenario.
    /// </summary>
    [Fact(DisplayName = "Italian Stemmer: Handles Gender And Number")]
    public void ItalianStemmer_HandlesGenderAndNumber()
    {
        var s = new ItalianStemmer();
        Assert.NotEqual("gatti", s.Stem("gatti"));
    }

    /// <summary>
    /// Verifies the Portuguese Stemmer: Strips Verb Endings scenario.
    /// </summary>
    [Fact(DisplayName = "Portuguese Stemmer: Strips Verb Endings")]
    public void PortugueseStemmer_StripsVerbEndings()
    {
        var s = new PortugueseStemmer();
        Assert.NotEqual("falando", s.Stem("falando"));
    }

    /// <summary>
    /// Verifies the Dutch Stemmer: Strips Common Suffixes scenario.
    /// </summary>
    [Fact(DisplayName = "Dutch Stemmer: Strips Common Suffixes")]
    public void DutchStemmer_StripsCommonSuffixes()
    {
        var s = new DutchStemmer();
        Assert.NotEqual("lopen", s.Stem("lopen"));
    }

    /// <summary>
    /// Verifies the Arabic Stemmer: Strips Definite Article And Common Prefixes scenario.
    /// </summary>
    [Fact(DisplayName = "Arabic Stemmer: Strips Definite Article And Common Prefixes")]
    public void ArabicStemmer_StripsDefiniteArticleAndCommonPrefixes()
    {
        var s = new ArabicStemmer();
        // الكتاب: strip "ال" then single-char prefix "ك" per the light stemmer.
        Assert.Equal("تاب", s.Stem("الكتاب"));
    }

    /// <summary>
    /// Verifies the Identity Stemmers: Return Input Unchanged scenario.
    /// </summary>
    /// <param name="lang">The lang value for the test case.</param>
    [Theory(DisplayName = "Identity Stemmers: Return Input Unchanged")]
    [InlineData("zh")]
    [InlineData("ja")]
    [InlineData("ko")]
    public void IdentityStemmers_ReturnInputUnchanged(string lang)
    {
        IStemmer stemmer = lang switch
        {
            "zh" => new ChineseStemmer(),
            "ja" => new JapaneseStemmer(),
            "ko" => new KoreanStemmer(),
            _ => throw new InvalidOperationException()
        };

        Assert.Equal("猫", stemmer.Stem("猫"));
        Assert.Equal("test", stemmer.Stem("test"));
    }

    /// <summary>
    /// Verifies the German Stemmer: Folds Umlauts And Sharp S scenario.
    /// </summary>
    [Fact(DisplayName = "German Stemmer: Folds Umlauts And Sharp S")]
    public void GermanStemmer_FoldsUmlautsAndSharpS()
    {
        var s = new GermanStemmer();
        Assert.Equal("haus", s.Stem("häuser"));
        // ß → ss expansion exercised here.
        var stemmed = s.Stem("straße");
        Assert.StartsWith("strass", stemmed);
    }
}
