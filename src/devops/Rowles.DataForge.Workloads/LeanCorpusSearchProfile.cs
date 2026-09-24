using System.Globalization;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public sealed record SearchRecord(
    long Ordinal,
    string Id,
    string Locale,
    string Archetype,
    string Title,
    string Body,
    string Category,
    string Region,
    string PersonName,
    string Company,
    string Email,
    string Url,
    string TimestampUtc,
    long PriceMinor,
    bool Active,
    int LatitudeE6,
    int LongitudeE6);

public sealed class LeanCorpusSearchProfile : IDataForgeGeneratedProfile<SearchRecord>
{
    private const int BasisPointRange = 10_000;
    private const string ReservedNoHitTerm = "zzzznomatch";
    private static readonly DateTimeOffset StartTimestamp = new(2020, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly long TimestampRangeSeconds = (long)(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) - StartTimestamp).TotalSeconds;

    private static readonly (string Value, int Weight)[] Locales =
    [
        ("en_GB", 8_200),
        ("en", 800),
        ("fr", 300),
        ("de", 250),
        ("es", 200),
        ("nl", 100),
        ("pt_BR", 50),
        ("ru", 50),
        ("ja", 25),
        ("ar", 25)
    ];

    private static readonly (string Value, int Weight)[] Archetypes =
    [
        ("article", 3_000),
        ("product", 2_000),
        ("technical", 1_500),
        ("forum", 1_500),
        ("contact", 1_000),
        ("business", 1_000)
    ];

    private static readonly (string Label, string Text, int Threshold)[] TermAnchors =
    [
        ("workload/term/said", "said", 3_500),
        ("workload/term/government", "government", 1_500),
        ("workload/term/people", "people", 750),
        ("workload/term/market", "market", 250),
        ("workload/term/rare", "quasarneedle", 10)
    ];

    private static readonly (string Label, string Text, int Threshold)[] PhraseAnchors =
    [
        ("workload/phrase/new-york", "new york", 500),
        ("workload/phrase/new-york-stock", "new york stock", 100),
        ("workload/phrase/said-government-slop", "said public government", 300)
    ];

    private static readonly (string Label, int Threshold, string[] Variants)[] LexicalFamilies =
    [
        ("workload/family/governance", 500, ["governments", "governmental", "governance", "governor", "governing"]),
        ("workload/family/market", 500, ["markets", "marketed", "marketing", "marketplace", "marker", "marked"]),
        ("workload/family/president", 500, ["president", "presidents", "presidential", "presidency", "present", "presentation"]),
        ("workload/family/nation", 300, ["nation", "national", "nationally", "international", "internationally"])
    ];

    private static readonly string[] SuggesterTerms =
    [
        "government", "president", "market", "company", "million", "financial", "reported", "political", "economic", "hospital",
        "computer", "network", "message", "article", "because", "people", "national", "through", "without", "believe"
    ];

    private static readonly IReadOnlyList<DataForgeDependencyVersion> ProfileDependencies =
    [new DataForgeDependencyVersion("Bogus", "35.6.5")];

    public DataForgeProfileDescriptor Descriptor { get; } = new(
        "leancorpus-search", 1, "Generated", 42, 20_000,
        "Deterministic text and structured records for LeanCorpus benchmark workloads.");

    public IDataForgeCanonicalRecordWriter<SearchRecord> CanonicalRecordWriter { get; } = new SearchRecordWriter();

    public IReadOnlyList<DataForgeDependencyVersion> Dependencies => ProfileDependencies;

    public static IReadOnlyList<string> LocaleIds { get; } = Locales.Select(static item => item.Value).ToArray();

    public static IReadOnlyList<string> ArchetypeIds { get; } = Archetypes.Select(static item => item.Value).ToArray();

    public static IReadOnlyList<string> RequiredLexicalTokens { get; } =
        LexicalFamilies.SelectMany(static family => family.Variants).Concat(SuggesterTerms).Distinct(StringComparer.Ordinal).ToArray();

    public IEnumerable<SearchRecord> Generate(DataForgeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var fakersByLocale = new Dictionary<string, DataForgeFaker>(StringComparer.Ordinal);
        for (var ordinal = 0; ordinal < options.RecordCount; ordinal++)
            yield return GenerateRecord(options, ordinal, fakersByLocale);
    }

