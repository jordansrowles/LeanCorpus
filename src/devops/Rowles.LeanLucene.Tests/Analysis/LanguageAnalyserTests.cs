using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Analysis.Stemmers;
using Rowles.LeanLucene.Analysis.Tokenisers;
using Xunit;

namespace Rowles.LeanLucene.Tests.Analysis;

/// <summary>
/// Contains unit tests for Language Analyser.
/// </summary>
public sealed class LanguageAnalyserTests
{
    /// <summary>
    /// Verifies the Factory Create: English Stems And Removes Stop Words scenario.
    /// </summary>
    [Fact(DisplayName = "Factory Create: English Stems And Removes Stop Words")]
    public void FactoryCreate_English_StemsAndRemovesStopWords()
    {
        var analyser = AnalyserFactory.Create("en");
        var tokens = analyser.Analyse("The running foxes jumped over the lazy dogs");

        var texts = tokens.Select(t => t.Text).ToList();
        // "the" is a stop word and should be removed
        Assert.DoesNotContain("the", texts);
        Assert.Contains("run", texts); // "running" -> "run" via Porter
        Assert.Contains("fox", texts); // "foxes" -> "fox"
        Assert.Contains("jump", texts); // "jumped" -> "jump"
    }

    /// <summary>
    /// Verifies the French Analyser: Stems And Removes Stop Words scenario.
    /// </summary>
    [Fact(DisplayName = "French Analyser: Stems And Removes Stop Words")]
    public void FrenchAnalyser_StemsAndRemovesStopWords()
    {
        var analyser = AnalyserFactory.Create("fr");
        var tokens = analyser.Analyse("Les maisons sont grandes et belles");

        var texts = tokens.Select(t => t.Text).ToList();
        Assert.DoesNotContain("les", texts);
        Assert.DoesNotContain("et", texts);
        Assert.DoesNotContain("sont", texts);
        Assert.True(texts.Count > 0, "Should have non-stop-word tokens");
    }

    /// <summary>
    /// Verifies the German Analyser: Normalises Umlauts And Stems scenario.
    /// </summary>
    [Fact(DisplayName = "German Analyser: Normalises Umlauts And Stems")]
    public void GermanAnalyser_NormalisesUmlautsAndStems()
    {
        var analyser = AnalyserFactory.Create("de");
        var tokens = analyser.Analyse("Die Häuser und Straßen sind schön");

        var texts = tokens.Select(t => t.Text).ToList();
        Assert.DoesNotContain("die", texts);
        Assert.DoesNotContain("und", texts);
        Assert.DoesNotContain("sind", texts);
        // "häuser" → "hauser" (umlaut normalised) → further stemmed
        Assert.True(texts.Any(t => t.StartsWith("haus")), "Should normalise 'Häuser' to stem starting with 'haus'");
    }

    /// <summary>
    /// Verifies the Russian Analyser: Removes Stop Words And Stems scenario.
    /// </summary>
    [Fact(DisplayName = "Russian Analyser: Removes Stop Words And Stems")]
    public void RussianAnalyser_RemovesStopWordsAndStems()
    {
        var analyser = AnalyserFactory.Create("ru");
        var tokens = analyser.Analyse("Это большой и красивый город");

        var texts = tokens.Select(t => t.Text).ToList();
        Assert.DoesNotContain("это", texts);
        Assert.DoesNotContain("и", texts);
        Assert.True(texts.Count > 0, "Should have non-stop-word tokens");
    }

    /// <summary>
    /// Verifies the Chinese Analyser: Produces Bigrams scenario.
    /// </summary>
    [Fact(DisplayName = "Chinese Analyser: Produces Bigrams")]
    public void ChineseAnalyser_ProducesBigrams()
    {
        var analyser = AnalyserFactory.Create("zh");
        var tokens = analyser.Analyse("中华人民共和国");

        // 7 characters → 6 overlapping bigrams
        Assert.Equal(6, tokens.Count);
        Assert.Equal("中华", tokens[0].Text);
        Assert.Equal("华人", tokens[1].Text);
        Assert.Equal("人民", tokens[2].Text);
        Assert.Equal("民共", tokens[3].Text);
        Assert.Equal("共和", tokens[4].Text);
        Assert.Equal("和国", tokens[5].Text);
    }

