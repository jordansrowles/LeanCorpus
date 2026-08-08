namespace Rowles.LeanCorpus.Analysis.Analysers;

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
    /// Chinese uses lexicon segmentation, Japanese uses dictionary-backed
    /// Viterbi segmentation, and Korean keeps Hangul word runs intact. These
    /// languages skip stemming. For Arabic, upstream hamza normalisation is
    /// recommended before analysis.
    /// </remarks>
    public static IAnalyser Create(string languageCode)
    {
        ArgumentNullException.ThrowIfNull(languageCode);

        // Strip region and script subtags before selecting the analyser.
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
            "zh" => new LanguageAnalyser(new ChineseLexiconTokeniser(ChineseLexicon.Default), StopWords.Chinese, stemmer: null),
            "ja" => new LanguageAnalyser(new JapaneseTokeniser(), StopWords.Japanese, stemmer: null),
            "ko" => new LanguageAnalyser(new CJKBigramTokeniser(), StopWords.Korean, stemmer: null),
            "sk" => new LanguageAnalyser(new Tokeniser(), StopWords.Slovak, new SlovakStemmer()),
            _ => throw new NotSupportedException(
                $"Language '{languageCode}' is not supported. Supported: {string.Join(", ", SupportedLanguages)}.")
        };
    }

    /// <summary>
    /// Returns all supported BCP 47 primary language subtags.
    /// </summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } =
    [
        "en", "fr", "de", "es", "it", "pt", "nl", "ru", "ar", "zh", "ja", "ko", "sk"
    ];
}