    public IReadOnlyList<DataForgeSummary> Summarise(DataForgeGenerationOptions options) =>
    [
        new DataForgeSummary("profileId", Descriptor.ProfileId),
        new DataForgeSummary("profileVersion", Descriptor.ProfileVersion.ToString(CultureInfo.InvariantCulture)),
        new DataForgeSummary("recordCount", options.RecordCount.ToString(CultureInfo.InvariantCulture)),
        new DataForgeSummary("seed", options.Seed.ToString(CultureInfo.InvariantCulture))
    ];

    private static SearchRecord GenerateRecord(
        DataForgeGenerationOptions options,
        int ordinal,
        Dictionary<string, DataForgeFaker> fakersByLocale)
    {
        var context = new DataForgeRecordContext(options.Seed, "leancorpus-search", 1, (ulong)ordinal);
        var locale = SelectWeighted(context.Random("locale"), Locales);
        var archetype = SelectWeighted(context.Random("archetype"), Archetypes);
        var targetTokenCount = SelectTargetTokenCount(context.Random("length"));

        if (!fakersByLocale.TryGetValue(locale, out var faker))
        {
            faker = DataForgeFakerFactory.Create(locale, context.Seed("structured/name"));
            fakersByLocale.Add(locale, faker);
        }
        else
        {
            faker.ResetRandomiser(context.Seed("structured/name"));
        }

        var personName = string.Concat(faker.FirstName(), " ", faker.LastName());
        faker.ResetRandomiser(context.Seed("structured/company"));
        var company = faker.CompanyName();
        faker.ResetRandomiser(context.Seed("structured/contact"));
        var email = faker.Email();
        var url = faker.Url();
        faker.ResetRandomiser(context.Seed("structured/address"));
        var addressCity = faker.City();

        var title = BuildTitle(archetype, personName, company, addressCity, faker);
        var body = BuildBody(context, faker, archetype, targetTokenCount);
        var timestamp = StartTimestamp.AddSeconds(context.Random("structured/time").NextInt64(TimestampRangeSeconds));
        var priceMinor = context.Random("structured/price").NextInt32(100, 999_901);
        var category = string.Concat("category-", context.Random("structured/category").NextInt32(32).ToString("D2", CultureInfo.InvariantCulture));
        var region = string.Concat("region-", context.Random("structured/region").NextInt32(16).ToString("D2", CultureInfo.InvariantCulture));
        var active = context.Random("structured/active").NextInt32(BasisPointRange) < 7_000;
        var geoRandom = context.Random("structured/geo");
        var latitude = geoRandom.NextInt32(-90_000_000, 90_000_001);
        var longitude = geoRandom.NextInt32(-180_000_000, 180_000_001);

        return new SearchRecord(
            ordinal,
            string.Concat("search-", ordinal.ToString("D8", CultureInfo.InvariantCulture)),
            locale,
            archetype,
            title,
            body,
            category,
            region,
            personName,
            company,
            email,
            url,
            timestamp.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
            priceMinor,
            active,
            latitude,
            longitude);
    }

    private static string BuildTitle(string archetype, string personName, string company, string addressCity, DataForgeFaker faker) =>
        archetype switch
        {
            "article" => string.Concat(company, " ", faker.JobTitle()),
            "product" => faker.ProductName(),
            "technical" => string.Concat(faker.JobTitle(), " at ", company),
            "forum" => string.Concat(personName, " discussion"),
            "contact" => string.Concat(personName, " ", addressCity),
            "business" => string.Concat(company, " update"),
            _ => throw new InvalidOperationException($"Unknown search archetype '{archetype}'.")
        };