    /// <summary>
    /// Verifies the Chinese Analyser: Mixed CJK And Latin scenario.
    /// </summary>
    [Fact(DisplayName = "Chinese Analyser: Mixed CJK And Latin")]
    public void ChineseAnalyser_MixedCJKAndLatin()
    {
        var analyser = AnalyserFactory.Create("zh");
        var tokens = analyser.Analyse("Hello 世界 World");

        var texts = tokens.Select(t => t.Text).ToList();
        Assert.Contains("hello", texts);
        Assert.Contains("世界", texts); // single bigram from 2-char run
        Assert.Contains("world", texts);
    }

    /// <summary>
    /// Verifies the Analyser Factory: Unsupported Language Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Analyser Factory: Unsupported Language Throws")]
    public void AnalyserFactory_UnsupportedLanguage_Throws()
    {
        Assert.Throws<NotSupportedException>(() => AnalyserFactory.Create("xx"));
    }

    /// <summary>
    /// Verifies the Analyser Factory: Supported Languages Contains Expected scenario.
    /// </summary>
    [Fact(DisplayName = "Analyser Factory: Supported Languages Contains Expected")]
    public void AnalyserFactory_SupportedLanguages_ContainsExpected()
    {
        var supported = AnalyserFactory.SupportedLanguages;
        Assert.Contains("en", supported);
        Assert.Contains("fr", supported);
        Assert.Contains("de", supported);
        Assert.Contains("ru", supported);
        Assert.Contains("zh", supported);
    }

    /// <summary>
    /// Verifies the CJK Bigram Tokeniser: Single Character Emits Unigram scenario.
    /// </summary>
    [Fact(DisplayName = "CJK Bigram Tokeniser: Single Character Emits Unigram")]
    public void CJKBigramTokeniser_SingleCharacter_EmitsUnigram()
    {
        var tokeniser = new CJKBigramTokeniser();
        var tokens = tokeniser.Tokenise("猫");

        Assert.Single(tokens);
        Assert.Equal("猫", tokens[0].Text);
    }

    /// <summary>
    /// Verifies the Stop Words: For Language Returns Correct Lists scenario.
    /// </summary>
    [Fact(DisplayName = "Stop Words: For Language Returns Correct Lists")]
    public void StopWords_ForLanguage_ReturnsCorrectLists()
    {
        Assert.NotNull(StopWords.ForLanguage("en"));
        Assert.NotNull(StopWords.ForLanguage("fr"));
        Assert.NotNull(StopWords.ForLanguage("de"));
        Assert.NotNull(StopWords.ForLanguage("ru"));
        Assert.NotNull(StopWords.ForLanguage("zh"));
        Assert.Null(StopWords.ForLanguage("xx"));
    }

    /// <summary>
    /// Verifies the English Stemmer: Known Words scenario.
    /// </summary>
    [Fact(DisplayName = "English Stemmer: Known Words")]
    public void EnglishStemmer_KnownWords()
    {
        var stemmer = new EnglishStemmer();
        Assert.Equal("connect", stemmer.Stem("connected"));
        Assert.Equal("run", stemmer.Stem("running"));
    }

    /// <summary>
    /// Verifies the German Stemmer: Known Words scenario.
    /// </summary>
    [Fact(DisplayName = "German Stemmer: Known Words")]
    public void GermanStemmer_KnownWords()
    {
        var stemmer = new GermanStemmer();
        // "häuser" → umlaut normalised, then "er" suffix removed → "haus"
        Assert.Equal("haus", stemmer.Stem("häuser"));
    }

    /// <summary>
    /// Verifies the French Stemmer: Known Words scenario.
    /// </summary>
    [Fact(DisplayName = "French Stemmer: Known Words")]
    public void FrenchStemmer_KnownWords()
    {
        var stemmer = new FrenchStemmer();
        // "manger" → remove "er" → "mang"
        Assert.Equal("mang", stemmer.Stem("manger"));
    }
}
