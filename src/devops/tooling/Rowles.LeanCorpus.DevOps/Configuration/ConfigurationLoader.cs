using System.Collections.Immutable;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Configuration;

internal sealed class ConfigurationLoader
{
    private const int SupportedSchemaVersion = 1;

    private readonly string _repositoryRoot;
    private readonly string _configurationRoot;
    private readonly Func<string, bool> _projectExists;

    public ConfigurationLoader(string repositoryRoot, Func<string, bool>? projectExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        _repositoryRoot = Path.GetFullPath(repositoryRoot);
        _configurationRoot = Path.Combine(_repositoryRoot, ".config", "devops");
        _projectExists = projectExists ?? File.Exists;
    }

    public DevOpsConfiguration Load()
    {
        var (testSuites, testAreas) = ReadTestSuites();
        var benchmarkProjects = ReadMap("benchmark-projects.json", ReadBenchmarkProject, StringComparer.Ordinal);
        var (benchmarkSuites, historicalSuiteLabels) = ReadBenchmarkSuites();
        var codeAreas = ReadMap("code-areas.json", ReadCodeArea);
        var benchmarkStrategies = ReadMap("benchmark-strategies.json", ReadBenchmarkStrategy);
        var (benchmarkDatasets, defaultBenchmarkDataset) = ReadBenchmarkDatasets();
        var benchmarkGroups = ReadMap("benchmark-groups.json", ReadBenchmarkGroup);
        var requiredDataForgeProfiles = ReadDataForgeSyntheticProfiles();
        var benchmarkCaseManifest = ReadBenchmarkCases();
        var benchmarkCorrectnessAudit = ReadBenchmarkCorrectnessAudit();

        var configuration = new DevOpsConfiguration(testSuites, codeAreas, benchmarkProjects, benchmarkSuites,
            benchmarkStrategies, benchmarkDatasets, benchmarkGroups)
        {
            DefaultBenchmarkDataset = defaultBenchmarkDataset,
            HistoricalBenchmarkSuiteLabels = historicalSuiteLabels,
            TestAreas = testAreas,
            RequiredDataForgeSyntheticProfiles = requiredDataForgeProfiles,
            BenchmarkCases = benchmarkCaseManifest.CaseFamilies,
            BenchmarkCaseExclusions = benchmarkCaseManifest.Exclusions,
            BenchmarkCorrectnessAudit = benchmarkCorrectnessAudit
        };
        return ConfigurationValidator.Validate(configuration, _repositoryRoot, _projectExists);
    }

    private (ImmutableDictionary<string, TestSuiteConfiguration> Suites, ImmutableArray<string> Areas) ReadTestSuites()
    {
        const string fileName = "test-suites.json";
        var path = Path.Combine(_configurationRoot, fileName);
        using var document = ReadDocument(fileName);
        ValidateDocument(document.RootElement, path, ["areas"]);
        if (!document.RootElement.TryGetProperty("areas", out var areasElement))
            throw Error(path, "$.areas", "required property 'areas' is missing");

        var areas = ReadStringArray(areasElement, path, "$.areas");
        var suites = ReadDataMap(document.RootElement.GetProperty("data"), path, ReadTestSuite);
        return (suites, areas);
    }

    private ImmutableDictionary<string, TValue> ReadMap<TValue>(
        string fileName,
        Func<JsonElement, string, TValue> readValue,
        StringComparer? idComparer = null)
    {
        var path = Path.Combine(_configurationRoot, fileName);
        using var document = ReadDocument(fileName);
        ValidateDocument(document.RootElement, path);
        return ReadDataMap(document.RootElement.GetProperty("data"), path, readValue, idComparer);
    }

