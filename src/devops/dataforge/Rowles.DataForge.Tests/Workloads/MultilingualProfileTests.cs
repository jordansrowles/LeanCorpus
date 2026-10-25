using Rowles.DataForge;
using Rowles.DataForge.Workloads;

namespace Rowles.DataForge.Tests.Workloads;

public sealed class MultilingualProfileTests
{
    private static readonly string[] ExpectedLanguages =
        ["en", "fr", "de", "es", "it", "pt", "nl", "ru", "ar", "zh", "ja", "ko"];

    private static readonly string[] ExpectedBaseSentences =
    [
        "The running foxes jumped over the lazy dogs in the warm afternoon sun while children watched.",
        "Les enfants regardaient les renards courir sur la colline pendant que le soleil se couchait lentement.",
        "Die schnellen Füchse sprangen über die faulen Hunde während die Häuser in der Sonne glänzten.",
        "Los zorros rápidos saltaron sobre los perros perezosos mientras los niños observaban tranquilamente.",
        "Le volpi veloci saltavano sopra i cani pigri mentre i bambini guardavano dalla finestra.",
        "As raposas rápidas saltaram sobre os cães preguiçosos enquanto as crianças observavam pela janela.",
        "De snelle vossen sprongen over de luie honden terwijl de kinderen vanuit het raam toekeken.",
        "Быстрые лисы прыгали через ленивых собак пока дети смотрели в окно тихим вечером.",
        "قفزت الثعالب السريعة فوق الكلاب الكسولة بينما كان الأطفال يراقبون من النافذة.",
        "敏捷的狐狸跳过了懒惰的狗孩子们从窗户里观看安静的下午阳光。",
        "素早い狐が怠け者の犬を飛び越え子供たちは窓から静かな午後の日差しを眺めていました。",
        "빠른 여우가 게으른 개를 뛰어넘는 동안 아이들은 창문에서 고요한 오후 햇살을 바라보았습니다."
    ];

    [Fact]
    public void Language_set_and_base_sentences_match_the_v1_fixtures()
    {
        Assert.Equal(ExpectedLanguages, RowlesTextMultilingualProfile.Languages);
        var profile = new RowlesTextMultilingualProfile();
        for (var index = 0; index < ExpectedLanguages.Length; index++)
        {
            var language = ExpectedLanguages[index];
            Assert.Equal(ExpectedBaseSentences[index], RowlesTextMultilingualProfile.GetBaseSentence(language));
            var record = profile.Generate(Options(language, 1)).Single();
            Assert.Equal(0, record.Ordinal);
            Assert.Equal("text-" + language + "-00000000", record.Id);
            Assert.Equal(ExpectedBaseSentences[index], record.Text);
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ar")]
    [InlineData("zh")]
    [InlineData("ja")]
    [InlineData("ko")]
    public void Expanded_records_are_prefix_stable_and_valid_unicode(string language)
    {
        var profile = new RowlesTextMultilingualProfile();
        var shortRecords = profile.Generate(Options(language, 32)).ToArray();
        var longPrefix = profile.Generate(Options(language, 256)).Take(32).ToArray();
        Assert.Equal(shortRecords, longPrefix);
        Assert.All(shortRecords.Skip(1), record =>
        {
            Assert.StartsWith(RowlesTextMultilingualProfile.GetBaseSentence(language), record.Text, StringComparison.Ordinal);
            Assert.Contains(record.Id, record.Text, StringComparison.Ordinal);
            Assert.All(record.Text.EnumerateRunes(), static rune => Assert.True(System.Text.Rune.IsValid(rune.Value)));
        });
    }

    [Fact]
    public void Unsupported_languages_and_parameters_are_rejected()
    {
        var profile = new RowlesTextMultilingualProfile();
        Assert.Throws<ArgumentOutOfRangeException>(() => profile.Generate(Options("sv", 1)).ToArray());
        Assert.Throws<ArgumentException>(() => profile.Generate(new DataForgeGenerationOptions(42, 1)).ToArray());
    }

    private static DataForgeGenerationOptions Options(string language, int count) =>
        new(42, count, new Dictionary<string, string>(StringComparer.Ordinal) { ["language"] = language });
}
