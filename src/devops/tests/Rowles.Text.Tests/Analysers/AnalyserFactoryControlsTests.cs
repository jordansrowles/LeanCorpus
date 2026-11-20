using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Analysis.Filters;
using Rowles.LeanCorpus.Analysis.Stemmers;
using Rowles.LeanCorpus.Analysis.Tokenisers;
using Rowles.LeanCorpus.Tests.Shared.Infrastructure;
using Rowles.LeanCorpus.Tests.Stemming;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>
/// Issue #123: the factory's language pipelines can keep their tokeniser and stop words while the
/// stemmer is replaced or removed, and each default pipeline has an identity that changes exactly
/// when its output can change.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Analysers)]
public sealed class AnalyserFactoryControlsTests
{
    private const string FingerprintUpdateVariable = "LEANCORPUS_UPDATE_ANALYSIS_FINGERPRINTS";

    private static readonly string[] SnowballLanguages = ["en", "fr", "de", "es", "it", "pt", "nl", "ru", "ar"];

    public static TheoryData<string> Languages() => new(AnalyserFactory.SupportedLanguages);

    [Fact]
    public void NullStemmer_KeepsTheGermanPipelineWithoutStemming()
    {
        var tokens = Analyse(AnalyserFactory.Create("de", stemmer: null), "Tomaten und Zwiebeln");

        Assert.Equal(["tomaten", "zwiebeln"], tokens.Select(static token => token.Text));
        Assert.Equal([0, 12], tokens.Select(static token => token.StartOffset));
        Assert.Equal([7, 20], tokens.Select(static token => token.EndOffset));
        // "und" is a German stop word, so "zwiebeln" carries its position gap.
        Assert.Equal([1, 2], tokens.Select(static token => token.PositionIncrement));

        Assert.Equal(["tomat", "zwiebel"], Analyse(AnalyserFactory.Create("de"), "Tomaten und Zwiebeln").Select(static token => token.Text));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void DefaultStemmerOverload_MatchesTheOneArgumentPipeline(string language)
    {
        ISpanStemmer? stemmer = DefaultStemmer(language);
        foreach (var (code, sentence) in LanguageOutputSnapshotTests.Samples.Where(sample => sample.Language == language))
        {
            Assert.Equal(
                Analyse(AnalyserFactory.Create(code), sentence).Select(Describe),
                Analyse(AnalyserFactory.Create(code, stemmer), sentence).Select(Describe));
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void NullStemmer_KeepsTheLanguageTokeniserAndStopWords(string language)
    {
        foreach (var (code, sentence) in LanguageOutputSnapshotTests.Samples.Where(sample => sample.Language == language))
        {
            var unstemmed = Analyse(AnalyserFactory.Create(code, stemmer: null), sentence);
            var expected = Analyse(new LanguageAnalyser(Tokeniser(code), StopWords.ForLanguage(code), stemmer: null), sentence);
            Assert.Equal(expected.Select(Describe), unstemmed.Select(Describe));

            if (DefaultStemmer(code) is null)
                Assert.Equal(Analyse(AnalyserFactory.Create(code), sentence).Select(Describe), unstemmed.Select(Describe));
        }
    }

    [Fact]
    public void CustomShareableStemmer_IsUsedForAnyLanguage()
    {
        var upper = new UpperCaseStemmer();
        Assert.Equal(["TOMATEN"], Analyse(AnalyserFactory.Create("de", upper), "Tomaten").Select(static t => t.Text));
        Assert.Equal(["CATS"], Analyse(AnalyserFactory.Create("en-GB", upper), "cats").Select(static t => t.Text));

        var analyser = (IThreadLocalAnalyser)AnalyserFactory.Create("fr", upper);
        Assert.Equal(["CHATS"], Analyse(analyser.CreateThreadLocalAnalyser(), "chats").Select(static t => t.Text));
    }

    [Fact]
    public void CustomThreadLocalStemmer_IsClonedPerOwner()
    {
        var stemmer = new CountingStemmer();
        var analyser = (IThreadLocalAnalyser)AnalyserFactory.Create("es", stemmer);
        var owned = analyser.CreateThreadLocalAnalyser();
        Assert.Equal(1, stemmer.Clones);

        Assert.Equal(["gato", "perro"], Analyse(owned, "gato perro").Select(static t => t.Text));
        Assert.Equal(["casa"], Analyse(analyser, "casa").Select(static t => t.Text));

        // Every analysis runs on an owned copy, so the configured instance is never called.
        Assert.Equal(0, stemmer.Calls);
        Assert.True(stemmer.Clones >= 2);
    }

    [Fact]
    public void CustomStemmerWithoutOwnership_IsRejectedOnUse()
    {
        // As with LanguageAnalyser, creation succeeds but analysis needs an ownership contract.
        var analyser = (IThreadLocalAnalyser)AnalyserFactory.Create("it", new UnownedStemmer());

        var analyse = Assert.Throws<InvalidOperationException>(() => Analyse(analyser, "Cane"));
        Assert.Contains(nameof(UnownedStemmer), analyse.Message, StringComparison.Ordinal);
        var copy = Assert.Throws<InvalidOperationException>(() => analyser.CreateThreadLocalAnalyser());
        Assert.Contains(nameof(UnownedStemmer), copy.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    public void NullLanguage_Throws(string? language)
    {
        Assert.Throws<ArgumentNullException>(() => AnalyserFactory.Create(language!, stemmer: null));
        Assert.Throws<ArgumentNullException>(() => AnalyserFactory.GetDefaultAnalysisId(language!));
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("")]
    [InlineData("klingon")]
    public void UnknownLanguage_Throws(string language)
    {
        Assert.Throws<NotSupportedException>(() => AnalyserFactory.Create(language, stemmer: null));
        Assert.Throws<NotSupportedException>(() => AnalyserFactory.GetDefaultAnalysisId(language));
    }

    [Fact]
    public void DefaultIdentities_MatchTheAgreedTable()
    {
        foreach (var code in SnowballLanguages)
            Assert.Equal($"rowles-analysis/{code}/3.2.0", AnalyserFactory.GetDefaultAnalysisId(code));
        foreach (var code in new[] { "sk", "zh", "ja", "ko" })
            Assert.Equal($"rowles-analysis/{code}/0.1.0", AnalyserFactory.GetDefaultAnalysisId(code));

        Assert.Equal(AnalyserFactory.GetDefaultAnalysisId("de"), AnalyserFactory.GetDefaultAnalysisId("de-DE"));
        Assert.Equal(AnalyserFactory.GetDefaultAnalysisId("pt"), AnalyserFactory.GetDefaultAnalysisId("PT-br"));
        Assert.Equal(AnalyserFactory.GetDefaultAnalysisId("zh"), AnalyserFactory.GetDefaultAnalysisId("zh-Hans"));
        Assert.Equal(AnalyserFactory.SupportedLanguages.Count, AnalyserFactory.SupportedLanguages.Select(AnalyserFactory.GetDefaultAnalysisId).Distinct().Count());
    }

    [Fact]
    public void DefaultIdentities_MatchTheCommittedManifest()
    {
        var manifest = ReadVersions();
        Assert.Equal(AnalyserFactory.SupportedLanguages.Order(StringComparer.Ordinal), manifest.Keys.Order(StringComparer.Ordinal));
        foreach (var (code, entry) in manifest)
            Assert.Equal($"rowles-analysis/{code}/{entry.Version}", AnalyserFactory.GetDefaultAnalysisId(code));
    }

    /// <summary>
    /// Guards the identity rule: a default pipeline's output may change only together with its
    /// identity. A mismatch means the output changed; record it with a new identity through
    /// <c>./devops snowball generate --accept-fingerprints</c>.
    /// </summary>
    [Fact]
    public void DefaultPipelineOutput_MatchesItsRecordedFingerprint()
    {
        var actual = AnalyserFactory.SupportedLanguages.ToDictionary(static code => code, Fingerprint, StringComparer.Ordinal);
        if (Environment.GetEnvironmentVariable(FingerprintUpdateVariable) == "1")
        {
            string path = Path.Combine(Rowles.LeanCorpus.Tests.Shared.Fixtures.RepositoryTemporaryStorage.RepositoryRoot, "artifacts", "snowball", "pending-fingerprints.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }

        var manifest = ReadVersions();
        var failures = new List<string>();
        foreach (var (code, fingerprint) in actual)
        {
            var entry = manifest[code];
            if (entry.Fingerprint is null || !string.Equals(entry.FingerprintVersion, entry.Version, StringComparison.Ordinal))
                failures.Add($"{code}: no fingerprint is recorded for {entry.Version}");
            else if (!string.Equals(entry.Fingerprint, fingerprint, StringComparison.Ordinal))
                failures.Add($"{code}: the default output changed but its identity is still {entry.Version}");
        }

        Assert.True(failures.Count == 0,
            string.Join("; ", failures) + $". Run the text tests with {FingerprintUpdateVariable}=1, then " +
            "'./devops snowball generate --accept-fingerprints' (with --rebaseline or --bump if the identity must change).");
    }

    [Fact]
    public void EnglishDefaults_UsePorter2_AndPorterStaysExplicit()
    {
        // Words that Porter (1980) and Porter2 stem differently; expected values from the Snowball reference.
        var porter2 = SnowballReference.Create("english");
        string[] words = ["generously", "dying", "skies", "news", "cosmos", "communism", "inning", "proceed", "fluently", "gently"];
        var expected = words.Select(porter2.Stem).ToArray();
        var legacy = words.Select(new PorterStemmer().Stem).ToArray();
        Assert.NotEqual(expected, legacy);

        Assert.Equal(expected, words.Select(new EnglishStemmer().Stem));
        Assert.Equal(expected, Analyse(AnalyserFactory.Create("en"), string.Join(' ', words)).Select(static t => t.Text));
        Assert.Equal(expected, Analyse(new StemmedAnalyser(), string.Join(' ', words)).Select(static t => t.Text));
        Assert.Equal(expected, Analyse(new StemmedAnalyser(keywordMarker: null), string.Join(' ', words)).Select(static t => t.Text));
        Assert.Equal(legacy, Analyse(StemmerAnalyser.Porter(), string.Join(' ', words)).Select(static t => t.Text));
        Assert.Equal(legacy, words.Select(new PorterStemmer().Stem));
    }

    [Fact]
    public void StemmedAnalyser_KeywordMarker_StillBypassesStemming()
    {
        var keywords = new KeywordMarkerFilter(["running"]);
        var tokens = Analyse(new StemmedAnalyser(keywords), "Running runners ran");

        Assert.Equal(["running", SnowballReference.Create("english").Stem("runners"), "ran"], tokens.Select(static t => t.Text));
        Assert.Equal([0, 8, 16], tokens.Select(static t => t.StartOffset));
    }

    [Fact]
    public void GermanContractedPrepositions_AreStopWords()
    {
        Assert.Empty(Analyse(AnalyserFactory.Create("de"), "vom zum beim ans ins aufs"));
        Assert.Empty(Analyse(AnalyserFactory.Create("de", stemmer: null), "Vom zum beim ans ins aufs"));

        var tokens = Analyse(AnalyserFactory.Create("de"), "Suppe vom Kürbis");
        Assert.Equal(["supp", "kurbis"], tokens.Select(static t => t.Text));
        Assert.Equal([0, 10], tokens.Select(static t => t.StartOffset));
        Assert.Equal([5, 16], tokens.Select(static t => t.EndOffset));
        Assert.Equal([1, 2], tokens.Select(static t => t.PositionIncrement));
    }

    [Fact]
    public void OnlyTheGermanStopWordsChanged()
    {
        string[] added = ["ans", "aufs", "ins", "vom"];
        Assert.All(added, word => Assert.Contains(word, StopWords.German));
        foreach (var code in AnalyserFactory.SupportedLanguages.Where(static code => code != "de"))
            Assert.All(added.Where(static word => word != "ins"), word => Assert.DoesNotContain(word, StopWords.ForLanguage(code) ?? []));
    }

    /// <summary>The documented language table must name every language, its default stemmer and identity.</summary>
    [Fact]
    public void DocumentedLanguageTable_MatchesTheFactory()
    {
        string path = Path.Combine(Rowles.LeanCorpus.Tests.Shared.Fixtures.RepositoryTemporaryStorage.RepositoryRoot,
            "docs", "content", "analysis", "01-analysers.md");
        string page = File.ReadAllText(path, Encoding.UTF8);
        int start = page.IndexOf("<!-- analysis-languages:start -->", StringComparison.Ordinal);
        int end = page.IndexOf("<!-- analysis-languages:end -->", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "01-analysers.md has lost its analysis-languages markers.");

        var rows = page[start..end].Split('\n')
            .Where(static line => line.StartsWith("| `", StringComparison.Ordinal))
            .Select(static line => line.Split('|', StringSplitOptions.TrimEntries))
            .ToDictionary(static cells => cells[1].Trim('`'), static cells => cells, StringComparer.Ordinal);

        Assert.Equal(AnalyserFactory.SupportedLanguages.Order(StringComparer.Ordinal), rows.Keys.Order(StringComparer.Ordinal));
        foreach (var (code, cells) in rows)
        {
            Assert.Equal($"`{AnalyserFactory.GetDefaultAnalysisId(code)}`", cells[4]);
            string expectedStemmer = DefaultStemmer(code)?.GetType().Name ?? "None";
            Assert.True(cells[3].StartsWith(expectedStemmer == "None" ? "None" : $"`{expectedStemmer}`", StringComparison.Ordinal),
                $"{code}: documented stemmer '{cells[3]}', factory uses {expectedStemmer}.");
            Assert.Equal($"`{Tokeniser(code).GetType().Name}`", cells[2]);
        }
    }

    private static string Fingerprint(string code)
    {
        var analyser = AnalyserFactory.Create(code);
        var builder = new StringBuilder();
        foreach (var text in FingerprintCorpus(code))
        {
            var sink = new MaterialisingTokenSink();
            analyser.Analyse(text, sink);
            foreach (var token in sink.Tokens)
                builder.Append(Describe(token)).Append('\n');
            builder.Append("--\n");
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
    }

    /// <summary>Sample sentences, every stop word, and words from the pinned lists and a seeded generator.</summary>
    private static IEnumerable<string> FingerprintCorpus(string code)
    {
        foreach (var (language, sentence) in LanguageOutputSnapshotTests.Samples)
        {
            if (language == code)
                yield return sentence;
        }

        yield return string.Join(' ', StopWords.ForLanguage(code) ?? []);
        yield return "vom zum beim ans ins aufs Straße ßßßß Tomaten Zwiebeln Kürbisse";

        string? algorithm = SnowballAlgorithm(code);
        if (algorithm is not null && SnowballFixtures.VendoredAlgorithms.Concat(["arabic"]).Contains(algorithm))
        {
            foreach (var chunk in SnowballFixtures.Vectors(algorithm).Select(static pair => pair.Input).Chunk(500))
                yield return string.Join(' ', chunk);
        }

        string alphabet = code switch
        {
            "ru" => "абвгдеёжзийклмнопрстуфхцчшщъыьэюя",
            "ar" => "ءآأؤإئابةتثجحخدذرزسشصضطظعغفقكلمنهوىي",
            "zh" => "的一是不了人我在有他这中大来上国个到说们为子和你地出道也时年",
            "ja" => "あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをんアイウエオカキクケコ日本語",
            "ko" => "가나다라마바사아자차카타파하고노도로모보소오조초코토포호",
            _ => "abcdefghijklmnopqrstuvwxyzàâäçéèêëîïôöùûüßñáíóúãõ'",
        };
        var random = new Random(code.Aggregate(123, static (seed, ch) => unchecked((seed * 31) + ch))); // NOSONAR: deterministic corpus, not security.
        for (int line = 0; line < 50; line++)
        {
            var words = new string[100];
            for (int i = 0; i < words.Length; i++)
                words[i] = new string(Enumerable.Range(0, random.Next(1, 14)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            yield return string.Join(' ', words);
        }
    }

    private static string? SnowballAlgorithm(string code) => code switch
    {
        "en" => "english", "fr" => "french", "de" => "german", "es" => "spanish", "it" => "italian",
        "pt" => "portuguese", "nl" => "dutch", "ru" => "russian", "ar" => "arabic",
        _ => null,
    };

    private static Dictionary<string, VersionEntry> ReadVersions()
    {
        string path = Path.Combine(Rowles.LeanCorpus.Tests.Shared.Fixtures.RepositoryTemporaryStorage.RepositoryRoot,
            "src", "core", "Rowles.Text", "Analysis", "Stemmers", "Snowball", "analysis-versions.json");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var result = new Dictionary<string, VersionEntry>(StringComparer.Ordinal);
        foreach (var language in document.RootElement.GetProperty("languages").EnumerateArray())
        {
            result[language.GetProperty("code").GetString()!] = new VersionEntry(
                language.GetProperty("version").GetString()!,
                language.TryGetProperty("fingerprint", out var fingerprint) ? fingerprint.GetString() : null,
                language.TryGetProperty("fingerprintVersion", out var version) ? version.GetString() : null);
        }
        return result;
    }

    private static ISpanStemmer? DefaultStemmer(string code) => code switch
    {
        "en" => new EnglishStemmer(),
        "fr" => new FrenchStemmer(),
        "de" => new GermanStemmer(),
        "es" => new SpanishStemmer(),
        "it" => new ItalianStemmer(),
        "pt" => new PortugueseStemmer(),
        "nl" => new DutchStemmer(),
        "ru" => new RussianStemmer(),
        "ar" => new ArabicStemmer(),
        "sk" => new SlovakStemmer(),
        _ => null,
    };

    private static ISpanTokeniser Tokeniser(string code) => code switch
    {
        "zh" => new ChineseLexiconTokeniser(ChineseLexicon.Default),
        "ja" => new JapaneseTokeniser(),
        "ko" => new CJKBigramTokeniser(),
        _ => new Tokeniser(),
    };

    private static List<Token> Analyse(IAnalyser analyser, string text)
    {
        var sink = new MaterialisingTokenSink();
        analyser.Analyse(text, sink);
        return sink.Tokens;
    }

    private static string Describe(Token token) => string.Join('\t',
        token.Text,
        token.StartOffset.ToString(CultureInfo.InvariantCulture),
        token.EndOffset.ToString(CultureInfo.InvariantCulture),
        token.PositionIncrement.ToString(CultureInfo.InvariantCulture),
        token.PositionLength.ToString(CultureInfo.InvariantCulture));

    private sealed record VersionEntry(string Version, string? Fingerprint, string? FingerprintVersion);

    private sealed class UpperCaseStemmer : IShareableSpanStemmer
    {
        public int Stem(ReadOnlySpan<char> word, Span<char> output)
        {
            if (output.Length < word.Length)
                return -1;
            word.ToUpperInvariant(output);
            return word.Length;
        }
    }

    private sealed class CountingStemmer : IThreadLocalSpanStemmer
    {
        public int Clones { get; private set; }
        public int Calls { get; private set; }
        public CountingStemmer? LastClone { get; private set; }

        public ISpanStemmer CreateThreadLocalStemmer()
        {
            Clones++;
            return LastClone = new CountingStemmer();
        }

        public int Stem(ReadOnlySpan<char> word, Span<char> output)
        {
            Calls++;
            if (output.Length < word.Length)
                return -1;
            word.CopyTo(output);
            return word.Length;
        }
    }

    private sealed class UnownedStemmer : ISpanStemmer
    {
        public int Stem(ReadOnlySpan<char> word, Span<char> output)
        {
            if (output.Length < word.Length)
                return -1;
            word.CopyTo(output);
            return word.Length;
        }
    }
}
