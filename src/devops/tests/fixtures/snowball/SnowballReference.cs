namespace Rowles.LeanCorpus.Tests.Stemming;

/// <summary>
/// The unchanged Snowball 3.2.0 reference implementation (namespace <c>Snowball</c>), compiled
/// into the test projects only. It is the oracle that the production ports are compared with.
/// </summary>
internal static class SnowballReference
{
    /// <summary>Creates a new, independently owned reference stemmer for <paramref name="algorithm"/>.</summary>
    public static global::Snowball.Stemmer Create(string algorithm) => algorithm switch
    {
        "english" => new global::Snowball.EnglishStemmer(),
        "french" => new global::Snowball.FrenchStemmer(),
        "german" => new global::Snowball.GermanStemmer(),
        "spanish" => new global::Snowball.SpanishStemmer(),
        "italian" => new global::Snowball.ItalianStemmer(),
        "portuguese" => new global::Snowball.PortugueseStemmer(),
        "dutch" => new global::Snowball.DutchStemmer(),
        "russian" => new global::Snowball.RussianStemmer(),
        "arabic" => new global::Snowball.ArabicStemmer(),
        _ => throw new ArgumentException($"'{algorithm}' is not a pinned Snowball algorithm.", nameof(algorithm)),
    };
}
