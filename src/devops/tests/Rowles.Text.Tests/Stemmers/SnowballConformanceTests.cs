using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Tests.Stemming;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>
/// Issue #122: the built-in language stemmers must produce exactly the official Snowball 3.2.0
/// stems. The pinned word lists are the oracle; the unchanged reference implementation is checked
/// against them first, so a failure here is always a production divergence, never a bad oracle.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Stemmers)]
public sealed class SnowballConformanceTests
{
    public static TheoryData<string> WordListAlgorithms() => new(SnowballFixtures.WordListAlgorithms);

    [Theory]
    [MemberData(nameof(WordListAlgorithms))]
    public void Reference_MatchesOfficialWordList(string algorithm)
    {
        var reference = SnowballReference.Create(algorithm);
        AssertNoMismatches(algorithm, "reference", reference.Stem);
    }

    [Theory]
    [MemberData(nameof(WordListAlgorithms))]
    public void Production_String_MatchesOfficialWordList(string algorithm)
    {
        var stemmer = ProductionStemmers.Create(algorithm);
        AssertNoMismatches(algorithm, "production string", word => ProductionStemmers.StemString(stemmer, word));
    }

    [Theory]
    [MemberData(nameof(WordListAlgorithms))]
    public void Production_Span_MatchesOfficialWordList(string algorithm)
    {
        var stemmer = ProductionStemmers.Create(algorithm);
        var buffer = new char[256];
        AssertNoMismatches(algorithm, "production span", word =>
        {
            int length = stemmer.Stem(word, buffer);
            Assert.True(length >= 0, $"'{word}' did not fit a {buffer.Length}-character buffer.");
            return new string(buffer, 0, length);
        });
    }

    private static void AssertNoMismatches(string algorithm, string route, Func<string, string> stem)
    {
        var vectors = SnowballFixtures.Vectors(algorithm);
        var mismatches = new List<string>();
        int count = 0;
        foreach (var (input, expected) in vectors)
        {
            string actual = stem(input);
            if (string.Equals(actual, expected, StringComparison.Ordinal))
                continue;
            count++;
            if (mismatches.Count < 15)
                mismatches.Add($"{input} -> {actual} (expected {expected})");
        }

        Assert.True(count == 0,
            $"{algorithm} ({route}): {count} of {vectors.Count} words differ from Snowball 3.2.0. First: " +
            string.Join("; ", mismatches));
    }
}

/// <summary>Maps a pinned Snowball algorithm name to its public production stemmer.</summary>
internal static class ProductionStemmers
{
    public static ISpanStemmer Create(string algorithm) => algorithm switch
    {
        "english" => new EnglishStemmer(),
        "french" => new FrenchStemmer(),
        "german" => new GermanStemmer(),
        "spanish" => new SpanishStemmer(),
        "italian" => new ItalianStemmer(),
        "portuguese" => new PortugueseStemmer(),
        "dutch" => new DutchStemmer(),
        "russian" => new RussianStemmer(),
        "arabic" => new ArabicStemmer(),
        _ => throw new ArgumentException($"'{algorithm}' is not a pinned Snowball algorithm.", nameof(algorithm)),
    };

    /// <summary>Calls the public string convenience overload of a built-in stemmer.</summary>
    public static string StemString(ISpanStemmer stemmer, string word) => stemmer switch
    {
        SnowballStemmer snowball => snowball.Stem(word),
        EnglishStemmer english => english.Stem(word),
        ArabicStemmer arabic => arabic.Stem(word),
        _ => throw new ArgumentException($"{stemmer.GetType().Name} has no string overload.", nameof(stemmer)),
    };
}