    private (ImmutableDictionary<string, BenchmarkSuiteConfiguration> Suites, ImmutableDictionary<string, string> HistoricalLabels) ReadBenchmarkSuites()
    {
        const string fileName = "benchmark-suites.json";
        var path = Path.Combine(_configurationRoot, fileName);
        using var document = ReadDocument(fileName);
        ValidateDocument(document.RootElement, path, ["suiteMetadata", "historicalLabels"]);
        var descriptions = ReadDataMap(document.RootElement.GetProperty("data"), path,
            (value, propertyPath) => ReadString(value, path, propertyPath), StringComparer.Ordinal);
        var historicalLabels = document.RootElement.TryGetProperty("historicalLabels", out var historicalLabelsElement)
            ? ReadDataMap(historicalLabelsElement, path, (value, propertyPath) => ReadString(value, path, propertyPath), StringComparer.Ordinal)
            : ImmutableDictionary.Create<string, string>(StringComparer.Ordinal);

        var metadata = ImmutableDictionary.CreateBuilder<string, BenchmarkSuiteMetadata>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty("suiteMetadata", out var metadataElement))
        {
            if (metadataElement.ValueKind != JsonValueKind.Object)
                throw Error(path, "$.suiteMetadata", "must be an object keyed by benchmark suite ID");

            foreach (var property in metadataElement.EnumerateObject())
            {
                var propertyPath = "$.suiteMetadata." + property.Name;
                metadata.Add(property.Name, ReadBenchmarkSuiteMetadata(property.Value, path, propertyPath, property.Name));
            }
        }

        var suites = ImmutableDictionary.CreateBuilder<string, BenchmarkSuiteConfiguration>(StringComparer.Ordinal);
        foreach (var (id, description) in descriptions)
        {
            var suiteMetadata = metadata.TryGetValue(id, out var configured)
                ? configured
                : new BenchmarkSuiteMetadata(
                    DisplayName: DisplayNameFromId(id),
                    Project: "core",
                    BenchmarkClasses: [],
                    Aliases: [],
                    Frameworks: [],
                    Datasets: [],
                    ExplicitOnly: false,
                    ControlledCorpusOnly: true,
                    CorpusOnly: false,
                    RequiresGutenbergData: false,
                    IsSelector: false,
                    IncludeInAll: null,
                    IncludeWithExplicit: null);

            suites.Add(id, new BenchmarkSuiteConfiguration(
                description,
                suiteMetadata.DisplayName,
                suiteMetadata.Project,
                suiteMetadata.BenchmarkClasses,
                suiteMetadata.Aliases,
                suiteMetadata.Frameworks,
                suiteMetadata.Datasets,
                suiteMetadata.ExplicitOnly,
                suiteMetadata.ControlledCorpusOnly,
                suiteMetadata.CorpusOnly,
                suiteMetadata.RequiresGutenbergData,
                suiteMetadata.IsSelector,
                suiteMetadata.IncludeInAll,
                suiteMetadata.IncludeWithExplicit));
        }

        foreach (var (id, suiteMetadata) in metadata)
        {
            if (!descriptions.ContainsKey(id))
            {
                suites.Add(id, new BenchmarkSuiteConfiguration(
                    string.Empty,
                    suiteMetadata.DisplayName,
                    suiteMetadata.Project,
                    suiteMetadata.BenchmarkClasses,
                    suiteMetadata.Aliases,
                    suiteMetadata.Frameworks,
                    suiteMetadata.Datasets,
                    suiteMetadata.ExplicitOnly,
                    suiteMetadata.ControlledCorpusOnly,
                    suiteMetadata.CorpusOnly,
                    suiteMetadata.RequiresGutenbergData,
                    suiteMetadata.IsSelector,
                    suiteMetadata.IncludeInAll,
                    suiteMetadata.IncludeWithExplicit));
            }
        }

