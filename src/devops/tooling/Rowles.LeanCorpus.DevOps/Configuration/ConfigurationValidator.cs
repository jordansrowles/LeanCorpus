using System.Collections.Immutable;

namespace Rowles.LeanCorpus.DevOps.Configuration;

internal static class ConfigurationValidator
{
    private static readonly HashSet<string> SupportedFrameworks = new(StringComparer.Ordinal)
    {
        "net10.0", "net11.0"
    };

    private static readonly HashSet<string> ProjectCapabilities = new(StringComparer.Ordinal)
    {
        "Groups", "Wikipedia", "Gutenberg", "Independent"
    };

    public static DevOpsConfiguration Validate(
        DevOpsConfiguration configuration,
        string repositoryRoot,
        Func<string, bool>? projectExists = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var root = Path.GetFullPath(repositoryRoot);
        var projectFileExists = projectExists ?? File.Exists;
        ValidateTestMetadata(configuration.TestAreas, configuration.TestSuites);
        ValidateBenchmarkProjects(configuration.BenchmarkProjects, root, projectFileExists);
        var benchmarkSuites = ValidateBenchmarkSuites(configuration.BenchmarkSuites, configuration.BenchmarkProjects);
        ValidateHistoricalSuiteLabels(configuration.HistoricalBenchmarkSuiteLabels, benchmarkSuites);
        ValidateCodeAreaReferences(configuration.CodeAreas, configuration.TestSuites, configuration.TestAreas);
        if (!IsStableId(configuration.DefaultBenchmarkDataset))
            throw Error("benchmark-datasets.json", "$.defaultDataset", $"'{configuration.DefaultBenchmarkDataset}' is not a lower-case stable dataset ID");
        ValidateBenchmarkDatasets(configuration.BenchmarkDatasets, benchmarkSuites, configuration.BenchmarkProjects,
            configuration.DefaultBenchmarkDataset);
        ValidateBenchmarkGroups(configuration.BenchmarkGroups, configuration.BenchmarkProjects, configuration.TestAreas);
        ValidateBenchmarkCases(configuration.BenchmarkCases, configuration.BenchmarkCaseExclusions, configuration.BenchmarkProjects);

        return configuration with { BenchmarkSuites = benchmarkSuites };
    }

    private static void ValidateBenchmarkCases(
        ImmutableArray<BenchmarkCaseConfiguration> cases,
        ImmutableArray<BenchmarkCaseExclusionConfiguration> exclusions,
        ImmutableDictionary<string, BenchmarkProjectConfiguration> projects)
    {
        foreach (var benchmarkCase in cases)
        {
            if (!projects.ContainsKey(benchmarkCase.Project))
                throw Error("benchmark-cases.json", "$.caseFamilies." + benchmarkCase.Id + ".project",
                    $"refers to unknown benchmark project '{benchmarkCase.Project}'");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var exclusion in exclusions)
        {
            if (!projects.ContainsKey(exclusion.Project))
                throw Error("benchmark-cases.json", "$.exclusions." + exclusion.BenchmarkClass + ".project",
                    $"refers to unknown benchmark project '{exclusion.Project}'");
            if (exclusion.ExpiresOn < today)
                throw Error("benchmark-cases.json", "$.exclusions." + exclusion.BenchmarkClass + ".expiresOn",
                    $"benchmark exclusion for '{exclusion.BenchmarkClass}.{exclusion.BenchmarkMethod}' expired on {exclusion.ExpiresOn:yyyy-MM-dd}");
        }
    }

    private static void ValidateTestMetadata(
        ImmutableArray<string> testAreas,
        ImmutableDictionary<string, TestSuiteConfiguration> suites)
    {
        if (testAreas.IsDefaultOrEmpty)
            throw Error("test-suites.json", "$.areas", "must contain at least one test area");

        var areaIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < testAreas.Length; index++)
        {
            var area = testAreas[index];
            if (string.IsNullOrWhiteSpace(area) || !area.All(char.IsLetterOrDigit) || !char.IsUpper(area[0]))
                throw Error("test-suites.json", $"$.areas[{index}]", $"'{area}' is not a valid test area ID");
            if (!areaIds.Add(area))
                throw Error("test-suites.json", $"$.areas[{index}]", $"duplicate test area '{area}'");
        }