    private static string BuildBody(DataForgeRecordContext context, DataForgeFaker faker, string archetype, int targetTokenCount)
    {
        var anchorBlocks = new List<string> { archetype };
        foreach (var anchor in TermAnchors)
        {
            if (context.Random(anchor.Label).NextInt32(BasisPointRange) < anchor.Threshold)
                anchorBlocks.Add(anchor.Text);
        }
        foreach (var anchor in PhraseAnchors)
        {
            if (context.Random(anchor.Label).NextInt32(BasisPointRange) < anchor.Threshold)
                anchorBlocks.Add(anchor.Text);
        }
        foreach (var family in LexicalFamilies)
        {
            var familyRandom = context.Random(family.Label);
            if (familyRandom.NextInt32(BasisPointRange) < family.Threshold)
                anchorBlocks.Add(family.Variants[familyRandom.NextInt32(family.Variants.Length)]);
        }
        for (var termIndex = 0; termIndex < SuggesterTerms.Length; termIndex++)
        {
            var term = SuggesterTerms[termIndex];
            var label = string.Concat("workload/suggester/", term);
            if (context.RecordOrdinal == (ulong)termIndex ||
                context.Random(label).NextInt32(BasisPointRange) < 200)
                anchorBlocks.Add(term);
        }

        var anchorTokenCount = anchorBlocks.Sum(CountTokens);
        var fillerWordCount = Math.Max(1, targetTokenCount - anchorTokenCount);
        faker.ResetRandomiser(context.Seed("content/filler"));
        var fillerWords = faker.LoremWords(fillerWordCount)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(static word => string.Equals(word, ReservedNoHitTerm, StringComparison.OrdinalIgnoreCase) ? "neutral" : word)
            .ToList();
        if (fillerWords.Count == 0)
            fillerWords.Add("neutral");

        var placement = context.Random("workload/placement");
        foreach (var block in anchorBlocks)
        {
            var slot = placement.NextInt32(fillerWords.Count + 1);
            fillerWords.Insert(slot, block);
        }
        return string.Join(' ', fillerWords);
    }

    private static int SelectTargetTokenCount(DataForgePrng random)
    {
        var roll = random.NextInt32(BasisPointRange);
        if (roll < 1_500)
            return random.NextInt32(10, 41);
        if (roll < 8_500)
            return random.NextInt32(50, 151);
        if (roll < 9_900)
            return random.NextInt32(150, 501);
        return random.NextInt32(500, 1_501);
    }

    private static string SelectWeighted(DataForgePrng random, IReadOnlyList<(string Value, int Weight)> choices)
    {
        var roll = random.NextInt32(BasisPointRange);
        var cumulative = 0;
        foreach (var choice in choices)
        {
            cumulative += choice.Weight;
            if (roll < cumulative)
                return choice.Value;
        }
        throw new InvalidOperationException("Weighted choices do not cover the full basis-point range.");
    }

    private static int CountTokens(string block) => block.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private sealed class SearchRecordWriter : IDataForgeCanonicalRecordWriter<SearchRecord>
    {
        public void Write(CanonicalJsonWriter writer, SearchRecord record)
        {
            writer.WriteStartObject();
            Property(writer, "ordinal", record.Ordinal);
            StringProperty(writer, "id", record.Id);
            StringProperty(writer, "locale", record.Locale);
            StringProperty(writer, "archetype", record.Archetype);
            StringProperty(writer, "title", record.Title);
            StringProperty(writer, "body", record.Body);
            StringProperty(writer, "category", record.Category);
            StringProperty(writer, "region", record.Region);
            StringProperty(writer, "personName", record.PersonName);
            StringProperty(writer, "company", record.Company);
            StringProperty(writer, "email", record.Email);
            StringProperty(writer, "url", record.Url);
            StringProperty(writer, "timestampUtc", record.TimestampUtc);
            Property(writer, "priceMinor", record.PriceMinor);
            writer.WritePropertyName("active");
            writer.WriteBooleanValue(record.Active);
            Property(writer, "latitudeE6", record.LatitudeE6);
            Property(writer, "longitudeE6", record.LongitudeE6);
            writer.WriteEndObject();
        }

        private static void StringProperty(CanonicalJsonWriter writer, string name, string value)
        {
            writer.WritePropertyName(name);
            writer.WriteStringValue(value);
        }

        private static void Property(CanonicalJsonWriter writer, string name, int value)
        {
            writer.WritePropertyName(name);
            writer.WriteInt32Value(value);
        }

        private static void Property(CanonicalJsonWriter writer, string name, long value)
        {
            writer.WritePropertyName(name);
            writer.WriteInt64Value(value);
        }
    }
}