        return (suites.ToImmutable(), historicalLabels);
    }

    private JsonDocument ReadDocument(string fileName)
    {
        var path = Path.Combine(_configurationRoot, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Managed DevOps configuration file '{path}' is missing.", path);

        try
        {
            return JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 128
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Configuration '{path}' contains invalid JSON: {exception.Message}", exception);
        }
    }

    private static void ValidateDocument(JsonElement root, string path, IReadOnlyCollection<string>? optionalProperties = null)
    {
        RejectDuplicateProperties(root, path, "$", caseInsensitive: true);
        var allowed = new HashSet<string>(["schemaVersion", "data"], StringComparer.Ordinal);
        if (optionalProperties is not null)
            allowed.UnionWith(optionalProperties);
        RequireObjectProperties(root, path, "$", allowed, ["schemaVersion", "data"]);
        var version = ReadInt(root.GetProperty("schemaVersion"), path, "$.schemaVersion");
        if (version != SupportedSchemaVersion)
            throw Error(path, "$.schemaVersion", $"unsupported version {version}; supported version is {SupportedSchemaVersion}");
    }

    private static ImmutableDictionary<string, TValue> ReadDataMap<TValue>(
        JsonElement data,
        string path,
        Func<JsonElement, string, TValue> readValue,
        StringComparer? idComparer = null)
    {
        if (data.ValueKind != JsonValueKind.Object)
            throw Error(path, "$.data", "must be an object keyed by configuration ID");

        var builder = ImmutableDictionary.CreateBuilder<string, TValue>(idComparer ?? StringComparer.OrdinalIgnoreCase);
        foreach (var property in data.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(property.Name))
                throw Error(path, "$.data", "configuration IDs cannot be empty");

            try
            {
                builder.Add(property.Name, readValue(property.Value, "$.data." + property.Name));
            }
            catch (ArgumentException exception)
            {
                throw Error(path, "$.data." + property.Name, $"duplicate configuration ID '{property.Name}' ({exception.Message})");
            }
        }

        if (builder.Count == 0)
            throw Error(path, "$.data", "must contain at least one configuration entry");

        return builder.ToImmutable();
    }

    private BenchmarkProjectConfiguration ReadBenchmarkProject(JsonElement value, string propertyPath)
    {
        const string fileName = "benchmark-projects.json";
        var path = Path.Combine(_configurationRoot, fileName);
        RequireObjectProperties(value, path, propertyPath,
            ["DisplayName", "Project", "Frameworks", "Capabilities", "IncludeInAll", "IncludeWithExplicit"],
            ["Project", "Frameworks", "Capabilities", "IncludeInAll", "IncludeWithExplicit"]);

        var displayName = OptionalString(value, "DisplayName", path, propertyPath) ?? DisplayNameFromId(propertyPath[(propertyPath.LastIndexOf('.') + 1)..]);
        var project = ReadString(value.GetProperty("Project"), path, propertyPath + ".Project");
        var frameworks = ReadStringArray(value.GetProperty("Frameworks"), path, propertyPath + ".Frameworks");
        var capabilities = ReadStringArray(value.GetProperty("Capabilities"), path, propertyPath + ".Capabilities");

        return new BenchmarkProjectConfiguration(
            displayName,
            project,
            frameworks,
            capabilities,
            ReadBoolean(value.GetProperty("IncludeInAll"), path, propertyPath + ".IncludeInAll"),
            ReadBoolean(value.GetProperty("IncludeWithExplicit"), path, propertyPath + ".IncludeWithExplicit"));
    }

    private (ImmutableDictionary<string, ImmutableArray<string>> Datasets, string DefaultDataset) ReadBenchmarkDatasets()
    {
        const string fileName = "benchmark-datasets.json";
        var path = Path.Combine(_configurationRoot, fileName);
        using var document = ReadDocument(fileName);
        ValidateDocument(document.RootElement, path, ["defaultDataset"]);
        var datasets = ReadDataMap(document.RootElement.GetProperty("data"), path,
            (value, propertyPath) => ReadStringArray(value, path, propertyPath), StringComparer.Ordinal);
        var defaultDataset = document.RootElement.TryGetProperty("defaultDataset", out var defaultElement)
            ? ReadString(defaultElement, path, "$.defaultDataset")
            : "synthetic";
        return (datasets, defaultDataset);
    }

    private ImmutableArray<DataForgeProfileRequirement> ReadDataForgeSyntheticProfiles()
    {
        const string fileName = "dataforge-qualification.json";
        var path = Path.Combine(_configurationRoot, fileName);
        using var document = ReadDocument(fileName);
        ValidateDocument(document.RootElement, path);
        var data = document.RootElement.GetProperty("data");
        RequireObjectProperties(data, path, "$.data", ["requiredSyntheticProfiles"], ["requiredSyntheticProfiles"]);
        var profiles = ReadDataMap(data.GetProperty("requiredSyntheticProfiles"), path,
            (value, propertyPath) =>
            {
                var version = ReadInt(value, path, propertyPath);
                if (version < 1)
                    throw Error(path, propertyPath, "profile version must be positive");
                return version;
            }, StringComparer.Ordinal);
        return profiles.OrderBy(static item => item.Key, StringComparer.Ordinal)
            .Select(static item => new DataForgeProfileRequirement(item.Key, item.Value))
            .ToImmutableArray();
    }

    private BenchmarkCaseManifestConfiguration ReadBenchmarkCases()
    {
        const string fileName = "benchmark-cases.json";
        var path = Path.Combine(_configurationRoot, fileName);
        using var document = ReadDocument(fileName);
        return BenchmarkCaseManifestReader.Read(document.RootElement, path);
    }

    private BenchmarkCorrectnessAuditConfiguration ReadBenchmarkCorrectnessAudit()
    {
        const string fileName = "benchmark-correctness-audit.json";
        var path = Path.Combine(_configurationRoot, fileName);
        using var document = ReadDocument(fileName);
        return BenchmarkCorrectnessAuditReader.Read(document.RootElement, path, _repositoryRoot);
    }

    private BenchmarkSuiteMetadata ReadBenchmarkSuiteMetadata(JsonElement value, string path, string propertyPath, string suiteId)
    {
        RequireObjectProperties(value, path, propertyPath,
            ["DisplayName", "Project", "BenchmarkClasses", "Aliases", "Frameworks", "Datasets", "ExplicitOnly", "ControlledCorpusOnly", "CorpusOnly", "RequiresGutenbergData", "IsSelector", "IncludeInAll", "IncludeWithExplicit"],
            []);

        return new BenchmarkSuiteMetadata(
            OptionalString(value, "DisplayName", path, propertyPath) ?? DisplayNameFromId(suiteId),
            OptionalString(value, "Project", path, propertyPath) ?? "core",
            OptionalStringArray(value, "BenchmarkClasses", path, propertyPath),
            OptionalStringArray(value, "Aliases", path, propertyPath),
            OptionalStringArray(value, "Frameworks", path, propertyPath),
            OptionalStringArray(value, "Datasets", path, propertyPath),
            OptionalBoolean(value, "ExplicitOnly", path, propertyPath) ?? false,
            OptionalBoolean(value, "ControlledCorpusOnly", path, propertyPath) ?? true,
            OptionalBoolean(value, "CorpusOnly", path, propertyPath) ?? false,
            OptionalBoolean(value, "RequiresGutenbergData", path, propertyPath) ?? false,
            OptionalBoolean(value, "IsSelector", path, propertyPath) ?? false,
            OptionalBoolean(value, "IncludeInAll", path, propertyPath),
            OptionalBoolean(value, "IncludeWithExplicit", path, propertyPath));
    }

    private static ImmutableArray<string> OptionalStringArray(JsonElement value, string name, string path, string propertyPath)
        => value.TryGetProperty(name, out var element) ? ReadStringArray(element, path, propertyPath + "." + name) : [];

    private static string DisplayNameFromId(string id)
        => string.Join(' ', id.Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));

    private sealed record BenchmarkSuiteMetadata(
        string DisplayName,
        string Project,
        ImmutableArray<string> BenchmarkClasses,
        ImmutableArray<string> Aliases,
        ImmutableArray<string> Frameworks,
        ImmutableArray<string> Datasets,
        bool ExplicitOnly,
        bool ControlledCorpusOnly,
        bool CorpusOnly,
        bool RequiresGutenbergData,
        bool IsSelector,
        bool? IncludeInAll,
        bool? IncludeWithExplicit);

    private TestSuiteConfiguration ReadTestSuite(JsonElement value, string propertyPath)
    {
        const string file = "test-suites.json";
        var path = Path.Combine(_configurationRoot, file);
        RequireObjectProperties(value, path, propertyPath,
            ["Name", "Project", "Runner", "Frameworks", "DefaultFramework", "Coverage", "Capabilities", "IncludeInAll", "ExpandFrameworksByDefault", "DefaultFilter", "Order", "DataForgeQualification", "RequiresBrowser"],
            ["Name", "Project", "Runner", "Frameworks", "Coverage", "Capabilities"]);

        var name = ReadString(value.GetProperty("Name"), path, propertyPath + ".Name");
        var project = ReadString(value.GetProperty("Project"), path, propertyPath + ".Project");
        var runner = ReadString(value.GetProperty("Runner"), path, propertyPath + ".Runner");
        if (runner is not ("Mtp" or "AotNative"))
        {
            throw Error(path, propertyPath + ".Runner", $"unknown runner '{runner}'; allowed values are Mtp and AotNative");
        }

        var frameworks = ReadStringArray(value.GetProperty("Frameworks"), path, propertyPath + ".Frameworks");
        if (frameworks.IsDefaultOrEmpty || frameworks.Distinct(StringComparer.OrdinalIgnoreCase).Count() != frameworks.Length ||
            frameworks.Any(framework => !System.Text.RegularExpressions.Regex.IsMatch(framework, "^net[0-9]+\\.[0-9]+$", System.Text.RegularExpressions.RegexOptions.CultureInvariant)))
        {
            throw Error(path, propertyPath + ".Frameworks", "must contain unique framework monikers such as net10.0");
        }

        var defaultFramework = OptionalString(value, "DefaultFramework", path, propertyPath);
        if (defaultFramework is not null && !frameworks.Contains(defaultFramework, StringComparer.OrdinalIgnoreCase))
        {
            throw Error(path, propertyPath + ".DefaultFramework", $"'{defaultFramework}' is not listed in Frameworks");
        }

        var fullProjectPath = Path.GetFullPath(Path.Combine(_repositoryRoot, project.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithin(_repositoryRoot, fullProjectPath) || !_projectExists(fullProjectPath) || !project.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw Error(path, propertyPath + ".Project", $"'{project}' must name an existing repository project file");
        }

        var capabilities = ReadStringArray(value.GetProperty("Capabilities"), path, propertyPath + ".Capabilities");
        if (capabilities.Any(capability => !TestRunnerCapabilityCatalog.TryParse(capability, out _)) ||
            capabilities.Distinct(StringComparer.OrdinalIgnoreCase).Count() != capabilities.Length)
        {
            throw Error(path, propertyPath + ".Capabilities", "contains an unknown or duplicate capability");
        }

        return new TestSuiteConfiguration(
            name,
            project,
            runner,
            frameworks,
            defaultFramework,
            ReadBoolean(value.GetProperty("Coverage"), path, propertyPath + ".Coverage"),
            capabilities,
            OptionalBoolean(value, "IncludeInAll", path, propertyPath),
            OptionalBoolean(value, "ExpandFrameworksByDefault", path, propertyPath),
            OptionalString(value, "DefaultFilter", path, propertyPath),
            value.TryGetProperty("Order", out var orderElement) ? ReadInt(orderElement, path, propertyPath + ".Order") : null,
            value.TryGetProperty("DataForgeQualification", out var qualificationElement)
                ? ReadBoolean(qualificationElement, path, propertyPath + ".DataForgeQualification")
                : false)
        {
            RequiresBrowser = OptionalBoolean(value, "RequiresBrowser", path, propertyPath) ?? false
        };
    }

    private CodeAreaConfiguration ReadCodeArea(JsonElement value, string propertyPath)
    {
        var path = Path.Combine(_configurationRoot, "code-areas.json");
        RequireObjectProperties(value, path, propertyPath, ["Globs", "Targets"], ["Globs", "Targets"]);
        var globs = ReadStringArray(value.GetProperty("Globs"), path, propertyPath + ".Globs");
        if (globs.IsDefaultOrEmpty)
        {
            throw Error(path, propertyPath + ".Globs", "must contain at least one path pattern");
        }

        return new CodeAreaConfiguration(globs, ReadStringArray(value.GetProperty("Targets"), path, propertyPath + ".Targets"));
    }

    private BenchmarkStrategyConfiguration ReadBenchmarkStrategy(JsonElement value, string propertyPath)
    {
        var path = Path.Combine(_configurationRoot, "benchmark-strategies.json");
        RequireObjectProperties(value, path, propertyPath, ["Description", "DocCount", "Job"], ["Description", "DocCount", "Job"]);
        var description = ReadString(value.GetProperty("Description"), path, propertyPath + ".Description");
        var docCount = ReadInt(value.GetProperty("DocCount"), path, propertyPath + ".DocCount");
        var job = ReadStringArray(value.GetProperty("Job"), path, propertyPath + ".Job");
        if (docCount < 0 || job.IsDefaultOrEmpty)
        {
            throw Error(path, propertyPath, "DocCount must be non-negative and Job must not be empty");
        }

        return new BenchmarkStrategyConfiguration(description, docCount, job);
    }

    private BenchmarkGroupConfiguration ReadBenchmarkGroup(JsonElement value, string propertyPath)
    {
        var path = Path.Combine(_configurationRoot, "benchmark-groups.json");
        RequireObjectProperties(value, path, propertyPath, ["Area", "Globs", "Project", "Benchmarks"], ["Area", "Globs", "Project", "Benchmarks"]);
        var area = ReadString(value.GetProperty("Area"), path, propertyPath + ".Area");
        var project = ReadString(value.GetProperty("Project"), path, propertyPath + ".Project");

        var globs = ReadStringArray(value.GetProperty("Globs"), path, propertyPath + ".Globs");
        var benchmarks = ReadStringArray(value.GetProperty("Benchmarks"), path, propertyPath + ".Benchmarks");
        if (globs.IsDefaultOrEmpty || benchmarks.IsDefaultOrEmpty)
        {
            throw Error(path, propertyPath, "Globs and Benchmarks must not be empty");
        }

        return new BenchmarkGroupConfiguration(area, globs, project, benchmarks);
    }

    private static string ReadString(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Error(path, propertyPath, "must be a non-empty string");
        }

        return value.GetString()!;
    }

    private static ImmutableArray<string> ReadStringArray(JsonElement value, string path, string propertyPath)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Error(path, propertyPath, "must be an array of strings");
        }

        var builder = ImmutableArray.CreateBuilder<string>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            builder.Add(ReadString(item, path, $"{propertyPath}[{index++}]"));
        }

        return builder.ToImmutable();
    }

    private static int ReadInt(JsonElement value, string path, string propertyPath)
        => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result)
            ? result
            : throw Error(path, propertyPath, "must be a 32-bit integer");

    private static bool ReadBoolean(JsonElement value, string path, string propertyPath)
        => value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw Error(path, propertyPath, "must be a boolean");

    private static string? OptionalString(JsonElement value, string name, string path, string propertyPath)
    {
        if (!value.TryGetProperty(name, out var element))
        {
            return null;
        }

        return ReadString(element, path, propertyPath + "." + name);
    }

    private static bool? OptionalBoolean(JsonElement value, string name, string path, string propertyPath)
    {
        if (!value.TryGetProperty(name, out var element))
        {
            return null;
        }

        return ReadBoolean(element, path, propertyPath + "." + name);
    }

    private static void RequireObjectProperties(JsonElement value, string path, string propertyPath, IReadOnlyCollection<string> allowed, IReadOnlyCollection<string> required)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Error(path, propertyPath, "must be an object");
        }

        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!allowedSet.Contains(property.Name))
            {
                throw Error(path, propertyPath, $"unknown property '{property.Name}'");
            }

            present.Add(property.Name);
        }

        foreach (var name in required)
        {
            if (!present.Contains(name))
            {
                throw Error(path, propertyPath, $"required property '{name}' is missing");
            }
        }
    }

    private static void RejectDuplicateProperties(JsonElement value, string path, string propertyPath, bool caseInsensitive)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var comparer = caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var properties = new HashSet<string>(comparer);
            foreach (var property in value.EnumerateObject())
            {
                if (!properties.Add(property.Name))
                {
                    throw Error(path, propertyPath, $"contains duplicate property '{property.Name}'");
                }

                RejectDuplicateProperties(property.Value, path, propertyPath + "." + property.Name, caseInsensitive);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                RejectDuplicateProperties(item, path, $"{propertyPath}[{index++}]", caseInsensitive);
            }
        }
    }

    private static InvalidDataException Error(string path, string propertyPath, string message)
        => new($"Configuration '{path}', property '{propertyPath}': {message}.");

    private static bool IsWithin(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

}
