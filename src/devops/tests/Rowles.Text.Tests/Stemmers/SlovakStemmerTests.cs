namespace Rowles.Text.Tests.Stemmers;

/// <summary>
/// Unit tests for <see cref="SlovakStemmer"/>. Linguistic judgements live in
/// <c>fixtures/slovak/judgements.tsv</c> (<see cref="SlovakJudgementTests"/>); these tests pin rule
/// branches and the span contract with attested forms.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Stemmers)]
public sealed class SlovakStemmerTests
{
    private readonly SlovakStemmer _stemmer = new();

    [Theory(DisplayName = "SlovakStemmer: words under four letters keep their letters")]
    [InlineData("dom", "dom")]
    [InlineData("pes", "pes")]
    [InlineData("psa", "psa")]
    [InlineData("rúk", "rúk")]
    public void StemString_ShortWord_KeepsItsLetters(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: a short word's final soft consonant is written hard")]
    [InlineData("loď", "lod")]
    [InlineData("deň", "den")]
    [InlineData("soľ", "sol")]
    [InlineData("LOĎ", "LOD")]
    public void StemString_ShortWord_HardensItsFinalConsonant(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
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

    [Theory(DisplayName = "SlovakStemmer: case endings are removed from attested noun forms")]
    [InlineData("domov", "dom")]      // dom, G.pl
    [InlineData("domom", "dom")]      // dom, I.sg
    [InlineData("domoch", "dom")]     // dom, L.pl
    [InlineData("ženami", "žen")]     // žena, I.pl
    [InlineData("ženu", "žen")]       // žena, A.sg
    [InlineData("ženou", "žen")]      // žena, I.sg
    [InlineData("mestami", "mest")]   // mesto, I.pl
    [InlineData("uliciach", "ulic")]  // ulica, L.pl
    public void StemString_RemovesCaseEndings(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    /// <summary>
    /// Slovak writes d, t, n and l before e, i, í and ia whether the stem is hard or soft, so the
    /// stem's final consonant is always written hard. The 0.1.0 stemmer softened it after any of
    /// those endings, so "meste" became "mesť" and split from "mesto".
    /// </summary>
    [Theory(DisplayName = "SlovakStemmer: hard and soft stems end in a hard consonant")]
    [InlineData("meste", "mest")]     // mesto, L.sg (hard t)
    [InlineData("vode", "vod")]       // voda, D.sg (hard d)
    [InlineData("žene", "žen")]       // žena, D.sg (hard n)
    [InlineData("škole", "škol")]     // škola, D.sg (hard l)
    [InlineData("rade", "rad")]       // rad, L.sg (hard d)
    [InlineData("kole", "kol")]       // kolo, L.sg (hard l)
    [InlineData("kosti", "kost")]     // kosť, G.sg (soft ť)
    [InlineData("kostiam", "kost")]   // kosť, D.pl
    [InlineData("lode", "lod")]       // loď, G.sg (soft ď)
    [InlineData("dlani", "dlan")]     // dlaň, D.sg (soft ň)
    [InlineData("učiteľa", "učitel")] // učiteľ, G.sg (soft ľ)
    [InlineData("učitelia", "učitel")] // učiteľ, N.pl
    public void StemString_WritesTheFinalConsonantHard(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: upper-case soft consonants are hardened in upper case")]
    [InlineData("KOSTI", "KOST")]
    [InlineData("UČITEĽA", "UČITEL")]
    [InlineData("MESTE", "MEST")]
    public void StemString_UpperCase_HardensInUpperCase(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    /// <summary>
    /// "brat" matches the "-at" ending, and the over-stemming guard keeps it because "br" has no
    /// vowel or syllabic liquid.
    /// </summary>
    [Fact(DisplayName = "SlovakStemmer: the over-stemming guard keeps brat")]
    public void StemString_Brat_IsKeptByTheGuard()
    {
        Assert.Equal("brat", _stemmer.Stem("brat"));
    }

    [Theory(DisplayName = "SlovakStemmer: the over-stemming guard on words of four letters or more")]
    [InlineData("psom", "psom")]      // pes, I.sg: "ps" has no vowel or syllabic liquid, so the ending stays
    [InlineData("psov", "psov")]      // pes, G.pl
    [InlineData("vlkom", "vlk")]      // vlk, I.sg: syllabic l inside the root allows the cut
    [InlineData("krvou", "krv")]      // krv, I.sg: syllabic r inside the root
    [InlineData("stĺpa", "stĺp")]     // stĺp, G.sg: syllabic ĺ inside the root (0.1.0 listed í instead)
    [InlineData("stĺpom", "stĺp")]    // stĺp, I.sg
    public void StemString_OverStemmingGuard(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: genitive plural long vowels are shortened")]
    [InlineData("miest", "mest")]     // mesto, G.pl
    [InlineData("žien", "žen")]       // žena, G.pl
    [InlineData("škôl", "škol")]      // škola, G.pl
    [InlineData("kníh", "knih")]      // kniha, G.pl
    [InlineData("ciest", "cest")]     // cesta, G.pl
    [InlineData("vŕch", "vrch")]
    [InlineData("kríž", "križ")]
    [InlineData("kľúč", "kľuč")]
    [InlineData("stôl", "stol")]      // joins stola, stole, stolom
    [InlineData("MIEST", "MEST")]     // upper-case long vowels are located as well as found
    [InlineData("ŠKÔL", "ŠKOL")]
    public void StemString_LongVowelRules(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    /// <summary>
    /// The rule cannot tell a genitive plural from a nominative with a long vowel, so "kráľ" is
    /// shortened while "kráľa" is not. This split, and "sud"/"súd" staying apart, are pinned until
    /// the length rules are revisited (deferred P1 work, see the judgement fixture).
    /// </summary>
    [Theory(DisplayName = "SlovakStemmer: known long-vowel limits are pinned")]
    [InlineData("kráľ", "kral")]
    [InlineData("kráľa", "král")]
    [InlineData("súdu", "súd")]
    [InlineData("sudu", "sud")]
    public void StemString_LongVowelLimits(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: -ovi and -mi after c, z or g keep the family together")]
    [InlineData("otcovi", "otc")]     // otec, D.sg; joins otca, otcom, otec
    [InlineData("chlapcovi", "chlapc")]
    [InlineData("mesiacmi", "mesiac")]
    [InlineData("logovi", "log")]     // logo, D.sg is not a word, but logu and logom give log
    [InlineData("domovi", "dom")]
    public void StemString_IEndingsAfterCzg(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: -omat nouns keep -at")]
    [InlineData("automat", "automat")]
    [InlineData("automatu", "automat")]
    [InlineData("automatmi", "automat")]
    [InlineData("bankomat", "bankomat")]
    [InlineData("bankomate", "bankomat")]
    [InlineData("dievčat", "dievč")]  // dievča, G.pl still loses -at
    public void StemString_OmatNouns_KeepAt(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: fallback rewrites for a fleeting vowel")]
    [InlineData("chlapok", "chlapk")]
    [InlineData("výsledok", "výsledk")]  // joins výsledku, výsledky
    [InlineData("majster", "majstr")]    // joins majstra
    [InlineData("orol", "orl")]          // joins orla
    [InlineData("otec", "otc")]          // joins otca
    [InlineData("mrazeň", "mrazn")]
    [InlineData("public", "publik")]
    [InlineData("centrum", "centr")]     // joins centra
    public void StemString_FallbackSuffixes(string word, string expected)
    {
        Assert.Equal(expected, _stemmer.Stem(word));
    }

    [Theory(DisplayName = "SlovakStemmer: fallback rewrites have nearby words they must keep apart")]
    [InlineData("výsledok", "výsledný")]
    [InlineData("majster", "majstrovský")]
    [InlineData("centrum", "centrála")]
    public void StemString_FallbackSuffixes_KeepNeighboursApart(string word, string neighbour)
    {
        Assert.NotEqual(_stemmer.Stem(word), _stemmer.Stem(neighbour));
    }

    [Theory(DisplayName = "SlovakStemmer: matches case-insensitive suffixes")]
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

    [Theory(DisplayName = "SlovakStemmer: a token that is only a suffix does not throw")]
    [InlineData("iach")]
    [InlineData("iami")]
    [InlineData("IACH")]
    [InlineData("Iami")]
    [InlineData("encami")]
    [InlineData("atami")]
    public void StemString_SuffixOnlyToken_DoesNotThrow(string word)
    {
        string stem = _stemmer.Stem(word);

        Assert.NotEmpty(stem);
        Assert.True(stem.Length <= word.Length);
    }

    /// <summary>
    /// Every capacity from zero to the word's length plus two: a capacity below the stem's length
    /// returns -1 and leaves every character untouched; any other writes exactly the stem.
    /// </summary>
    [Theory(DisplayName = "SlovakStemmer: Stem(Span) honours every output capacity")]
    [InlineData("domov")]
    [InlineData("meste")]
    [InlineData("kosť")]
    [InlineData("zeniam")]
    [InlineData("chlapok")]
    [InlineData("centrum")]
    [InlineData("public")]
    [InlineData("otcovi")]
    [InlineData("miest")]
    [InlineData("psom")]
    [InlineData("iach")]
    public void StemSpan_EveryCapacity(string word)
    {
        string stem = _stemmer.Stem(word);
        for (int capacity = 0; capacity <= word.Length + 2; capacity++)
        {
            var output = new char[capacity + 4];
            Array.Fill(output, '#');
            int length = _stemmer.Stem(word.AsSpan(), output.AsSpan(0, capacity));
            if (capacity < stem.Length)
            {
                Assert.Equal(-1, length);
                Assert.All(output, static c => Assert.Equal('#', c));
            }
            else
            {
                Assert.Equal(stem, new string(output, 0, length));
                Assert.All(output.Skip(length), static c => Assert.Equal('#', c));
            }
        }
    }

    [Theory(DisplayName = "SlovakStemmer: words around the 64-character scratch size stem the same on both paths")]
    [InlineData(60)]
    [InlineData(63)]
    [InlineData(64)]
    [InlineData(65)]
    [InlineData(66)]
    [InlineData(200)]
    public void StemSpan_LengthsAroundTheScratchSize(int length)
    {
        foreach (string ending in new[] { "ami", "ove", "ťou", "iach", "om" })
        {
            string word = new string('k', length - ending.Length - 2) + "ra" + ending;
            string stem = _stemmer.Stem(word);
            var large = new char[word.Length];
            var exact = new char[stem.Length];
            Assert.Equal(stem, new string(large, 0, _stemmer.Stem(word, large)));
            Assert.Equal(stem, new string(exact, 0, _stemmer.Stem(word, exact)));
            if (stem.Length > 0)
                Assert.Equal(-1, _stemmer.Stem(word, new char[stem.Length - 1]));
        }
    }

    [Fact(DisplayName = "SlovakStemmer: string and span entry points agree on seeded mixed-script words")]
    public void StemSpan_MatchesString_OnSeededWords()
    {
        const string alphabet = "aáäbcčdďeéfghiíjklĺľmnňoóôpqrŕsštťuúvwxyýzžAÁČĎÉĽŇÔŠŤÚÝŽxyαβγабв日本";
        string[] endings = ["", "a", "e", "i", "ia", "iach", "iami", "ami", "om", "ov", "och", "ou", "u", "y", "ý", "ého", "ému", "ovi", "mi", "ok", "ec", "ol", "er", "um", "at", "omat", "ič"];
        var random = new Random(4021); // NOSONAR: S2245, a seeded generator makes the differential words reproducible; nothing here is security-sensitive.
        var output = new char[128];
        for (int i = 0; i < 20_000; i++)
        {
            var root = new char[random.Next(0, 12)];
            for (int j = 0; j < root.Length; j++)
                root[j] = alphabet[random.Next(alphabet.Length)];
            string word = new string(root) + endings[random.Next(endings.Length)];

            string expected = _stemmer.Stem(word);
            int length = _stemmer.Stem(word.AsSpan(), output);
            Assert.True(length >= 0 && length <= word.Length, word);
            Assert.Equal(expected, new string(output, 0, length));
        }
    }

    [Fact(DisplayName = "SlovakStemmer: Stem(Span) allocates nothing in the steady state")]
    public void StemSpan_SteadyState_DoesNotAllocate()
    {
        string[] words = ["mestami", "kostiach", "učiteľmi", "škôl", "automatoch", "otcovi", "výsledok", "centrum", "príčinách", "dlaňami", new string('k', 70) + "ami"];
        var buffer = new char[128];
        foreach (var word in words)
            _stemmer.Stem(word, buffer);

        var passes = new List<long>();
        for (int pass = 0; pass < 5; pass++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1_000; i++)
            {
                foreach (var word in words)
                    _stemmer.Stem(word, buffer);
            }
            passes.Add(GC.GetAllocatedBytesForCurrentThread() - before);
            if (passes[^1] == 0)
                return;
        }

        Assert.Fail($"Every pass allocated ({string.Join(", ", passes)} bytes).");
    }

    [Fact(DisplayName = "SlovakStemmer: one shared instance gives the same stems on many threads")]
    public void SharedInstance_IsThreadSafe()
    {
        string[] words = ["mestami", "meste", "kosti", "učitelia", "škôl", "automat", "otcovi", "chlapok", "príčinách", "dlani", "psom", "stĺpa"];
        var expected = words.Select(new SlovakStemmer().Stem).ToArray();

        Parallel.For(0, 64, new ParallelOptions { MaxDegreeOfParallelism = 8 }, _ =>
        {
            var buffer = new char[64];
            for (int round = 0; round < 500; round++)
            {
                for (int i = 0; i < words.Length; i++)
                {
                    int length = _stemmer.Stem(words[i].AsSpan(), buffer);
                    Assert.Equal(expected[i], new string(buffer, 0, length));
                }
            }
        });
    }

    /// <summary>
    /// The 43 Wikimedia comparison cases (<c>fixtures/slovak/NOTICE.md</c>), pinned at the current
    /// identity. The two implementations differ on 20 of them; that difference is recorded, not
    /// asserted as conformance in either direction.
    /// </summary>
    [Fact(DisplayName = "SlovakStemmer: Wikimedia comparison cases keep the recorded output")]
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
        Assert.Equal(20, rows.Count(static row => row[1] != row[2]));
    }
}