        var orders = new HashSet<int>();
        foreach (var (id, suite) in suites.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!IsStableId(id))
                throw Error("test-suites.json", "$.data." + id, $"'{id}' is not a lower-case stable suite ID");
            if (suite.Order is { } order && !orders.Add(order))
                throw Error("test-suites.json", "$.data." + id + ".Order", $"suite order '{order}' is already assigned");
            if (suite.DataForgeQualification && !suite.Runner.Equals("Mtp", StringComparison.Ordinal))
                throw Error("test-suites.json", "$.data." + id + ".DataForgeQualification", "qualification test suites must use the Mtp runner");
            if (suite.RequiresBrowser && suite.IncludeInAll != false)
                throw Error("test-suites.json", "$.data." + id + ".IncludeInAll", "browser-required suites must be excluded from the generic all selector");
        }
    }

    private static void ValidateBenchmarkProjects(
        ImmutableDictionary<string, BenchmarkProjectConfiguration> projects,
        string repositoryRoot,
        Func<string, bool> projectExists)
    {
        foreach (var (id, project) in projects.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var propertyPath = "$.data." + id;
            if (!IsStableId(id))
                throw Error("benchmark-projects.json", propertyPath, $"'{id}' is not a lower-case stable project ID");
            if (string.IsNullOrWhiteSpace(project.DisplayName))
                throw Error("benchmark-projects.json", propertyPath + ".DisplayName", "must be a non-empty project display name");

            ValidateProjectPath(project.Project, repositoryRoot, projectExists, propertyPath + ".Project");
            ValidateFrameworks(project.Frameworks, "benchmark-projects.json", propertyPath + ".Frameworks");

            if (project.Capabilities.IsDefaultOrEmpty)
                throw Error("benchmark-projects.json", propertyPath + ".Capabilities", "must contain at least one capability");

            var capabilities = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < project.Capabilities.Length; index++)
            {
                var capability = project.Capabilities[index];
                if (!ProjectCapabilities.Contains(capability))
                    throw Error("benchmark-projects.json", $"{propertyPath}.Capabilities[{index}]", $"unknown benchmark project capability '{capability}'");
                if (!capabilities.Add(capability))
                    throw Error("benchmark-projects.json", $"{propertyPath}.Capabilities[{index}]", $"duplicate benchmark project capability '{capability}'");
            }
        }
    }

    private static ImmutableDictionary<string, BenchmarkSuiteConfiguration> ValidateBenchmarkSuites(
        ImmutableDictionary<string, BenchmarkSuiteConfiguration> suites,
        ImmutableDictionary<string, BenchmarkProjectConfiguration> projects)
    {
        var resolved = ImmutableDictionary.CreateBuilder<string, BenchmarkSuiteConfiguration>(StringComparer.Ordinal);
        foreach (var (id, suite) in suites.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var metadataPath = "$.suiteMetadata." + id;
            if (string.IsNullOrWhiteSpace(suite.Description))
                throw Error("benchmark-suites.json", metadataPath, $"unknown benchmark suite '{id}'; no matching description exists at $.data.{id}");
            if (!IsStableId(id))
                throw Error("benchmark-suites.json", "$.data." + id, $"'{id}' is not a lower-case stable suite ID");
            if (!projects.TryGetValue(suite.Project, out var project))
                throw Error("benchmark-suites.json", metadataPath + ".Project", $"refers to unknown benchmark project '{suite.Project}'");

            var frameworks = suite.Frameworks.IsDefaultOrEmpty ? project.Frameworks : suite.Frameworks;
            ValidateFrameworks(frameworks, "benchmark-suites.json", metadataPath + ".Frameworks");
            foreach (var framework in frameworks)
            {
                if (!project.Frameworks.Contains(framework, StringComparer.Ordinal))
                    throw Error("benchmark-suites.json", metadataPath + ".Frameworks", $"framework '{framework}' is not supported by benchmark project '{suite.Project}'");
            }

            var aliases = ValidateUniqueStrings(suite.Aliases, "benchmark-suites.json", metadataPath + ".Aliases", StringComparer.OrdinalIgnoreCase,
                "duplicate benchmark suite alias");
            var benchmarkClasses = ValidateUniqueStrings(suite.BenchmarkClasses, "benchmark-suites.json", metadataPath + ".BenchmarkClasses", StringComparer.Ordinal,
                "duplicate benchmark class");
            if (suite.RequiresGutenbergData && !project.Capabilities.Contains("Gutenberg", StringComparer.Ordinal))
                throw Error("benchmark-suites.json", metadataPath + ".RequiresGutenbergData", $"benchmark project '{suite.Project}' does not provide Gutenberg data capability");
            if (suite.ExplicitOnly && suite.IncludeInAll == true)
                throw Error("benchmark-suites.json", metadataPath + ".IncludeInAll", "an explicit-only suite cannot be included in the primary all selector");
            if (suite.ExplicitOnly && suite.IncludeWithExplicit == false)
                throw Error("benchmark-suites.json", metadataPath + ".IncludeWithExplicit", "an explicit-only suite must be included by the explicit selector");
            if (suite.IncludeInAll == false && suite.IncludeWithExplicit == true && !suite.ExplicitOnly)
                throw Error("benchmark-suites.json", metadataPath + ".ExplicitOnly", "a suite available only through the explicit selector must be marked explicit-only");

            resolved.Add(id, suite with { Frameworks = frameworks, BenchmarkClasses = benchmarkClasses, Aliases = aliases });
        }

        var allIds = resolved.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aliasOwners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, suite) in resolved.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            for (var index = 0; index < suite.Aliases.Length; index++)
            {
                var alias = suite.Aliases[index];
                if (allIds.Contains(alias))
                    throw Error("benchmark-suites.json", $"$.suiteMetadata.{id}.Aliases[{index}]", $"benchmark suite alias '{alias}' conflicts with a stable suite ID");
                if (!aliasOwners.TryAdd(alias, id))
                    throw Error("benchmark-suites.json", $"$.suiteMetadata.{id}.Aliases[{index}]", $"duplicate benchmark suite alias '{alias}' also used by '{aliasOwners[alias]}'");
            }
        }

        return resolved.ToImmutable();
    }

    private static void ValidateCodeAreaReferences(
        ImmutableDictionary<string, CodeAreaConfiguration> areas,
        ImmutableDictionary<string, TestSuiteConfiguration> testSuites,
        ImmutableArray<string> testAreas)
    {
        foreach (var (areaName, area) in areas.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            for (var index = 0; index < area.Targets.Length; index++)
            {
                var target = area.Targets[index];
                var propertyPath = $"$.data.{areaName}.Targets[{index}]";
                var separator = target.IndexOf(':');
                if (separator <= 0 || separator == target.Length - 1)
                    throw Error("code-areas.json", propertyPath, $"'{target}' must use suite:area format");

                var suite = target[..separator];
                var testArea = target[(separator + 1)..];
                if (!testSuites.ContainsKey(suite))
                    throw Error("code-areas.json", propertyPath, $"'{target}' refers to unknown test suite '{suite}'");
                if (!testAreas.Contains(testArea, StringComparer.OrdinalIgnoreCase))
                    throw Error("code-areas.json", propertyPath, $"'{target}' refers to unknown test area '{testArea}'");
            }
        }
    }

    private static void ValidateHistoricalSuiteLabels(
        ImmutableDictionary<string, string> labels,
        ImmutableDictionary<string, BenchmarkSuiteConfiguration> suites)
    {
        foreach (var (suiteId, label) in labels.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (suites.ContainsKey(suiteId))
                throw Error("benchmark-suites.json", "$.historicalLabels." + suiteId, $"historical label '{suiteId}' is still an active benchmark suite; move its display name to suiteMetadata");
            if (string.IsNullOrWhiteSpace(label))
                throw Error("benchmark-suites.json", "$.historicalLabels." + suiteId, "must be a non-empty display name");
        }
    }

    private static void ValidateBenchmarkDatasets(
        ImmutableDictionary<string, ImmutableArray<string>> datasets,
        ImmutableDictionary<string, BenchmarkSuiteConfiguration> suites,
        ImmutableDictionary<string, BenchmarkProjectConfiguration> projects,
        string defaultDataset)
    {
        foreach (var (datasetName, suiteIds) in datasets.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var propertyPath = "$.data." + datasetName;
            if (!IsStableId(datasetName))
                throw Error("benchmark-datasets.json", propertyPath, $"'{datasetName}' is not a lower-case stable dataset ID");
            if (suiteIds.IsDefaultOrEmpty)
                throw Error("benchmark-datasets.json", propertyPath, "must contain at least one benchmark suite ID");

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < suiteIds.Length; index++)
            {
                var suiteId = suiteIds[index];
                var itemPath = $"{propertyPath}[{index}]";
                if (!seen.Add(suiteId))
                    throw Error("benchmark-datasets.json", itemPath, $"duplicate benchmark suite reference '{suiteId}'");
                if (!suites.TryGetValue(suiteId, out var suite))
                    throw Error("benchmark-datasets.json", itemPath, $"unknown benchmark suite reference '{suiteId}'");
                if (suite.IsSelector)
                    throw Error("benchmark-datasets.json", itemPath, $"selector '{suiteId}' cannot be a dataset benchmark suite");
                if (datasetName.Equals("wikipedia", StringComparison.Ordinal) &&
                    (!projects.TryGetValue(suite.Project, out var project) || !project.Capabilities.Contains("Wikipedia", StringComparer.Ordinal)))
                    throw Error("benchmark-datasets.json", itemPath, $"benchmark project '{suite.Project}' does not provide Wikipedia data capability");
            }
        }

        if (datasets.ContainsKey(defaultDataset))
            throw Error("benchmark-datasets.json", "$.defaultDataset", $"default dataset '{defaultDataset}' must not also be declared as a restricted dataset");

        foreach (var (suiteId, suite) in suites.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            for (var index = 0; index < suite.Datasets.Length; index++)
            {
                var datasetName = suite.Datasets[index];
                var propertyPath = $"$.suiteMetadata.{suiteId}.Datasets[{index}]";
                if (datasetName.Equals(defaultDataset, StringComparison.Ordinal))
                    continue;
                if (!datasets.TryGetValue(datasetName, out var eligibleSuites))
                    throw Error("benchmark-suites.json", propertyPath, $"unknown benchmark dataset '{datasetName}'");
                if (!eligibleSuites.Contains(suiteId, StringComparer.Ordinal))
                    throw Error("benchmark-suites.json", propertyPath, $"dataset '{datasetName}' does not list benchmark suite '{suiteId}' as eligible");
            }
        }
    }

    private static void ValidateBenchmarkGroups(
        ImmutableDictionary<string, BenchmarkGroupConfiguration> groups,
        ImmutableDictionary<string, BenchmarkProjectConfiguration> projects,
        ImmutableArray<string> testAreas)
    {
        foreach (var (groupId, group) in groups.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var propertyPath = "$.data." + groupId;
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(group.Area))
                throw Error("benchmark-groups.json", propertyPath, "group key and area must not be empty");
            if (!testAreas.Contains(group.Area, StringComparer.OrdinalIgnoreCase))
                throw Error("benchmark-groups.json", propertyPath + ".Area", $"unknown test area '{group.Area}'");
            if (!projects.TryGetValue(group.Project, out var project))
                throw Error("benchmark-groups.json", propertyPath + ".Project", $"unknown benchmark project '{group.Project}'");
            if (!project.Capabilities.Contains("Groups", StringComparer.Ordinal))
                throw Error("benchmark-groups.json", propertyPath + ".Project", $"benchmark project '{group.Project}' does not support benchmark groups");

            _ = ValidateUniqueStrings(group.Benchmarks, "benchmark-groups.json", propertyPath + ".Benchmarks", StringComparer.Ordinal,
                "duplicate benchmark class");
        }
    }

    private static ImmutableArray<string> ValidateUniqueStrings(
        ImmutableArray<string> values,
        string fileName,
        string propertyPath,
        StringComparer comparer,
        string duplicateMessage)
    {
        var seen = new HashSet<string>(comparer);
        for (var index = 0; index < values.Length; index++)
        {
            if (!seen.Add(values[index]))
                throw Error(fileName, $"{propertyPath}[{index}]", $"{duplicateMessage} '{values[index]}'");
        }

        return values;
    }

    private static void ValidateFrameworks(ImmutableArray<string> frameworks, string fileName, string propertyPath)
    {
        if (frameworks.IsDefaultOrEmpty)
            throw Error(fileName, propertyPath, "must contain at least one supported framework moniker");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < frameworks.Length; index++)
        {
            var framework = frameworks[index];
            if (!SupportedFrameworks.Contains(framework))
                throw Error(fileName, $"{propertyPath}[{index}]", $"unsupported benchmark framework '{framework}'");
            if (!seen.Add(framework))
                throw Error(fileName, $"{propertyPath}[{index}]", $"duplicate benchmark framework '{framework}'");
        }
    }

    private static void ValidateProjectPath(
        string project,
        string repositoryRoot,
        Func<string, bool> projectExists,
        string propertyPath)
    {
        var portablePath = project.Replace('\\', '/');
        if (Path.IsPathRooted(project) || portablePath.StartsWith("/", StringComparison.Ordinal) ||
            portablePath.IndexOf(':') >= 0 || portablePath.Split('/').Any(static part => part is "." or "..") ||
            !portablePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            throw Error("benchmark-projects.json", propertyPath, $"'{project}' must be a relative repository .csproj path");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Path.Combine(repositoryRoot, portablePath.Replace('/', Path.DirectorySeparatorChar)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw Error("benchmark-projects.json", propertyPath, $"'{project}' is not a valid repository project path");
        }

        if (!IsWithin(repositoryRoot, fullPath))
            throw Error("benchmark-projects.json", propertyPath, $"'{project}' must name an existing repository project file inside the repository");
        if (!projectExists(fullPath))
            throw Error("benchmark-projects.json", propertyPath, $"'{project}' must name an existing repository project file");
    }

    private static bool IsStableId(string value)
    {
        if (string.IsNullOrEmpty(value) || value[0] == '-' || value[^1] == '-')
            return false;

        var previousWasDash = false;
        foreach (var character in value)
        {
            var isDash = character == '-';
            if (!(isDash || character is >= 'a' and <= 'z' || character is >= '0' and <= '9') || (isDash && previousWasDash))
                return false;
            previousWasDash = isDash;
        }

        return true;
    }

    private static bool IsWithin(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static InvalidDataException Error(string fileName, string propertyPath, string message)
        => new($"Configuration '{fileName}', property '{propertyPath}': {message}.");
}
