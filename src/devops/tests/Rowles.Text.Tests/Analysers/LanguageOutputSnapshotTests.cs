using System.Globalization;
using System.Text;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>
/// Pins the default analyser output of every language, together with its analysis identity.
/// Regenerate it only for an intended change, with <c>LEANCORPUS_UPDATE_TEXT_SNAPSHOT=1</c>; an
/// output change also needs a new identity (see <c>AnalysisIdentityTests</c>). Snowball languages'
/// stems are checked against the reference implementation, so the snapshot never vouches for itself.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Analysers)]
public sealed class LanguageOutputSnapshotTests
{
    private const string UpdateVariable = "LEANCORPUS_UPDATE_TEXT_SNAPSHOT";

    internal static readonly (string Language, string Sentence)[] Samples =
    [
        ("en", "The running foxes jumped over the lazy dogs in the warm afternoon sun while children watched."),
        ("fr", "Les renards rapides sautaient par-dessus les chiens paresseux pendant que les enfants regardaient."),
        ("de", "Die schnellen Füchse sprangen über die faulen Hunde während die Häuser in der Sonne glänzten."),
        ("es", "Los zorros rápidos saltaron sobre los perros perezosos mientras los niños observaban tranquilamente."),
        ("it", "Le volpi veloci saltavano sopra i cani pigri mentre i bambini guardavano dalla finestra."),
        ("pt", "As raposas rápidas saltaram sobre os cães preguiçosos enquanto as crianças observavam pela janela."),
        ("nl", "De snelle vossen sprongen over de luie honden terwijl de kinderen vanuit het raam toekeken."),
        ("ru", "Быстрые лисы прыгали через ленивых собак пока дети смотрели в окно тихим вечером."),
        ("ar", "كان الأطفال يشاهدون الثعالب السريعة وهي تقفز فوق الكلاب الكسولة في المساء."),
        ("sk", "Veľké domy sú veľmi pekné, ale v meste sú staré školy."),
        ("zh", "敏捷的狐狸跳过了懒惰的狗孩子们从窗户里观看安静的下午阳光。"),
        ("ja", "素早い狐が怠け者の犬を飛び越え子供たちは窓から静かな午後の日差しを眺めていました。"),
        ("ko", "빠른 여우가 게으른 개를 뛰어넘는 동안 아이들은 창문에서 고요한 오후 햇살을 바라보았습니다.")
    ];

    [Fact(DisplayName = "Languages: analyser output matches the snapshot")]
    public void Languages_match_the_snapshot()
    {
        string actual = Render();
        string path = SnapshotPath();
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            File.WriteAllText(path, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Assert.True(File.Exists(path), $"Snapshot '{path}' is missing.");
        string expected = File.ReadAllText(path, Encoding.UTF8).ReplaceLineEndings("\n");
        Assert.Equal(expected, actual);
    }

    [Fact(DisplayName = "Languages: Snowball stems match the reference implementation")]
    public void Snowball_stems_match_the_reference()
    {
        foreach (var (language, sentence) in Samples)
        {
            string? algorithm = SnowballAlgorithmFor(language);
            if (algorithm is null)
                continue;

            var unstemmed = new MaterialisingTokenSink();
            new LanguageAnalyser(new Rowles.LeanCorpus.Analysis.Tokenisers.Tokeniser(), StopWords.ForLanguage(language), stemmer: null)
                .Analyse(sentence, unstemmed);
            var stemmed = new MaterialisingTokenSink();
            AnalyserFactory.Create(language).Analyse(sentence, stemmed);

            var reference = Rowles.LeanCorpus.Tests.Stemming.SnowballReference.Create(algorithm);
            Assert.Equal(unstemmed.Tokens.Select(token => reference.Stem(token.Text)), stemmed.Tokens.Select(static token => token.Text));
            Assert.Equal(unstemmed.Tokens.Select(static token => (token.StartOffset, token.EndOffset, token.PositionIncrement)),
                stemmed.Tokens.Select(static token => (token.StartOffset, token.EndOffset, token.PositionIncrement)));
        }
    }

    private static string? SnowballAlgorithmFor(string language) => language switch
    {
        "en" => "english", "fr" => "french", "de" => "german", "es" => "spanish", "it" => "italian",
        "pt" => "portuguese", "nl" => "dutch", "ru" => "russian", "ar" => "arabic",
        _ => null,
    };

    internal static string Render()
    {
        var builder = new StringBuilder();
        builder.Append("language\tterm\tstart\tend\tpositionIncrement\tpositionLength\n");
        foreach (var (language, sentence) in Samples)
        {
            builder.Append("# ").Append(Rowles.LeanCorpus.Analysis.Stemmers.Snowball.AnalysisIdentities.Find(language)).Append('\n');
            var sink = new MaterialisingTokenSink();
            AnalyserFactory.Create(language).Analyse(sentence, sink);
            foreach (var token in sink.Tokens)
            {
                builder.Append(language).Append('\t').Append(token.Text).Append('\t')
                    .Append(token.StartOffset.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(token.EndOffset.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(token.PositionIncrement.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(token.PositionLength.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string SnapshotPath()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, ".config", "devops", "test-suites.json")))
                return Path.Combine(current.FullName, "src", "devops", "tests", "Rowles.Text.Tests", "Analysers", "Snapshots", "language-output.tsv");
        }

        throw new DirectoryNotFoundException("Could not find the repository root for the Text snapshot.");
    }
}
