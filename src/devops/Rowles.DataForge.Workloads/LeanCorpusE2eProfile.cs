using System.Globalization;
using Rowles.DataForge;

namespace Rowles.DataForge.Workloads;

public sealed record E2eRecord(
    long Ordinal,
    string Id,
    string Title,
    string Body,
    string Category,
    long Year,
    long PriceMinor,
    bool Active);

public sealed class LeanCorpusE2eProfile : IDataForgeGeneratedProfile<E2eRecord>
{
    private readonly LeanCorpusSearchProfile _search = new();

    public DataForgeProfileDescriptor Descriptor { get; } = new(
        "leancorpus-e2e", 1, "Generated", 42, 256,
        "Deterministic reserved scenarios and background documents for real Server integration tests.");

    public IDataForgeCanonicalRecordWriter<E2eRecord> CanonicalRecordWriter { get; } = new RecordWriter();

    public IReadOnlyList<DataForgeDependencyVersion> Dependencies => _search.Dependencies;

    public IEnumerable<E2eRecord> Generate(DataForgeGenerationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.RecordCount < 32)
            throw new ArgumentOutOfRangeException(nameof(options), "The E2E profile requires at least 32 records.");
        if (options.Parameters.Count != 0)
            throw new ArgumentException("The E2E profile does not accept parameters.", nameof(options));

        foreach (var source in _search.Generate(options))
        {
            if (source.Ordinal < 8)
                yield return Reserved((int)source.Ordinal);
            else
                yield return new E2eRecord(source.Ordinal,
                    "e2e-" + source.Ordinal.ToString("D8", CultureInfo.InvariantCulture),
                    source.Title,
                    source.Body.Replace("forgeexactanchor", "neutralanchor", StringComparison.OrdinalIgnoreCase),
                    source.Category,
                    long.Parse(source.TimestampUtc.AsSpan(0, 4), CultureInfo.InvariantCulture),
                    source.PriceMinor,
                    source.Active);
        }
    }

    public IReadOnlyList<DataForgeSummary> Summarise(DataForgeGenerationOptions options) =>
    [
        new("reservedRecordCount", "8"),
        new("backgroundRecordCount", (options.RecordCount - 8).ToString(CultureInfo.InvariantCulture))
    ];

    private static E2eRecord Reserved(int ordinal) => ordinal switch
    {
        0 => new(0, "e2e-exact", "Exact anchor document",
            "The forgeexactanchor identifies this document in the deterministic corpus.",
            "scenario-exact", 2024, 100, true),
        1 => new(1, "e2e-phrase", "Phrase query document",
            "This document demonstrates deterministic corpus replay through the search API.",
            "scenario-phrase", 2024, 200, true),
        2 => new(2, "e2e-filter", "Filtered document",
            "A filtered document remains visible when its category is selected.",
            "scenario-filter", 2025, 300, true),
        3 => new(3, "e2e-unicode", "Unicode round trip",
            "café naïve Ελληνικά 日本語 العربية",
            "scenario-unicode", 2025, 400, true),
        4 => new(4, "e2e-long", "Long document",
            string.Join(' ', Enumerable.Repeat("indexing", 1_100)),
            "scenario-long", 2023, 500, true),
        5 => new(5, "e2e-product", "Compact search appliance",
            "A compact product catalogue entry for a durable indexing appliance.",
            "product", 2025, 12_345, true),
        6 => new(6, "e2e-contact", "Willow Data contact",
            "Contact Willow Data Ltd for business enquiries and support.",
            "contact", 2024, 0, true),
        7 => new(7, "e2e-technical", "Technical indexing note",
            "The async await span memory indexing path keeps work bounded.",
            "technical", 2025, 0, true),
        _ => throw new ArgumentOutOfRangeException(nameof(ordinal))
    };

    private sealed class RecordWriter : IDataForgeCanonicalRecordWriter<E2eRecord>
    {
        public void Write(CanonicalJsonWriter writer, E2eRecord record)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("ordinal");
            writer.WriteInt64Value(record.Ordinal);
            writer.WritePropertyName("id");
            writer.WriteStringValue(record.Id);
            writer.WritePropertyName("title");
            writer.WriteStringValue(record.Title);
            writer.WritePropertyName("body");
            writer.WriteStringValue(record.Body);
            writer.WritePropertyName("category");
            writer.WriteStringValue(record.Category);
            writer.WritePropertyName("year");
            writer.WriteInt64Value(record.Year);
            writer.WritePropertyName("priceMinor");
            writer.WriteInt64Value(record.PriceMinor);
            writer.WritePropertyName("active");
            writer.WriteBooleanValue(record.Active);
            writer.WriteEndObject();
        }
    }
}
