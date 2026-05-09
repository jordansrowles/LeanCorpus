namespace Rowles.LeanLucene.Analysis.Analysers;

/// <summary>
/// Factory for creating language-specific analysers.
/// </summary>
public static class AnalyserFactory
{
    /// <summary>
    /// Creates an analyser configured for the specified language.
    /// </summary>
    /// <param name="languageCode">
    /// A BCP 47 language tag. The primary subtag is used; region and script
    /// subtags are stripped (so <c>"pt-BR"</c> resolves to Portuguese,
    /// <c>"zh-Hans"</c> to Chinese, <c>"en-GB"</c> to English).
    /// Supported primary subtags: en, fr, de, es, it, pt, nl, ru, ar, zh, ja, ko.
    /// </param>
    /// <returns>A configured analyser for the language.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="languageCode"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">Thrown for unsupported language codes.</exception>
    /// <remarks>
    /// CJK languages (zh, ja, ko) use <see cref="CJKBigramTokeniser"/> and skip stemming;
    /// suffix-stripping is linguistically inappropriate for those scripts. For Arabic,
    /// upstream hamza normalisation (أ إ آ → ا) is recommended before analysis.
    /// </remarks>
    public static IAnalyser Create(string languageCode)
    {
        ArgumentNullException.ThrowIfNull(languageCode);

        // Normalise: strip region/script subtag so "pt-BR", "zh-Hans", etc. resolve cleanly.
        var tag = languageCode.Split('-')[0].ToLowerInvariant();

        return tag switch
        {
            "en" => new LanguageAnalyser(new Tokeniser(), StopWords.English, new EnglishStemmer()),
            "fr" => new LanguageAnalyser(new Tokeniser(), StopWords.French, new FrenchStemmer()),
            "de" => new LanguageAnalyser(new Tokeniser(), StopWords.German, new GermanStemmer()),
            "es" => new LanguageAnalyser(new Tokeniser(), StopWords.Spanish, new SpanishStemmer()),
            "it" => new LanguageAnalyser(new Tokeniser(), StopWords.Italian, new ItalianStemmer()),
            "pt" => new LanguageAnalyser(new Tokeniser(), StopWords.Portuguese, new PortugueseStemmer()),
            "nl" => new LanguageAnalyser(new Tokeniser(), StopWords.Dutch, new DutchStemmer()),
            "ru" => new LanguageAnalyser(new Tokeniser(), StopWords.Russian, new RussianStemmer()),
            "ar" => new LanguageAnalyser(new Tokeniser(), StopWords.Arabic, new ArabicStemmer()),
            "zh" => new LanguageAnalyser(new CJKBigramTokeniser(), StopWords.Chinese, stemmer: null),
            "ja" => new LanguageAnalyser(new CJKBigramTokeniser(), StopWords.Japanese, stemmer: null),
            "ko" => new LanguageAnalyser(new CJKBigramTokeniser(), StopWords.Korean, stemmer: null),
            _ => throw new NotSupportedException(
                $"Language '{languageCode}' is not supported. Supported: {string.Join(", ", SupportedLanguages)}.")
        };
    }

    /// <summary>
    /// Returns all supported BCP 47 primary language subtags.
    /// </summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } =
    [
        "en", "fr", "de", "es", "it", "pt", "nl", "ru", "ar", "zh", "ja", "ko"
    ];
}
