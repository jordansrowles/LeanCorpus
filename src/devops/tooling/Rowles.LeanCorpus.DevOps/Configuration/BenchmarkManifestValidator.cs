using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Infrastructure;

namespace Rowles.LeanCorpus.DevOps.Configuration;

internal static class BenchmarkManifestValidator
{
    public static void Validate(DevOpsConfiguration configuration, IReadOnlyDictionary<string, string> manifestJsonByProject)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(manifestJsonByProject);

        foreach (var projectKey in configuration.BenchmarkProjects.Keys.OrderBy(static key => key, StringComparer.Ordinal))
        {
            if (!manifestJsonByProject.TryGetValue(projectKey, out var json))
                throw Error($"No benchmark host manifest was supplied for project '{projectKey}'.");
            ValidateProject(configuration, projectKey, json);
        }

        var unknownProject = manifestJsonByProject.Keys.FirstOrDefault(key => !configuration.BenchmarkProjects.ContainsKey(key));
        if (unknownProject is not null)
            throw Error($"Benchmark host manifest refers to unknown project '{unknownProject}'.");
    }

    public static async Task ValidateBuiltHostsAsync(
        DevOpsConfiguration configuration,
        string repositoryRoot,
        IProcessRunner processRunner,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentNullException.ThrowIfNull(processRunner);

        var temporaryRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus.DevOps.BenchmarkManifests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            foreach (var (projectKey, project) in configuration.BenchmarkProjects.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                var projectPath = Path.Combine(repositoryRoot, project.Project.Replace('/', Path.DirectorySeparatorChar));
                foreach (var framework in project.Frameworks)
                {
                    var runKey = projectKey + "." + framework;
                    var stdoutPath = Path.Combine(temporaryRoot, runKey + ".stdout.log");
                    var stderrPath = Path.Combine(temporaryRoot, runKey + ".stderr.log");
                    var result = await processRunner.RunAsync(new ProcessRequest
                    {
                        ExecutablePath = "dotnet",
                        Arguments = ["run", "--configuration", "Release", "--framework", framework, "--no-build", "--no-restore",
                            "--project", projectPath, "--", "--devops-manifest"],
                        WorkingDirectory = repositoryRoot,
                        StandardOutputLogPath = stdoutPath,
                        StandardErrorLogPath = stderrPath,
                        EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                            ["DOTNET_NOLOGO"] = "1"
                        },
                        Timeout = TimeSpan.FromMinutes(2),
                        ProgressInterval = TimeSpan.FromSeconds(30),
                        MaxTailLines = 10,
                        MaxTailCharacters = 4_000_000
                    }, cancellationToken).ConfigureAwait(false);

                    if (result.Outcome != Rowles.LeanCorpus.DevOps.Terminal.OutcomeKind.Success)
                    {
                        var details = string.Join(Environment.NewLine,
                            result.StandardErrorTail.Concat(result.StandardOutputTail).Where(static line => !string.IsNullOrWhiteSpace(line)));
                        throw Error($"Benchmark host '{projectKey}' could not emit a manifest. Build its benchmark project before running deep validation. " +
                            (string.IsNullOrWhiteSpace(details) ? result.Failure ?? $"Exit code {result.RawExitCode}." : details));
                    }

                    var manifest = string.Join(Environment.NewLine, result.StandardOutputTail);
                    ValidateProject(configuration, projectKey, manifest);

                    var inventoryArguments = new List<string>
                    {
                        "run", "--configuration", "Release", "--framework", framework, "--no-build", "--no-restore",
                        "--project", projectPath
                    };
                    if (projectKey == "core")
                        inventoryArguments.AddRange(["--", "--suite", "all-with-explicit", "--", "--list", "json"]);
                    else
                        inventoryArguments.AddRange(["--", "--list", "json"]);

                    var inventoryStdoutPath = Path.Combine(temporaryRoot, runKey + ".bdn.stdout.log");
                    var inventoryStderrPath = Path.Combine(temporaryRoot, runKey + ".bdn.stderr.log");
                    var inventoryResult = await processRunner.RunAsync(new ProcessRequest
                    {
                        ExecutablePath = "dotnet",
                        Arguments = inventoryArguments,
                        WorkingDirectory = repositoryRoot,
                        StandardOutputLogPath = inventoryStdoutPath,
                        StandardErrorLogPath = inventoryStderrPath,
                        EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1",
                            ["DOTNET_NOLOGO"] = "1",
                            ["LEANCORPUS_ARTIFACT_DIR"] = Path.Combine(temporaryRoot, "list-artifacts", runKey)
                        },
                        Timeout = TimeSpan.FromMinutes(2),
                        ProgressInterval = TimeSpan.FromSeconds(30),
                        MaxTailLines = 10,
                        MaxTailCharacters = 4_000_000
                    }, cancellationToken).ConfigureAwait(false);

                    if (inventoryResult.Outcome != Rowles.LeanCorpus.DevOps.Terminal.OutcomeKind.Success)
                    {
                        var details = string.Join(Environment.NewLine,
                            inventoryResult.StandardErrorTail.Concat(inventoryResult.StandardOutputTail).Where(static line => !string.IsNullOrWhiteSpace(line)));
                        throw Error($"Benchmark host '{projectKey}' could not emit its BDN --list json inventory. " +
                            (string.IsNullOrWhiteSpace(details) ? inventoryResult.Failure ?? $"Exit code {inventoryResult.RawExitCode}." : details));
                    }

                    var inventory = await File.ReadAllTextAsync(inventoryStdoutPath, cancellationToken).ConfigureAwait(false);
                    BenchmarkCaseInventoryValidator.Validate(configuration.BenchmarkCases, configuration.BenchmarkCaseExclusions,
                        projectKey, inventory);
                }
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static void ValidateProject(DevOpsConfiguration configuration, string projectKey, string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw Error($"Benchmark host manifest for '{projectKey}' is invalid JSON: {exception.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out var schemaVersion) || schemaVersion.GetInt32() != 1)
                throw Error($"Benchmark host manifest for '{projectKey}' must use schema version 1.");
            if (!root.TryGetProperty("project", out var manifestProject) || manifestProject.GetString() != projectKey)
                throw Error($"Benchmark host manifest project does not match configured project '{projectKey}'.");
            if (!root.TryGetProperty("benchmarkClasses", out var benchmarkClasses) || benchmarkClasses.ValueKind != JsonValueKind.Array)
                throw Error($"Benchmark host manifest for '{projectKey}' must contain a benchmarkClasses array.");

            var classNames = new HashSet<string>(StringComparer.Ordinal);
            var manifestWorkloads = new List<ManifestWorkload>();
            foreach (var item in benchmarkClasses.EnumerateArray())
            {
                if (!item.TryGetProperty("type", out var typeElement) || string.IsNullOrWhiteSpace(typeElement.GetString()))
                    throw Error($"Benchmark host manifest for '{projectKey}' contains a class without a type name.");
                var fullName = typeElement.GetString()!;
                var className = fullName[(fullName.LastIndexOf('.') + 1)..];
                if (!classNames.Add(className))
                    throw Error($"Benchmark host manifest for '{projectKey}' contains duplicate benchmark class '{className}'.");

                var suiteIds = item.TryGetProperty("suiteIds", out var suiteIdsElement)
                    ? ReadStringArray(suiteIdsElement, $"benchmark class '{className}' suiteIds")
                    : [];
                var includeInAll = item.TryGetProperty("includeInAll", out var includeInAllElement) && includeInAllElement.GetBoolean();
                var includeWithExplicit = item.TryGetProperty("includeWithExplicit", out var includeWithExplicitElement) && includeWithExplicitElement.GetBoolean();
                manifestWorkloads.Add(new ManifestWorkload(className, suiteIds, includeInAll, includeWithExplicit));
            }

            if (projectKey == "core" && root.TryGetProperty("discoveredBenchmarkClasses", out var discoveredClasses))
            {
                if (discoveredClasses.ValueKind != JsonValueKind.Array)
                    throw Error("Core benchmark host manifest must contain a discoveredBenchmarkClasses array.");

                var discoveredClassNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in discoveredClasses.EnumerateArray())
                {
                    if (!item.TryGetProperty("type", out var typeElement) || string.IsNullOrWhiteSpace(typeElement.GetString()))
                        throw Error("Core benchmark host manifest contains a discovered class without a type name.");
                    var fullName = typeElement.GetString()!;
                    var className = fullName[(fullName.LastIndexOf('.') + 1)..];
                    if (!discoveredClassNames.Add(className))
                        throw Error($"Core benchmark reflection inventory contains duplicate class '{className}'.");
                }

                var unregisteredClasses = discoveredClassNames.Except(classNames, StringComparer.Ordinal)
                    .OrderBy(static className => className, StringComparer.Ordinal).ToArray();
                if (unregisteredClasses.Length > 0)
                    throw Error("Core BDN-discovered benchmark classes have no suite registration: " + string.Join(", ", unregisteredClasses) + ".");

                var missingDiscovery = classNames.Except(discoveredClassNames, StringComparer.Ordinal)
                    .OrderBy(static className => className, StringComparer.Ordinal).ToArray();
                if (missingDiscovery.Length > 0)
                    throw Error("Core suite registrations are absent from the reflection inventory: " + string.Join(", ", missingDiscovery) + ".");
            }

            foreach (var (groupId, group) in configuration.BenchmarkGroups.Where(pair => pair.Value.Project == projectKey))
            {
                for (var index = 0; index < group.Benchmarks.Length; index++)
                {
                    var benchmark = group.Benchmarks[index];
                    if (!classNames.Contains(benchmark))
                        throw Error($"benchmark-groups.json $.data.{groupId}.Benchmarks[{index}] refers to unknown benchmark class '{benchmark}' in project '{projectKey}'.");
                }
            }

            foreach (var (suiteId, suite) in configuration.BenchmarkSuites.Where(pair => pair.Value.Project == projectKey))
            {
                foreach (var (index, className) in suite.BenchmarkClasses.Select(static (name, index) => (index, name)))
                {
                    if (!classNames.Contains(className))
                        throw Error($"benchmark-suites.json $.suiteMetadata.{suiteId}.BenchmarkClasses[{index}] refers to unknown benchmark class '{className}' in project '{projectKey}'.");
                }
            }

            if (projectKey != "core") return;
            if (!root.TryGetProperty("suites", out var suitesElement) || suitesElement.ValueKind != JsonValueKind.Array)
                throw Error("Core benchmark host manifest must contain a suites array.");

            var manifestSuites = new Dictionary<string, ManifestSuite>(StringComparer.Ordinal);
            foreach (var item in suitesElement.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
                if (string.IsNullOrWhiteSpace(id))
                    throw Error("Core benchmark host manifest contains a suite without an ID.");
                var aliases = item.TryGetProperty("aliases", out var aliasesElement)
                    ? ReadStringArray(aliasesElement, $"suite '{id}' aliases")
                    : [];
                var isSelector = item.TryGetProperty("isSelector", out var selectorElement) && selectorElement.GetBoolean();
                if (!manifestSuites.TryAdd(id, new ManifestSuite(aliases, isSelector)))
                    throw Error($"Core benchmark host manifest contains duplicate suite ID '{id}'.");
            }

            var configuredSuites = configuration.BenchmarkSuites
                .Where(static pair => pair.Value.Project == "core")
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
            var unconfiguredSuite = manifestSuites.Keys.FirstOrDefault(id => !configuredSuites.ContainsKey(id));
            if (unconfiguredSuite is not null)
                throw Error($"Core benchmark host suite '{unconfiguredSuite}' is missing from benchmark-suites.json.");
            foreach (var (suiteId, suite) in configuredSuites)
            {
                if (!manifestSuites.TryGetValue(suiteId, out var manifestSuite))
                    throw Error($"benchmark-suites.json $.data.{suiteId} has no matching Core host suite registration.");

                if (!new HashSet<string>(suite.Aliases, StringComparer.OrdinalIgnoreCase)
                    .SetEquals(manifestSuite.Aliases))
                    throw Error($"Suite aliases for '{suiteId}' differ between benchmark-suites.json and the Core host manifest.");
                if (suite.IsSelector != manifestSuite.IsSelector)
                    throw Error($"Selector policy for suite '{suiteId}' differs between benchmark-suites.json and the Core host manifest.");
                if (suite.IsSelector) continue;

                var workloads = manifestWorkloads.Where(workload => workload.SuiteIds.Contains(suiteId, StringComparer.Ordinal)).ToArray();
                if (workloads.Length == 0)
                    throw Error($"Core host suite '{suiteId}' has no registered benchmark classes.");

                var expectedInAll = suite.IncludeInAll ?? (!suite.ExplicitOnly && !suite.RequiresGutenbergData);
                var expectedWithExplicit = suite.IncludeWithExplicit ?? suite.ExplicitOnly;
                var expectedExplicitOnly = !expectedInAll && expectedWithExplicit;
                if (suite.ExplicitOnly != expectedExplicitOnly || workloads.Any(workload =>
                        workload.IncludeInAll != expectedInAll || workload.IncludeWithExplicit != expectedWithExplicit))
                    throw Error($"Explicit and default inclusion policy for suite '{suiteId}' differs between benchmark-suites.json and the Core host manifest.");
            }
        }
    }

    private static string[] ReadStringArray(JsonElement element, string description)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw Error($"Manifest {description} must be an array.");
        var values = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            var value = item.GetString();
            if (string.IsNullOrWhiteSpace(value))
                throw Error($"Manifest {description} contains an empty value.");
            values.Add(value);
        }
        return values.ToArray();
    }

    private static InvalidDataException Error(string message) => new(message);

    private sealed record ManifestSuite(string[] Aliases, bool IsSelector);

    private sealed record ManifestWorkload(string ClassName, string[] SuiteIds, bool IncludeInAll, bool IncludeWithExplicit);
}
