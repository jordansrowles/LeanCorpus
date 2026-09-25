using System.Globalization;
using System.Text;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public sealed record MultilingualTextRecord(long Ordinal, string Id, string Language, string Text);

public sealed class RowlesTextMultilingualProfile : IDataForgeGeneratedProfile<MultilingualTextRecord>
{
    private const string ProfileId = "rowles-text-multilingual";
    private static readonly (string Language, string Sentence)[] BaseSamples =
    [
        ("en", "The running foxes jumped over the lazy dogs in the warm afternoon sun while children watched."),
        ("fr", "Les enfants regardaient les renards courir sur la colline pendant que le soleil se couchait lentement."),
        ("de", "Die schnellen Füchse sprangen über die faulen Hunde während die Häuser in der Sonne glänzten."),
        ("es", "Los zorros rápidos saltaron sobre los perros perezosos mientras los niños observaban tranquilamente."),
        ("it", "Le volpi veloci saltavano sopra i cani pigri mentre i bambini guardavano dalla finestra."),
        ("pt", "As raposas rápidas saltaram sobre os cães preguiçosos enquanto as crianças observavam pela janela."),
        ("nl", "De snelle vossen sprongen over de luie honden terwijl de kinderen vanuit het raam toekeken."),
        ("ru", "Быстрые лисы прыгали через ленивых собак пока дети смотрели в окно тихим вечером."),
        ("ar", "قفزت الثعالب السريعة فوق الكلاب الكسولة بينما كان الأطفال يراقبون من النافذة."),
        ("zh", "敏捷的狐狸跳过了懒惰的狗孩子们从窗户里观看安静的下午阳光。"),
        ("ja", "素早い狐が怠け者の犬を飛び越え子供たちは窓から静かな午後の日差しを眺めていました。"),
        ("ko", "빠른 여우가 게으른 개를 뛰어넘는 동안 아이들은 창문에서 고요한 오후 햇살을 바라보았습니다.")
    ];

    public DataForgeProfileDescriptor Descriptor { get; } = new(
        ProfileId, 1, "Generated", 42, 256,
        "Deterministic multilingual analyser inputs from twelve fixed language samples.");

    public IDataForgeCanonicalRecordWriter<MultilingualTextRecord> CanonicalRecordWriter { get; } = new RecordWriter();

    public IReadOnlyList<DataForgeDependencyVersion> Dependencies { get; } = [];

    public static IReadOnlyList<string> Languages { get; } = BaseSamples.Select(static item => item.Language).ToArray();

    public static string GetBaseSentence(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        foreach (var sample in BaseSamples)
            if (string.Equals(sample.Language, language, StringComparison.Ordinal))
                return sample.Sentence;
        throw new ArgumentOutOfRangeException(nameof(language), $"Unsupported multilingual language '{language}'.");
    }

    public IEnumerable<MultilingualTextRecord> Generate(DataForgeGenerationOptions options)
    {
        var language = ParseLanguage(options);
        var sentence = GetBaseSentence(language);
        var runes = sentence.EnumerateRunes().ToArray();
        for (var ordinal = 0; ordinal < options.RecordCount; ordinal++)
        {
            var id = string.Concat("text-", language, "-", ordinal.ToString("D8", CultureInfo.InvariantCulture));
            var text = ordinal == 0 ? sentence : Compose(options.Seed, ordinal, language, sentence, runes, id);
            yield return new MultilingualTextRecord(ordinal, id, language, text);
        }
    }

    public IReadOnlyList<DataForgeSummary> Summarise(DataForgeGenerationOptions options) =>
    [
        new("language", ParseLanguage(options)),
        new("baseSentence", GetBaseSentence(ParseLanguage(options))),
        new("recordCount", options.RecordCount.ToString(CultureInfo.InvariantCulture))
    ];

    private static string Compose(ulong seed, int ordinal, string language, string sentence, Rune[] runes, string id)
    {
        var context = new DataForgeRecordContext(seed, ProfileId, 1, (ulong)ordinal);
        var random = context.Random(string.Concat("text/rotation/", language));
        var offset = random.NextInt32(runes.Length);
        var punctuation = ".:;!?"[context.Random(string.Concat("text/punctuation/", language)).NextInt32(5)];
        var marker = context.Random(string.Concat("text/identifier/", language)).NextUInt64().ToString("x16", CultureInfo.InvariantCulture);
        var result = new StringBuilder(sentence.Length * 2 + 48);
        result.Append(sentence).Append(' ').Append(punctuation).Append(' ').Append(id).Append('-').Append(marker).Append(' ');
        for (var index = 0; index < runes.Length; index++)
            result.Append(runes[(offset + index) % runes.Length]);
        return result.ToString();
    }

    private static string ParseLanguage(DataForgeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        foreach (var key in options.Parameters.Keys)
            if (!string.Equals(key, "language", StringComparison.Ordinal))
                throw new ArgumentException($"Unknown multilingual parameter '{key}'.", nameof(options));
        var language = options.Parameters.GetValueOrDefault("language")
            ?? throw new ArgumentException("The multilingual language parameter is required.", nameof(options));
        _ = GetBaseSentence(language);
        return language;
    }

    private sealed class RecordWriter : IDataForgeCanonicalRecordWriter<MultilingualTextRecord>
    {
        public void Write(CanonicalJsonWriter writer, MultilingualTextRecord record)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("ordinal");
            writer.WriteInt64Value(record.Ordinal);
            writer.WritePropertyName("id");
            writer.WriteStringValue(record.Id);
            writer.WritePropertyName("language");
            writer.WriteStringValue(record.Language);
            writer.WritePropertyName("text");
            writer.WriteStringValue(record.Text);
            writer.WriteEndObject();
        }
    }
}
