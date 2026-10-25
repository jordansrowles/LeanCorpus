using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.DataForge;
using Rowles.DataForge.Workloads;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Codecs.Vectors;

namespace Rowles.LeanCorpus.Cli.Creation;

internal sealed record IndexRecipe(
    int SchemaVersion,
    string Mode,
    string Preset,
    string ProfileId,
    int ProfileVersion,
    ulong Seed,
    int RecordCount,
    IReadOnlyDictionary<string, string> Parameters,
    bool Compound,
    bool BuildHnsw,
    string VectorQuantisation,
    bool ManySmallSegments,
    bool DeleteHeavy,
    string RecipeIdentity);

internal sealed record IndexCreationResult(string IndexPath, int DocumentCount, string RecipeIdentity, string ProfileId);

internal static partial class IndexCreator
{
    private const int RecipeSchemaVersion = 1;
    private static readonly string[] SupportedPresets =
    [
        "minimal", "search-small", "search-medium", "realistic-mixed",
        "vector-flat", "vector-hnsw", "vector-int8", "vector-bbq", "vector-filtered",
        "hybrid-balanced", "hybrid-text-heavy", "hybrid-vector-heavy", "multilingual",
        "compound", "non-compound", "many-small-segments", "delete-heavy",
        "codec-current", "codec-migration-required", "custom"
    ];
    private static readonly JsonSerializerOptions RecipeJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private static readonly JsonSerializerOptions RecipeIdentityJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static IndexCreationResult CreatePreset(
        string preset,
        string outputPath,
        ulong seed,
        int? requestedCount,
        int dimension,
        string distribution,
        string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preset);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string normalisedPreset = preset.Trim().ToLowerInvariant();
        if (!SupportedPresets.Contains(normalisedPreset, StringComparer.Ordinal))
            throw new ArgumentException($"Unknown preset '{preset}'. Supported presets: {string.Join(", ", SupportedPresets)}.", nameof(preset));
        if (normalisedPreset == "custom")
            throw new ArgumentException("Use create custom with a schema file to create a custom index.", nameof(preset));
        if (normalisedPreset == "codec-migration-required")
        {
            if (requestedCount is not null || seed != 42)
                throw new ArgumentException("The frozen codec-migration-required preset does not accept --seed or --count.");
            return CreateMigrationFixture(outputPath);
        }

