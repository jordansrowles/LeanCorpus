using System.Collections.Immutable;
using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Configuration;

internal sealed class ConfigurationLoader
{
    private const int SupportedSchemaVersion = 1;
    private static readonly HashSet<string> TestAreas = new(StringComparer.OrdinalIgnoreCase)
    {
        "Foundation", "CodecKit", "Diagnostics", "Document", "Index", "Linq", "Mapping", "Search",
        "Serialization", "Store", "TextIntegration", "Util", "Analysers", "Filters", "Stemmers",
        "Tokenisers", "Dictionaries", "Languages", "Architecture", "Functional", "Orchard", "Server"
    };

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
        var testSuites = ReadMap("test-suites.json", ReadTestSuite);
        var benchmarkSuites = ReadMap("benchmark-suites.json", (value, propertyPath) => ReadString(value, Path.Combine(_configurationRoot, "benchmark-suites.json"), propertyPath));
        var codeAreas = ReadMap("code-areas.json", ReadCodeArea);
        var benchmarkStrategies = ReadMap("benchmark-strategies.json", ReadBenchmarkStrategy);
        var benchmarkDatasets = ReadMap("benchmark-datasets.json", (value, propertyPath) => ReadStringArray(value, Path.Combine(_configurationRoot, "benchmark-datasets.json"), propertyPath));
        var benchmarkGroups = ReadMap("benchmark-groups.json", ReadBenchmarkGroup);

        ValidateReferences(testSuites, codeAreas, benchmarkSuites, benchmarkDatasets, benchmarkGroups);
        return new DevOpsConfiguration(testSuites, codeAreas, benchmarkSuites, benchmarkStrategies, benchmarkDatasets, benchmarkGroups);
    }

    private ImmutableDictionary<string, TValue> ReadMap<TValue>(string fileName, Func<JsonElement, string, TValue> readValue)
    {
        var path = Path.Combine(_configurationRoot, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Managed DevOps configuration file '{path}' is missing.", path);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(path), new JsonDocumentOptions
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

        using (document)
        {
            RejectDuplicateProperties(document.RootElement, path, "$", caseInsensitive: true);
            RequireObjectProperties(document.RootElement, path, "$", ["schemaVersion", "data"], ["schemaVersion", "data"]);
            var version = ReadInt(document.RootElement.GetProperty("schemaVersion"), path, "$.schemaVersion");
            if (version != SupportedSchemaVersion)
            {
                throw Error(path, "$.schemaVersion", $"unsupported version {version}; supported version is {SupportedSchemaVersion}");
            }

            var data = document.RootElement.GetProperty("data");
            if (data.ValueKind != JsonValueKind.Object)
            {
                throw Error(path, "$.data", "must be an object keyed by configuration ID");
            }

            var builder = ImmutableDictionary.CreateBuilder<string, TValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in data.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name))
                {
                    throw Error(path, "$.data", "configuration IDs cannot be empty");
                }

                builder.Add(property.Name, readValue(property.Value, $"$.data.{property.Name}"));
            }

            if (builder.Count == 0)
            {
                throw Error(path, "$.data", "must contain at least one configuration entry");
            }

            return builder.ToImmutable();
        }
    }

    private TestSuiteConfiguration ReadTestSuite(JsonElement value, string propertyPath)
    {
        const string file = "test-suites.json";
        var path = Path.Combine(_configurationRoot, file);
        RequireObjectProperties(value, path, propertyPath,
            ["Name", "Project", "Runner", "Frameworks", "DefaultFramework", "Coverage", "Capabilities", "IncludeInAll", "ExpandFrameworksByDefault", "DefaultFilter"],
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
        var allowedCapabilities = new HashSet<string>(["Trx", "HangDump", "CrashDump"], StringComparer.OrdinalIgnoreCase);
        if (capabilities.Any(capability => !allowedCapabilities.Contains(capability)) || capabilities.Distinct(StringComparer.OrdinalIgnoreCase).Count() != capabilities.Length)
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
            OptionalString(value, "DefaultFilter", path, propertyPath));
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
        if (!TestAreas.Contains(area))
        {
            throw Error(path, propertyPath + ".Area", $"unknown test area '{area}'");
        }

        if (project is not ("core" or "text"))
        {
            throw Error(path, propertyPath + ".Project", $"unknown benchmark project '{project}'; allowed values are core and text");
        }

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

    private void ValidateReferences(
        ImmutableDictionary<string, TestSuiteConfiguration> testSuites,
        ImmutableDictionary<string, CodeAreaConfiguration> codeAreas,
        ImmutableDictionary<string, string> benchmarkSuites,
        ImmutableDictionary<string, ImmutableArray<string>> benchmarkDatasets,
        ImmutableDictionary<string, BenchmarkGroupConfiguration> benchmarkGroups)
    {
        foreach (var (areaName, area) in codeAreas)
        {
            foreach (var target in area.Targets)
            {
                var separator = target.IndexOf(':');
                if (separator <= 0 || separator == target.Length - 1)
                {
                    throw Error("code-areas.json", $"$.data.{areaName}.Targets", $"'{target}' must use suite:area format");
                }

                var suite = target[..separator];
                var testArea = target[(separator + 1)..];
                if (!testSuites.ContainsKey(suite))
                {
                    throw Error("code-areas.json", $"$.data.{areaName}.Targets", $"'{target}' refers to unknown test suite '{suite}'");
                }

                if (!TestAreas.Contains(testArea))
                {
                    throw Error("code-areas.json", $"$.data.{areaName}.Targets", $"'{target}' refers to unknown test area '{testArea}'");
                }
            }
        }

        foreach (var (datasetName, suites) in benchmarkDatasets)
        {
            foreach (var suite in suites)
            {
                if (!benchmarkSuites.ContainsKey(suite))
                {
                    throw Error("benchmark-datasets.json", $"$.data.{datasetName}", $"unknown benchmark suite reference '{suite}'");
                }
            }
        }

        foreach (var (groupName, group) in benchmarkGroups)
        {
            if (group.Area.Length == 0 || groupName.Length == 0)
            {
                throw Error("benchmark-groups.json", $"$.data.{groupName}", "group key and area must not be empty");
            }
        }
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
