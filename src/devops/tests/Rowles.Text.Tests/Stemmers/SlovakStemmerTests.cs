namespace Rowles.Text.Tests.Stemmers;

/// <summary>
/// Unit tests for <see cref="SlovakStemmer"/>.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Stemmers)]
public sealed class SlovakStemmerTests
{
    private readonly SlovakStemmer _stemmer = new();

    [Fact(DisplayName = "SlovakStemmer: Stem(string) returns same for short word")]
    public void StemString_ShortWord_ReturnsSame()
    {
        Assert.Equal("dom", _stemmer.Stem("dom"));
        Assert.Equal("pes", _stemmer.Stem("pes"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(Span) returns length for short word")]
    public void StemSpan_ShortWord_ReturnsLength()
    {
        Span<char> buf = stackalloc char[10];
        int len = _stemmer.Stem("dom".AsSpan(), buf);
        Assert.Equal(3, len);
        Assert.Equal("dom", buf[..len].ToString());
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(Span) returns -1 when buffer too small")]
    public void StemSpan_BufferTooSmall_ReturnsMinusOne()
    {
        Span<char> buf = stackalloc char[2];
        int len = _stemmer.Stem("domov".AsSpan(), buf);
        Assert.Equal(-1, len);
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) empty returns empty")]
    public void StemString_Empty_ReturnsEmpty()
    {
        Assert.Equal("", _stemmer.Stem(""));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) strips group 1 suffix ov")]
    public void StemString_StripsGroup1Ov()
    {
        // "domov" (houses, gen. pl.) -> "dom" (house)
        Assert.Equal("dom", _stemmer.Stem("domov"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) strips group 2 suffix ami")]
    public void StemString_StripsGroup2Ami()
    {
        // "zenami" (women, instr. pl.) -> "zen" (woman)
        Assert.Equal("zen", _stemmer.Stem("zenami"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) strips group 2 suffix ami with diacritics")]
    public void StemString_StripsGroup2Ami_Diacritic()
    {
        // "mestami" (cities, instr. pl.) -> "mest" (city)
        Assert.Equal("mest", _stemmer.Stem("mestami"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) strips group 3 suffix u")]
    public void StemString_StripsGroup3U()
    {
        // "zenu" (woman, acc.) -> "zen" (woman)
        Assert.Equal("zen", _stemmer.Stem("zenu"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) strips group 2 suffix ou")]
    public void StemString_StripsGroup2Ou()
    {
        // "zenou" (woman, instr.) -> "zen" (woman)
        Assert.Equal("zen", _stemmer.Stem("zenou"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) handles DTNL restoration")]
    public void StemString_DtnlRestoration()
    {
        // "zeniam" (women, dat. pl.) restores the final n to ň after removing "iam".
        Assert.Equal("zeň", _stemmer.Stem("zeniam"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) unchanged for no matching suffix")]
    public void StemString_NoSuffixMatch_ReturnsSame()
    {
        // "brat" (brother) has no matching suffix or fallback rule.
        Assert.Equal("brat", _stemmer.Stem("brat"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) handles group 1 suffix iach")]
    public void StemString_StripsGroup1Iach()
    {
        // "uliciach" (streets, loc. pl.) -> "ulic" (street)
        Assert.Equal("ulic", _stemmer.Stem("uliciach"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(Span) exact buffer size works")]
    public void StemSpan_ExactSize_Works()
    {
        Span<char> buf = stackalloc char[5];
        int len = _stemmer.Stem("domov".AsSpan(), buf);
        Assert.Equal(3, len);
        Assert.Equal("dom", buf[..len].ToString());
    }

    [Theory(DisplayName = "SlovakStemmer: Stem(string) handles long-vowel rules")]
    [InlineData("kráľ", "kraľ")]
    [InlineData("miest", "mest")]
    [InlineData("vŕch", "vrch")]
    [InlineData("voľb", "volb")]
    [InlineData("kríž", "križ")]
    [InlineData("kľúč", "kľuč")]
    [InlineData("kmeň", "kmen")]
    [InlineData("stôl", "stol")]
    [InlineData("kráľek", "kráľek")]
    public void StemString_LongVowelRules(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) strips group 2 suffix e")]
    public void StemString_StripsGroup2E()
    {
        // "meste" (city, loc.) -> "mest" (suffix "e" in group 2, DTNL: "e" in eiSuffix, root "mest" ends with "t" -> RestoreDtnl -> "mesť")
        Assert.Equal("mesť", _stemmer.Stem("meste"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) handles er to r conversion")]
    public void StemString_ErToR()
    {
        // "majster" (master) -> ends with "er" (post-group fallback) -> "majstr"
        Assert.Equal("majstr", _stemmer.Stem("majster"));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) unchanged for 4-char word with no match")]
    public void StemString_FourChar_NoMatch_ReturnsSame()
    {
        // "brat" (brother) has no matching suffix, no fallback match -> ProcessGentivPlural -> unchanged
        Assert.Equal("brat", _stemmer.Stem("brat"));
    }

    [Theory(DisplayName = "SlovakStemmer: Stem(string) handles representative suffix groups")]
    [InlineData("domovia", "dom")]
    [InlineData("domom", "dom")]
    [InlineData("domach", "dom")]
    [InlineData("zenu", "zen")]
    [InlineData("ženi", "žeň")]
    public void StemString_SuffixGroups(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: Stem(string) restores d t n and l before i or e endings")]
    [InlineData("rade", "raď")]
    [InlineData("meste", "mesť")]
    [InlineData("zeniam", "zeň")]
    [InlineData("kole", "koľ")]
    public void StemString_RestoresDtnl(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: Stem(string) avoids over-stemming consonant roots")]
    [InlineData("psa", "psa")]
    [InlineData("trsa", "trs")]
    [InlineData("plsa", "pls")]
    [InlineData("psra", "psra")]
    public void StemString_OverStemmingGuard(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: Stem(string) handles foreign and ordinary i endings")]
    [InlineData("kocovi", "koci")]
    [InlineData("kocami", "koci")]
    [InlineData("mrazovi", "mrazi")]
    [InlineData("logovi", "logi")]
    [InlineData("domovi", "dom")]
    public void StemString_ForeignIEnding(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: Stem(string) handles fallback suffix rewrites")]
    [InlineData("chlapok", "chlapk")]
    [InlineData("mrazeň", "mrazň")]
    [InlineData("stol", "stl")]
    [InlineData("public", "publik")]
    [InlineData("otec", "otc")]
    [InlineData("centrum", "centr")]
    public void StemString_FallbackSuffixes(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: Stem(string) matches case-insensitive suffixes")]
    [InlineData("DOMOV", "DOM")]
    [InlineData("domOV", "dom")]
    public void StemString_CaseInsensitiveSuffixes(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(string) handles a long input")]
    public void StemString_LongInput_ReducesCorrectly()
    {
        string input = new string('b', 62) + "aov";
        string expected = new string('b', 62) + "a";

        Assert.True(input.Length > 64);
        Assert.Equal(expected, _stemmer.Stem(input));
    }

    [Theory(DisplayName = "SlovakStemmer: Stem(Span) matches Stem(string)")]
    [InlineData("domov", "dom")]
    [InlineData("kráľ", "kraľ")]
    [InlineData("chlapok", "chlapk")]
    [InlineData("meste", "mesť")]
    [InlineData("brat", "brat")]
    public void StemSpan_MatchesString(string word, string expected)
    {
        Span<char> output = stackalloc char[64];

        int length = _stemmer.Stem(word.AsSpan(), output);

        Assert.Equal(expected.Length, length);
        Assert.Equal(expected, output[..length].ToString());
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(Span) accepts empty input and empty output")]
    public void StemSpan_EmptyInput_ReturnsZero()
    {
        int length = _stemmer.Stem(ReadOnlySpan<char>.Empty, Span<char>.Empty);

        Assert.Equal(0, length);
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(Span) returns minus one when long-vowel output does not fit")]
    public void StemSpan_LongVowel_BufferTooSmall_ReturnsMinusOne()
    {
        Span<char> output = stackalloc char[3];

        int length = _stemmer.Stem("kráľ".AsSpan(), output);

        Assert.Equal(-1, length);
    }

    /// <summary>
    /// Pins <c>rowles-analysis/sk/0.1.0</c> on the 43 Wikimedia comparison cases
    /// (<c>fixtures/slovak/NOTICE.md</c>). The two implementations differ on 19 of them; that
    /// difference is recorded, not asserted as conformance in either direction.
    /// </summary>
    [Fact(DisplayName = "SlovakStemmer: Wikimedia comparison cases keep the 0.1.0 output")]
    public void WikimediaCases_KeepTheCurrentOutput()
    {
        string path = Path.Combine(Rowles.LeanCorpus.Tests.Shared.Fixtures.RepositoryTemporaryStorage.RepositoryRoot,
            "src", "devops", "tests", "fixtures", "slovak", "wikimedia-cases.tsv");
        var rows = File.ReadAllLines(path, System.Text.Encoding.UTF8)
            .Where(static line => line.Length > 0 && line[0] != '#')
            .Select(static line => line.Split('\t'))
            .ToArray();
        Assert.Equal(43, rows.Length);

        var changed = rows.Where(row => _stemmer.Stem(row[0]) != row[2]).Select(static row => row[0]).ToArray();
        Assert.Empty(changed);
        Assert.Equal(19, rows.Count(static row => row[1] != row[2]));
    }

    [Theory(DisplayName = "SlovakStemmer: a token that is only a softening suffix does not throw")]
    [InlineData("iach")]
    [InlineData("iami")]
    [InlineData("IACH")]
    [InlineData("Iami")]
    public void StemString_SuffixOnlyToken_DoesNotThrow(string word)
    {
        string stem = _stemmer.Stem(word);

        Assert.NotEmpty(stem);
        Assert.True(stem.Length <= word.Length);
    }

    [Theory(DisplayName = "SlovakStemmer: Stem(Span) returns minus one and leaves a short output untouched on every rule")]
    [InlineData("meste")]
    [InlineData("zeniam")]
    [InlineData("chlapok")]
    [InlineData("centrum")]
    [InlineData("public")]
    [InlineData("kocovi")]
    [InlineData("miest")]
    public void StemSpan_ShortOutput_IsAtomic(string word)
    {
        string stem = _stemmer.Stem(word);
        Span<char> output = stackalloc char[stem.Length + 2];
        output.Fill('#');

        int length = _stemmer.Stem(word.AsSpan(), output[..(stem.Length - 1)]);

        Assert.Equal(-1, length);
        Assert.Equal(new string('#', output.Length), output.ToString());
    }

    [Theory(DisplayName = "SlovakStemmer: Stem(Span) writes a stem into an output of exactly its length")]
    [InlineData("meste")]
    [InlineData("domov")]
    [InlineData("chlapok")]
    public void StemSpan_OutputOfStemLength_Works(string word)
    {
        string stem = _stemmer.Stem(word);
        Span<char> output = stackalloc char[stem.Length];

        int length = _stemmer.Stem(word.AsSpan(), output);

        Assert.Equal(stem, output[..length].ToString());
    }
}