        return CreatePresetCore(normalisedPreset, outputPath, seed, requestedCount, dimension, distribution,
            language, recipe: null);
    }

    private static IndexCreationResult CreatePresetCore(string preset, string outputPath, ulong seed,
        int? requestedCount, int dimension, string distribution, string language, IndexRecipe? recipe,
        CustomIndexSchema? customSchema = null)
    {
        string normalisedPreset = preset.Trim().ToLowerInvariant();
        if (normalisedPreset == "custom" && customSchema is null && recipe?.Parameters.TryGetValue("schemaJson", out string? schemaJson) == true)
            customSchema = DeserializeCustomSchema(schemaJson);
        bool compound = recipe?.Compound ?? (normalisedPreset == "custom" ? customSchema?.Compound ?? false : normalisedPreset == "compound");
        bool buildHnsw = recipe?.BuildHnsw ?? (normalisedPreset == "custom"
            ? customSchema is not null && (customSchema.BuildHnsw || customSchema.Fields.Any(static field => field.Hnsw))
            : normalisedPreset is not "vector-flat");
        VectorQuantisation quantisation = recipe is null
            ? normalisedPreset == "custom" ? ResolveCustomQuantisation(customSchema!) : normalisedPreset switch
            {
                "vector-int8" => VectorQuantisation.Int8,
                "vector-bbq" => VectorQuantisation.BBQ,
                _ => VectorQuantisation.None
            }
            : ParseQuantisation(recipe.VectorQuantisation);
        bool manySmallSegments = recipe?.ManySmallSegments ?? normalisedPreset == "many-small-segments";
        bool deleteHeavy = recipe?.DeleteHeavy ?? normalisedPreset == "delete-heavy";
        IDataForgeProfileSelection selection;

        if (normalisedPreset == "custom")
        {
            if (customSchema is null)
                throw new InvalidDataException("A custom index recipe does not contain its schema.");
            string canonicalSchema = JsonSerializer.Serialize(customSchema, RecipeIdentityJsonOptions);
            selection = new CustomProfileSelection(new LeanCorpusSearchProfile(), customSchema,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["schemaJson"] = canonicalSchema });
        }
        else if (normalisedPreset is "vector-flat" or "vector-hnsw" or "vector-int8" or "vector-bbq" or "vector-filtered")
        {
            if (recipe is null)
                buildHnsw = normalisedPreset != "vector-flat";
            selection = new VectorProfileSelection(new LeanCorpusVectorProfile(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["dimension"] = dimension.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["vectorDistribution"] = distribution,
                    ["clusterCount"] = "4",
                    ["queryCount"] = "1"
                });
        }
        else if (normalisedPreset is "hybrid-balanced" or "hybrid-text-heavy" or "hybrid-vector-heavy")
        {
            selection = new HybridProfileSelection(new LeanCorpusHybridProfile(),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["dimension"] = dimension.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }
        else if (normalisedPreset == "multilingual")
        {
            selection = new MultilingualProfileSelection(new RowlesTextMultilingualProfile(),
                new Dictionary<string, string>(StringComparer.Ordinal) { ["language"] = language });
        }
        else
        {
            selection = new SearchProfileSelection(new LeanCorpusSearchProfile(), new Dictionary<string, string>(StringComparer.Ordinal));
        }

        int count = recipe?.RecordCount ?? requestedCount ?? DefaultCount(normalisedPreset);
        IndexCreationResult created = selection.Create(normalisedPreset, outputPath, seed, count, compound, buildHnsw, quantisation,
            new IndexWriterConfigOptions(manySmallSegments, deleteHeavy));
        if (normalisedPreset == "custom" && customSchema is not null)
        {
            string metadataDirectory = Path.Combine(created.IndexPath, ".leancorpus");
            File.WriteAllText(Path.Combine(metadataDirectory, "schema.json"),
                JsonSerializer.Serialize(customSchema, RecipeJsonOptions) + Environment.NewLine);
        }
        return created;
    }

    internal static IndexCreationResult Reproduce(string sourcePath, string outputPath)
    {
        string recipePath = Path.Combine(Path.GetFullPath(sourcePath), ".leancorpus", "recipe.json");
        if (!File.Exists(recipePath))
            throw new FileNotFoundException("The index does not contain a reproducibility recipe.", recipePath);
        IndexRecipe recipe = JsonSerializer.Deserialize<IndexRecipe>(File.ReadAllText(recipePath), RecipeJsonOptions)
            ?? throw new InvalidDataException("The index recipe is empty or invalid.");
        if (recipe.SchemaVersion != RecipeSchemaVersion)
            throw new InvalidDataException($"Unsupported recipe schema version {recipe.SchemaVersion}.");
        if (!SupportedPresets.Contains(recipe.Preset, StringComparer.Ordinal))
            throw new InvalidDataException($"Recipe preset '{recipe.Preset}' is not registered.");
        IndexCreationResult result = recipe.Preset == "codec-migration-required"
            ? CreateMigrationFixture(outputPath)
            : CreatePresetCore(recipe.Preset, outputPath, recipe.Seed, recipe.RecordCount,
                ParseInt(recipe.Parameters, "dimension", 32), recipe.Parameters.GetValueOrDefault("vectorDistribution") ?? "Clustered",
                recipe.Parameters.GetValueOrDefault("language") ?? "en", recipe,
                recipe.Preset == "custom" && recipe.Parameters.TryGetValue("schemaJson", out string? schemaJson)
                    ? DeserializeCustomSchema(schemaJson)
                    : null);
        if (!string.Equals(result.RecipeIdentity, recipe.RecipeIdentity, StringComparison.Ordinal))
            throw new InvalidDataException("Recreated recipe identity differs from the source recipe.");
        return result;
    }

    private static VectorQuantisation ParseQuantisation(string value)
        => Enum.TryParse(value, ignoreCase: true, out VectorQuantisation parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidDataException($"Unknown recipe vector quantisation '{value}'.");

    private static int ParseInt(IReadOnlyDictionary<string, string> parameters, string key, int fallback)
        => parameters.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed) ? parsed : fallback;

    private static int DefaultCount(string preset) => preset switch
    {
        "minimal" => 8,
        "search-small" => 128,
        "search-medium" => 10_000,
        "realistic-mixed" => 2_048,
        "vector-hnsw" or "vector-flat" or "vector-int8" or "vector-bbq" or "vector-filtered" => 128,
        "hybrid-balanced" or "hybrid-text-heavy" or "hybrid-vector-heavy" => 128,
        "multilingual" => 24,
        "many-small-segments" => 64,
        "delete-heavy" => 128,
        _ => 128
    };

    private static IndexCreationResult CreateMigrationFixture(string outputPath)
    {
        string fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath))
            throw new IOException($"Output path '{fullPath}' is a file.");
        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
            throw new IOException($"Output directory '{fullPath}' is not empty.");
        bool createdDirectory = !Directory.Exists(fullPath);
        Directory.CreateDirectory(fullPath);
        try
        {
            using JsonDocument manifest = MigrationFixtureInstaller.ExtractRelease311Loose(fullPath);
            string metadataPath = Path.Combine(fullPath, ".leancorpus");
            Directory.CreateDirectory(metadataPath);
            File.WriteAllText(Path.Combine(metadataPath, "migration-fixture-manifest.json"),
                manifest.RootElement.GetRawText() + Environment.NewLine);
            string archiveIdentity = manifest.RootElement.GetProperty("ArchiveSha256").GetString()!;
            var recipeWithoutIdentity = new IndexRecipe(RecipeSchemaVersion, "fixture", "codec-migration-required",
                "leancorpus-release-fixture", 311, 311400, 8,
                new SortedDictionary<string, string>(StringComparer.Ordinal) { ["layout"] = "loose", ["archiveSha256"] = archiveIdentity },
                false, false, VectorQuantisation.None.ToString(), false, false, string.Empty);
            string identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(recipeWithoutIdentity, RecipeIdentityJsonOptions))));
            File.WriteAllText(Path.Combine(metadataPath, "recipe.json"),
                JsonSerializer.Serialize(recipeWithoutIdentity with { RecipeIdentity = identity }, RecipeJsonOptions) + Environment.NewLine);
            return new IndexCreationResult(fullPath, 8, identity, "leancorpus-release-fixture");
        }
        catch
        {
            if (createdDirectory && Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);
            throw;
        }
    }

    private sealed record IndexWriterConfigOptions(bool ManySmallSegments, bool DeleteHeavy);

    private interface IDataForgeProfileSelection
    {
        IndexCreationResult Create(string preset, string outputPath, ulong seed, int count, bool compound,
            bool buildHnsw, VectorQuantisation quantisation, IndexWriterConfigOptions indexOptions);
    }

    private sealed record SearchProfileSelection(LeanCorpusSearchProfile Profile, IReadOnlyDictionary<string, string> Parameters) : IDataForgeProfileSelection
    {
        public IndexCreationResult Create(string preset, string outputPath, ulong seed, int count, bool compound,
            bool buildHnsw, VectorQuantisation quantisation, IndexWriterConfigOptions indexOptions)
        {
            DataForgeGenerationOptions options = new(seed, count, Parameters);
            return MaterialiseAndBuild(Profile, options, preset, outputPath, compound, buildHnsw, quantisation, indexOptions,
                static record =>
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", record.Id));
                    document.Add(new TextField("title", record.Title, stored: true));
                    document.Add(new TextField("body", string.Concat(record.Title, " ", record.Body), stored: true));
                    document.Add(new StringField("locale", record.Locale));
                    document.Add(new StringField("archetype", record.Archetype));
                    document.Add(new StringField("category", record.Category));
                    document.Add(new StringField("region", record.Region));
                    document.Add(new StringField("active", record.Active ? "true" : "false"));
                    document.Add(new NumericField("price", record.PriceMinor));
                    document.Add(new GeoPointField("location", record.LatitudeE6 / 1_000_000.0, record.LongitudeE6 / 1_000_000.0));
                    return document;
                });
        }
    }

    private sealed record VectorProfileSelection(LeanCorpusVectorProfile Profile, IReadOnlyDictionary<string, string> Parameters) : IDataForgeProfileSelection
    {
        public IndexCreationResult Create(string preset, string outputPath, ulong seed, int count, bool compound,
            bool buildHnsw, VectorQuantisation quantisation, IndexWriterConfigOptions indexOptions)
        {
            DataForgeGenerationOptions options = new(seed, count, Parameters);
            return MaterialiseAndBuild(Profile, options, preset, outputPath, compound, buildHnsw, quantisation, indexOptions,
                static record =>
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", record.Id));
                    document.Add(new StringField("category", record.Category));
                    document.Add(new StringField("cluster", record.ClusterId.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    document.Add(new StringField("accessGroup", record.AccessGroup));
                    document.Add(new TextField("body", $"vector document {record.Id} cluster {record.ClusterId}", stored: true));
                    document.Add(new VectorField("embedding", record.Vector));
                    return document;
                });
        }
    }

    private sealed record HybridProfileSelection(LeanCorpusHybridProfile Profile, IReadOnlyDictionary<string, string> Parameters) : IDataForgeProfileSelection
    {
        public IndexCreationResult Create(string preset, string outputPath, ulong seed, int count, bool compound,
            bool buildHnsw, VectorQuantisation quantisation, IndexWriterConfigOptions indexOptions)
        {
            DataForgeGenerationOptions options = new(seed, count, Parameters);
            return MaterialiseAndBuild(Profile, options, preset, outputPath, compound, buildHnsw, quantisation, indexOptions,
                static record =>
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", record.Id));
                    document.Add(new TextField("title", record.Title, stored: true));
                    document.Add(new TextField("body", record.Body, stored: true));
                    document.Add(new StringField("topic", record.Topic));
                    document.Add(new StringField("category", record.Category));
                    document.Add(new StringField("accessGroup", record.AccessGroup));
                    document.Add(new VectorField("embedding", record.Vector));
                    return document;
                });
        }
    }

    private sealed record MultilingualProfileSelection(RowlesTextMultilingualProfile Profile, IReadOnlyDictionary<string, string> Parameters) : IDataForgeProfileSelection
    {
        public IndexCreationResult Create(string preset, string outputPath, ulong seed, int count, bool compound,
            bool buildHnsw, VectorQuantisation quantisation, IndexWriterConfigOptions indexOptions)
        {
            DataForgeGenerationOptions options = new(seed, count, Parameters);
            return MaterialiseAndBuild(Profile, options, preset, outputPath, compound, buildHnsw, quantisation, indexOptions,
                static record =>
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", record.Id));
                    document.Add(new StringField("language", record.Language));
                    document.Add(new TextField("body", record.Text, stored: true));
                    return document;
                });
        }
    }

    private static IndexCreationResult MaterialiseAndBuild<TRecord>(
        IDataForgeGeneratedProfile<TRecord> profile,
        DataForgeGenerationOptions options,
        string preset,
        string outputPath,
        bool compound,
        bool buildHnsw,
        VectorQuantisation quantisation,
        IndexWriterConfigOptions indexOptions,
        Func<TRecord, LeanDocument> map)
    {
        string fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath))
            throw new IOException($"Output path '{fullPath}' is a file.");
        if (Directory.Exists(fullPath) && Directory.EnumerateFileSystemEntries(fullPath).Any())
            throw new IOException($"Output directory '{fullPath}' is not empty.");
        bool createdDirectory = !Directory.Exists(fullPath);
        Directory.CreateDirectory(fullPath);
        string metadataDirectory = Path.Combine(fullPath, ".leancorpus");
        string datasetDirectory = Path.Combine(metadataDirectory, "dataforge");
        Directory.CreateDirectory(metadataDirectory);
        try
        {
            DataForgeMaterialiser.Materialise(profile, options, datasetDirectory);
            var config = new IndexWriterConfig
            {
                UseCompoundFile = compound,
                BuildHnswOnFlush = buildHnsw,
                VectorQuantisation = quantisation,
                StoreTermVectors = true,
                HnswSeed = (long)(options.Seed % (ulong)long.MaxValue),
                MergePolicy = indexOptions.ManySmallSegments ? NoMergePolicy.Instance : new TieredMergePolicy()
            };
            using (var directory = new MMapDirectory(fullPath))
            using (var writer = new IndexWriter(directory, config))
            {
                int ordinal = 0;
                foreach (TRecord record in profile.Generate(options))
                {
                    writer.AddDocument(map(record));
                    ordinal++;
                    if (indexOptions.ManySmallSegments && ordinal % 8 == 0)
                        writer.Commit();
                    if (indexOptions.DeleteHeavy && ordinal % 2 == 0)
                        writer.DeleteDocuments(new TermQuery("id", GetRecordId(record)));
                }
                writer.Commit();
            }

            File.Copy(Path.Combine(datasetDirectory, "manifest.json"), Path.Combine(metadataDirectory, "dataforge-manifest.json"), overwrite: true);
            var recipeWithoutIdentity = new IndexRecipe(RecipeSchemaVersion, preset == "custom" ? "custom" : "preset", preset,
                profile.Descriptor.ProfileId, profile.Descriptor.ProfileVersion, options.Seed, options.RecordCount,
                new SortedDictionary<string, string>(options.Parameters.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal), StringComparer.Ordinal), compound, buildHnsw,
                quantisation.ToString(), indexOptions.ManySmallSegments, indexOptions.DeleteHeavy, string.Empty);
            string identity = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(recipeWithoutIdentity, RecipeIdentityJsonOptions))));
            IndexRecipe recipe = recipeWithoutIdentity with { RecipeIdentity = identity };
            File.WriteAllText(Path.Combine(metadataDirectory, "recipe.json"), JsonSerializer.Serialize(recipe, RecipeJsonOptions) + Environment.NewLine);
            return new IndexCreationResult(fullPath, options.RecordCount, identity, profile.Descriptor.ProfileId);
        }
        catch
        {
            if (createdDirectory && Directory.Exists(fullPath))
                Directory.Delete(fullPath, recursive: true);
            throw;
        }
    }

    private static string GetRecordId<TRecord>(TRecord record)
    {
        return record switch
        {
            SearchRecord search => search.Id,
            VectorRecord vector => vector.Id,
            HybridRecord hybrid => hybrid.Id,
            MultilingualTextRecord multilingual => multilingual.Id,
            _ => throw new InvalidOperationException($"The generated record type '{typeof(TRecord).Name}' has no deterministic ID mapping.")
        };
    }
}
