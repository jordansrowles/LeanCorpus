using Rowles.LeanCorpus.Analysis.Stemmers.Snowball;

namespace Rowles.LeanCorpus.Analysis.Analysers;

/// <summary>
/// Factory for creating language-specific analysers.
/// </summary>
public static class AnalyserFactory
{
    /// <summary>
    /// Creates an explicitly disposable Japanese analyser that owns a custom dictionary.
    /// </summary>
    /// <param name="dictionaryPath">Path to a versioned <c>.jlc</c> file.</param>
    /// <returns>An analyser and owner that should be disposed after all its calls complete.</returns>
    public static OwnedJapaneseAnalyser CreateOwnedJapaneseAnalyser(string dictionaryPath)
        => new(dictionaryPath);

    /// <summary>
    /// Creates the default analyser for the specified language.
    /// </summary>
    /// <param name="languageCode">
    /// A BCP 47 language tag. The primary subtag is used; region and script
    /// subtags are stripped (so <c>"pt-BR"</c> resolves to Portuguese,
    /// <c>"zh-Hans"</c> to Chinese, <c>"en-GB"</c> to English).
    /// Supported primary subtags: en, fr, de, es, it, pt, nl, ru, ar, sk, zh, ja, ko.
    /// </param>
    /// <returns>A configured analyser for the language.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="languageCode"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">Thrown for unsupported language codes.</exception>
    /// <remarks>
    /// <para>Every pipeline tokenises, lower-cases and removes the language's stop words. Ten languages
    /// then stem: en, fr, de, es, it, pt, nl, ru and ar use the official Snowball 3.2.0 algorithms
    /// (English is Porter2), and sk uses <see cref="SlovakStemmer"/>. Chinese uses lexicon
    /// segmentation, Japanese uses dictionary-backed Viterbi segmentation, and Korean keeps Hangul word
    /// runs intact; these three do not stem. For Arabic, upstream hamza normalisation is recommended.</para>
    /// <para>To keep a language's tokeniser and stop words but change or disable stemming, use
    /// <see cref="Create(string, ISpanStemmer?)"/>. <see cref="GetDefaultAnalysisId"/> identifies the
    /// default pipeline, so callers can tell when a reindex is needed.</para>
    /// </remarks>
    public static IAnalyser Create(string languageCode)
    {
        var language = Resolve(languageCode);
        return new LanguageAnalyser(language.Tokeniser(), language.StopWords, language.DefaultStemmer(), keywordMarker: null, IdentityOf(language));
    }

    /// <summary>
    /// Creates the analyser for the specified language with an explicit stemmer, or no stemming.
    /// </summary>
    /// <param name="languageCode">A BCP 47 language tag, resolved as for <see cref="Create(string)"/>.</param>
    /// <param name="stemmer">
    /// The stemmer to apply after stop-word removal, or <see langword="null"/> to disable stemming.
    /// The language's tokeniser, lower-casing and stop words are kept either way.
    /// </param>
    /// <returns>A configured analyser for the language.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="languageCode"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">Thrown for unsupported language codes.</exception>
    /// <remarks>
    /// <para>The supplied stemmer is used as given, for every language. The pipeline takes an owned
    /// copy for each analysis and for <see cref="IThreadLocalAnalyser.CreateThreadLocalAnalyser"/>: an
    /// <see cref="IShareableSpanStemmer"/> is shared and an <see cref="IThreadLocalSpanStemmer"/> is
    /// cloned. Any other stemmer makes analysis throw <see cref="InvalidOperationException"/>, as with
    /// <see cref="LanguageAnalyser"/>, so implement one of those two interfaces.</para>
    /// <para>The result has no built-in analysis identity: <see cref="GetDefaultAnalysisId"/>
    /// describes only the <see cref="Create(string)"/> pipeline, so callers own the identity of a
    /// custom stemmer.</para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // German tokenisation and stop words, without stemming:
    /// var exact = AnalyserFactory.Create("de", stemmer: null);
    /// </code>
    /// </example>
    public static IAnalyser Create(string languageCode, ISpanStemmer? stemmer)
        => Build(Resolve(languageCode), stemmer);

