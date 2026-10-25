using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Codecs.Vectors;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Search.Scoring;

namespace Rowles.LeanCorpus.Cli.Creation;

internal sealed class CustomIndexSchema
{
    public int SchemaVersion { get; init; } = 1;
    public bool Compound { get; init; }
    public bool BuildHnsw { get; init; }
    public string VectorQuantisation { get; init; } = "None";
    public IReadOnlyList<CustomIndexSchemaField> Fields { get; init; } = [];
}

internal sealed class CustomIndexSchemaField
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string? Source { get; init; }
    public bool Stored { get; init; } = true;
    public bool? Positions { get; init; }
    public bool DocValues { get; init; } = true;
    public bool Facet { get; init; }
    public bool Hierarchical { get; init; }
    public int Dimensions { get; init; }
    public bool Hnsw { get; init; }
}

internal static partial class IndexCreator
{
    private static readonly JsonSerializerOptions CustomSchemaJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal static IndexCreationResult CreateCustom(string schemaPath, string outputPath, ulong seed, int count)
    {
        if (count is < 1 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(count), "Document count must be between 1 and 1,000,000.");
        string fullSchemaPath = Path.GetFullPath(schemaPath);
        if (!File.Exists(fullSchemaPath))
            throw new FileNotFoundException("Custom index schema was not found.", fullSchemaPath);
        CustomIndexSchema schema = DeserializeCustomSchema(File.ReadAllText(fullSchemaPath));
        ValidateCustomSchema(schema);
        return CreatePresetCore("custom", outputPath, seed, count, 32, "Clustered", "en", recipe: null, customSchema: schema);
    }

    private static CustomIndexSchema DeserializeCustomSchema(string value)
    {
        CustomIndexSchema schema = JsonSerializer.Deserialize<CustomIndexSchema>(value, CustomSchemaJsonOptions)
            ?? throw new InvalidDataException("The custom index schema is empty or invalid.");
        ValidateCustomSchema(schema);
        return schema;
    }

