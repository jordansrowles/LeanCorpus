using System.Globalization;
using System.Text;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Xunit;

namespace Rowles.Text.Tests;

/// <summary>
/// Pins the analyser output of every language whose stemmer did not change in Sprint 4 PR 1.
/// The snapshot was generated from the code at that PR's start commit; regenerate it only for an
/// intended change, with <c>LEANCORPUS_UPDATE_TEXT_SNAPSHOT=1</c>.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Analysers)]
public sealed class UnchangedLanguageOutputTests
{
    private const string UpdateVariable = "LEANCORPUS_UPDATE_TEXT_SNAPSHOT";

    internal static readonly (string Language, string Sentence)[] Samples =
    [
        ("en", "The running foxes jumped over the lazy dogs in the warm afternoon sun while children watched."),
        ("de", "Die schnellen Füchse sprangen über die faulen Hunde während die Häuser in der Sonne glänzten."),
        ("es", "Los zorros rápidos saltaron sobre los perros perezosos mientras los niños observaban tranquilamente."),
        ("it", "Le volpi veloci saltavano sopra i cani pigri mentre i bambini guardavano dalla finestra."),
        ("pt", "As raposas rápidas saltaram sobre os cães preguiçosos enquanto as crianças observavam pela janela."),
        ("nl", "De snelle vossen sprongen over de luie honden terwijl de kinderen vanuit het raam toekeken."),
        ("ru", "Быстрые лисы прыгали через ленивых собак пока дети смотрели в окно тихим вечером."),
        ("sk", "Velké domy sú velmi pekné"),
        ("zh", "敏捷的狐狸跳过了懒惰的狗孩子们从窗户里观看安静的下午阳光。"),
        ("ja", "素早い狐が怠け者の犬を飛び越え子供たちは窓から静かな午後の日差しを眺めていました。"),
        ("ko", "빠른 여우가 게으른 개를 뛰어넘는 동안 아이들은 창문에서 고요한 오후 햇살을 바라보았습니다.")
    ];

    [Fact(DisplayName = "Unchanged languages: analyser output matches the start-commit snapshot")]
    public void Unchanged_languages_match_the_snapshot()
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

    internal static string Render()
    {
        var builder = new StringBuilder();
        builder.Append("language\tterm\tstart\tend\tpositionIncrement\tpositionLength\n");
        foreach (var (language, sentence) in Samples)
        {
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
                return Path.Combine(current.FullName, "src", "devops", "tests", "Rowles.Text.Tests", "Analysers", "Snapshots", "unchanged-language-output.tsv");
        }

        throw new DirectoryNotFoundException("Could not find the repository root for the Text snapshot.");
    }
}