    /// <summary>
    /// Returns the identity of the default analysis pipeline for a language, such as
    /// <c>rowles-analysis/de/3.2.0</c>.
    /// </summary>
    /// <param name="languageCode">A BCP 47 language tag, resolved as for <see cref="Create(string)"/>.</param>
    /// <returns>The identity of the <see cref="Create(string)"/> pipeline for the language.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="languageCode"/> is <see langword="null"/>.</exception>
    /// <exception cref="NotSupportedException">Thrown for unsupported language codes.</exception>
    /// <remarks>
    /// <para>The identity changes exactly when the default pipeline's output can change, so a field
    /// indexed under one identity must be reindexed when the identity changes. The analyser that
    /// <see cref="Create(string)"/> returns reports the same identity through
    /// <see cref="IAnalysisIdentityProvider"/>, and an index records it for every field the analyser
    /// writes and refuses a different one. Compare identities only for equality; they are opaque.</para>
    /// <para>Snowball languages carry the Snowball version at which their output last changed
    /// (<c>3.2.0</c>, with <c>+rN</c> for later LeanCorpus-only changes); other languages carry a
    /// LeanCorpus <c>0.x.y</c> version, numbered after their earlier output eras. Releases before 4.0
    /// reported no identities, and their output matches none of these: 4.0 also keeps position gaps
    /// where stop words are removed. The identity describes only the built-in default pipeline.</para>
    /// </remarks>
    public static string GetDefaultAnalysisId(string languageCode) => IdentityOf(Resolve(languageCode));

    private static string IdentityOf(LanguageDefinition language)
        => AnalysisIdentities.Find(language.Tag)
            ?? throw new InvalidOperationException($"No analysis identity is defined for '{language.Tag}'.");

    private static IAnalyser Build(LanguageDefinition language, ISpanStemmer? stemmer)
        => new LanguageAnalyser(language.Tokeniser(), language.StopWords, stemmer);

    private static LanguageDefinition Resolve(string languageCode)
    {
        ArgumentNullException.ThrowIfNull(languageCode);

        // Strip region and script subtags before selecting the analyser.
        var tag = languageCode.Split('-')[0].ToLowerInvariant();

        return tag switch
        {
            "en" => new(tag, static () => new Tokeniser(), StopWords.English, static () => new EnglishStemmer()),
            "fr" => new(tag, static () => new Tokeniser(), StopWords.French, static () => new FrenchStemmer()),
            "de" => new(tag, static () => new Tokeniser(), StopWords.German, static () => new GermanStemmer()),
            "es" => new(tag, static () => new Tokeniser(), StopWords.Spanish, static () => new SpanishStemmer()),
            "it" => new(tag, static () => new Tokeniser(), StopWords.Italian, static () => new ItalianStemmer()),
            "pt" => new(tag, static () => new Tokeniser(), StopWords.Portuguese, static () => new PortugueseStemmer()),
            "nl" => new(tag, static () => new Tokeniser(), StopWords.Dutch, static () => new DutchStemmer()),
            "ru" => new(tag, static () => new Tokeniser(), StopWords.Russian, static () => new RussianStemmer()),
            "ar" => new(tag, static () => new Tokeniser(), StopWords.Arabic, static () => new ArabicStemmer()),
            "zh" => new(tag, static () => new ChineseLexiconTokeniser(ChineseLexicon.Default), StopWords.Chinese, static () => null),
            "ja" => new(tag, static () => new JapaneseTokeniser(), StopWords.Japanese, static () => null),
            "ko" => new(tag, static () => new CJKBigramTokeniser(), StopWords.Korean, static () => null),
            "sk" => new(tag, static () => new Tokeniser(), StopWords.Slovak, static () => new SlovakStemmer()),
            _ => throw new NotSupportedException(
                $"Language '{languageCode}' is not supported. Supported: {string.Join(", ", SupportedLanguages)}.")
        };
    }

    /// <summary>One language's default pipeline parts.</summary>
    private sealed record LanguageDefinition(
        string Tag,
        Func<ISpanTokeniser> Tokeniser,
        IReadOnlyList<string> StopWords,
        Func<ISpanStemmer?> DefaultStemmer);

    /// <summary>
    /// Returns all supported BCP 47 primary language subtags.
    /// </summary>
    public static IReadOnlyList<string> SupportedLanguages { get; } =
    [
        "en", "fr", "de", "es", "it", "pt", "nl", "ru", "ar", "zh", "ja", "ko", "sk"
    ];
}