    private static void ValidateCustomSchema(CustomIndexSchema schema)
    {
        if (schema.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported custom schema version {schema.SchemaVersion}.");
        if (schema.Fields.Count is < 1 or > 64)
            throw new InvalidDataException("A custom schema must contain between 1 and 64 fields.");
        if (schema.Fields.Any(static item => string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Type)))
            throw new InvalidDataException("Every custom schema field needs a name and type.");
        if (schema.Fields.Select(static item => item.Name).Distinct(StringComparer.Ordinal).Count() != schema.Fields.Count)
            throw new InvalidDataException("Custom schema field names must be unique.");

        bool hasVector = false;
        foreach (CustomIndexSchemaField field in schema.Fields)
        {
            string type = field.Type.Trim().ToLowerInvariant();
            if (type is not ("text" or "keyword" or "double" or "int64" or "vector" or "geopoint"))
                throw new InvalidDataException($"Custom field '{field.Name}' has unsupported type '{field.Type}'.");
            if (type == "vector")
            {
                hasVector = true;
                if (field.Dimensions is < 2 or > 4_096)
                    throw new InvalidDataException($"Vector field '{field.Name}' needs between 2 and 4,096 dimensions.");
            }
            else if (field.Hnsw)
                throw new InvalidDataException($"HNSW is only valid for vector field '{field.Name}'.");
            if (field.Positions == true && type != "text")
                throw new InvalidDataException($"Positions are only valid for text field '{field.Name}'.");
            if ((field.Facet || field.Hierarchical) && type != "keyword")
                throw new InvalidDataException($"Facet settings are only valid for keyword field '{field.Name}'.");
            if (field.Hierarchical && !field.Facet)
                throw new InvalidDataException($"Hierarchical field '{field.Name}' must also set facet to true.");
        }

        if (!hasVector && (schema.BuildHnsw || !string.Equals(schema.VectorQuantisation, "None", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("HNSW and vector quantisation require at least one vector field.");
        _ = ParseQuantisation(schema.VectorQuantisation);
    }

    private static VectorQuantisation ResolveCustomQuantisation(CustomIndexSchema schema)
        => ParseQuantisation(schema.VectorQuantisation);

    private sealed record CustomProfileSelection(LeanCorpusSearchProfile Profile,
        CustomIndexSchema Schema, IReadOnlyDictionary<string, string> Parameters) : IDataForgeProfileSelection
    {
        public IndexCreationResult Create(string preset, string outputPath, ulong seed, int count, bool compound,
            bool buildHnsw, VectorQuantisation quantisation, IndexWriterConfigOptions indexOptions)
        {
            DataForgeGenerationOptions options = new(seed, count, Parameters);
            return MaterialiseAndBuild(Profile, options, preset, outputPath, compound, buildHnsw, quantisation,
                indexOptions, record => BuildCustomDocument(record, Schema, seed));
        }
    }

    private static LeanDocument BuildCustomDocument(SearchRecord record, CustomIndexSchema schema, ulong seed)
    {
        var document = new LeanDocument();
        bool hasId = schema.Fields.Any(static field => string.Equals(field.Name, "id", StringComparison.Ordinal));
        if (!hasId)
            document.Add(new StringField("id", record.Id));

        foreach (CustomIndexSchemaField field in schema.Fields)
        {
            string type = field.Type.Trim().ToLowerInvariant();
            string source = field.Source ?? DefaultSource(type);
            switch (type)
            {
                case "text":
                {
                    string value = ResolveString(record, source);
                    FieldIndexOptions options = field.Positions != false
                        ? FieldIndexOptions.DocsAndFreqsAndPositions
                        : FieldIndexOptions.DocsAndFreqs;
                    document.Add(new TextField(field.Name, value, field.Stored, boost: 1f,
                        storeDocValues: false, indexOptions: options));
                    break;
                }
                case "keyword":
                {
                    string value = ResolveString(record, source);
                    if (field.Hierarchical)
                    {
                        string[] components = [record.Category, record.Region];
                        FacetPathIndexer.AddToDocument(document, field.Name, new FacetPath(components));
                        if (field.Stored)
                            document.Add(new StoredField(field.Name, value));
                    }
                    else
                    {
                        StringDocValues docValues = field.Facet
                            ? StringDocValues.SortedSet
                            : field.DocValues ? StringDocValues.Sorted : StringDocValues.None;
                        document.Add(new StringField(field.Name, value, field.Stored, 1f, docValues));
                    }
                    break;
                }
                case "double":
                case "int64":
                    document.Add(new NumericField(field.Name, ResolveNumber(record, source), field.Stored,
                        boost: 1f, storeDocValues: field.DocValues));
                    break;
                case "vector":
                {
                    var context = new DataForgeRecordContext(seed, "leancorpus-custom", 1, (ulong)record.Ordinal);
                    var random = context.Random(string.Concat("vector/", field.Name));
                    float[] vector = Enumerable.Range(0, field.Dimensions)
                        .Select(_ => random.NextInt32(-1_000_000, 1_000_001) / 1_000_000f).ToArray();
                    if (vector.All(static component => component == 0f))
                        vector[0] = 1f;
                    document.Add(new VectorField(field.Name, vector));
                    break;
                }
                case "geopoint":
                    document.Add(new GeoPointField(field.Name, record.LatitudeE6 / 1_000_000d,
                        record.LongitudeE6 / 1_000_000d));
                    break;
                default:
                    throw new InvalidDataException($"Unsupported custom field type '{field.Type}'.");
            }
        }
        return document;
    }

    private static string DefaultSource(string type) => type switch
    {
        "text" => "body",
        "keyword" => "category",
        "double" or "int64" => "price",
        "geopoint" => "location",
        _ => ""
    };

    private static string ResolveString(SearchRecord record, string source) => source.Trim().ToLowerInvariant() switch
    {
        "id" => record.Id,
        "title" => record.Title,
        "body" => record.Body,
        "locale" or "language" => record.Locale,
        "archetype" => record.Archetype,
        "category" => record.Category,
        "region" => record.Region,
        "person" or "personname" => record.PersonName,
        "company" => record.Company,
        "email" => record.Email,
        "url" => record.Url,
        "timestamp" => record.TimestampUtc,
        "active" => record.Active ? "true" : "false",
        "location" => string.Concat(record.LatitudeE6 / 1_000_000d, ",", record.LongitudeE6 / 1_000_000d),
        _ => throw new InvalidDataException($"Unknown DataForge string source '{source}'.")
    };

    private static double ResolveNumber(SearchRecord record, string source) => source.Trim().ToLowerInvariant() switch
    {
        "price" => record.PriceMinor,
        "ordinal" => record.Ordinal,
        "latitude" => record.LatitudeE6 / 1_000_000d,
        "longitude" => record.LongitudeE6 / 1_000_000d,
        "active" => record.Active ? 1 : 0,
        _ => throw new InvalidDataException($"Unknown DataForge numeric source '{source}'.")
    };
}
