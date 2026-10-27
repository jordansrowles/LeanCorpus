namespace Rowles.DataForge.Workloads;

/// <summary>Choices shared by the DataForge CLI and its guided workbench entry point.</summary>
internal static class DataForgeProfileOptions
{
    internal const string StressSearchMode = "search";
    internal const string StressVectorMode = "vector";
    internal const string StressHybridMode = "hybrid";

    internal static readonly string[] StressModes =
        [StressSearchMode, StressVectorMode, StressHybridMode];

    internal static readonly (string Language, string Sentence)[] MultilingualSamples =
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

    internal static IReadOnlyList<string> MultilingualLanguages { get; } =
        MultilingualSamples.Select(static sample => sample.Language).ToArray();
}
