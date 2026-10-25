using Rowles.LeanCorpus.DevOps.Configuration;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record TestTargetRequest
{
    public IReadOnlyList<string> Suites { get; init; } = ["all"];
    public string Framework { get; init; } = "net10.0";
    public bool FrameworkExplicit { get; init; }
    public string Configuration { get; init; } = "Release";
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public bool ContinuousIntegration { get; init; }
    public bool CollectCoverage { get; init; }
    public string Area { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Technique { get; init; } = string.Empty;
    public string Filter { get; init; } = string.Empty;
    public IReadOnlyList<string> AdditionalArguments { get; init; } = [];
}

internal sealed record TestTarget(
    string Key,
    string Name,
    string Suite,
    string RunnerKind,
    string Project,
    string Framework,
    string Configuration,
    string RuntimeIdentifier,
    string Filter,
    IReadOnlyList<string> Areas,
    bool CoverageEligible,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<string> AdditionalArguments);

internal sealed class TestTargetResolver(IReadOnlyDictionary<string, TestSuiteConfiguration> suites)
{
    public IReadOnlyList<TestTarget> Resolve(
        TestTargetRequest request,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? affectedAreasBySuite = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        var requestedSuites = ParseSuites(request.Suites);
        var isAffected = requestedSuites.Count == 1 && requestedSuites[0].Equals("affected", StringComparison.OrdinalIgnoreCase);
        var isAll = requestedSuites.Count == 1 && requestedSuites[0].Equals("all", StringComparison.OrdinalIgnoreCase);
        var isExplicitSelection = !isAll;
        if (requestedSuites.Count > 1 && (requestedSuites.Contains("all", StringComparer.OrdinalIgnoreCase) ||
                requestedSuites.Contains("affected", StringComparer.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("'all' and 'affected' cannot be combined with explicit test suites.");
        }

        IReadOnlyDictionary<string, IReadOnlyList<string>> affectedAreas = affectedAreasBySuite
            ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<string> suiteKeys;
        if (isAffected)
        {
            if (affectedAreas.Count == 0 || affectedAreas.Any(static pair => pair.Value.Count == 0))
            {
                throw new InvalidOperationException("Affected selection did not resolve any test areas; refusing to run zero tests.");
            }
            suiteKeys = affectedAreas.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        else if (isAll)
        {
            suiteKeys = GetAllSuiteKeys();
        }
        else
        {
            suiteKeys = requestedSuites;
        }

        var targets = new List<TestTarget>();
        foreach (var suiteKey in suiteKeys)
        {
            if (!suites.TryGetValue(suiteKey, out var suite))
            {
                throw new InvalidOperationException($"Unknown test suite '{suiteKey}'.");
            }

            var suiteAreas = isAffected ? affectedAreas[suiteKey] : [];
            var frameworks = SelectFrameworks(suiteKey, suite, request, isExplicitSelection);
            if (frameworks.Count == 0)
            {
                continue;
            }

            if (request.CollectCoverage && !suite.Coverage)
            {
                if (isExplicitSelection)
                {
                    throw new InvalidOperationException($"Test suite '{suiteKey}' is not eligible for coverage.");
                }
                continue;
            }

            var runnerKind = ParseRunner(suiteKey, suite.Runner);
            var suiteFilter = TestFilterBuilder.Build(
                affectedAreas: suiteAreas,
                areas: SplitValues(request.Area),
                categories: SplitValues(request.Category),
                techniques: SplitValues(request.Technique),
                filter: request.Filter,
                defaultFilter: suite.DefaultFilter);
            var runtimeIdentifier = runnerKind == TestRunnerKind.AotNative
                ? string.IsNullOrWhiteSpace(request.RuntimeIdentifier) ? GetCurrentRuntimeIdentifier() : request.RuntimeIdentifier
                : string.Empty;

            foreach (var framework in frameworks)
            {
                var key = $"{suiteKey}/{framework}";
                if (runtimeIdentifier.Length > 0)
                {
                    key += $"/{runtimeIdentifier}";
                }

                targets.Add(new TestTarget(
                    key,
                    suite.Name,
                    suiteKey,
                    runnerKind.ToString(),
                    suite.Project,
                    framework,
                    request.Configuration,
                    runtimeIdentifier,
                    suiteFilter,
                    suiteAreas.Order(StringComparer.OrdinalIgnoreCase).ToArray(),
                    suite.Coverage,
                    suite.Capabilities,
                    request.AdditionalArguments));
            }
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException(isExplicitSelection
                ? $"No executable target was resolved for suite '{string.Join(',', requestedSuites)}'."
                : "No executable test targets were resolved.");
        }

        return targets;
    }

    internal IReadOnlyList<string> GetOrderedSuiteKeys(bool includeExplicitOnly)
    {
        return suites
            .Where(pair => includeExplicitOnly || pair.Value.IncludeInAll != false)
            .OrderBy(static pair => pair.Value.Order ?? int.MaxValue)
            .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(static pair => pair.Key)
            .ToArray();
    }

    private IReadOnlyList<string> SelectFrameworks(
        string suiteKey,
        TestSuiteConfiguration suite,
        TestTargetRequest request,
        bool explicitSuite)
    {
        if (request.FrameworkExplicit)
        {
            if (suite.Frameworks.Contains(request.Framework, StringComparer.OrdinalIgnoreCase))
            {
                return [request.Framework];
            }
            if (explicitSuite)
            {
                throw new InvalidOperationException(
                    $"Test suite '{suiteKey}' does not support framework '{request.Framework}'. Supported frameworks: {string.Join(", ", suite.Frameworks)}.");
            }
            return [];
        }

        if (request.ContinuousIntegration || suite.ExpandFrameworksByDefault == true)
        {
            return suite.Frameworks;
        }

        var selected = suite.DefaultFramework ?? request.Framework;
        if (suite.Frameworks.Contains(selected, StringComparer.OrdinalIgnoreCase))
        {
            return [selected];
        }
        if (explicitSuite)
        {
            throw new InvalidOperationException(
                $"Test suite '{suiteKey}' does not support framework '{selected}'. Supported frameworks: {string.Join(", ", suite.Frameworks)}.");
        }
        return [];
    }

    private IReadOnlyList<string> GetAllSuiteKeys()
        => GetOrderedSuiteKeys(includeExplicitOnly: false);

    private static IReadOnlyList<string> ParseSuites(IEnumerable<string> suites)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var suite in suites)
        {
            foreach (var token in (suite ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var normalised = token.ToLowerInvariant();
                if (seen.Add(normalised))
                {
                    result.Add(normalised);
                }
            }
        }

        if (result.Count == 0)
        {
            result.Add("all");
        }
        return result;
    }

    private static string[] SplitValues(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static TestRunnerKind ParseRunner(string suiteKey, string runner)
        => runner switch
        {
            "Mtp" => TestRunnerKind.Mtp,
            "AotNative" => TestRunnerKind.AotNative,
            _ => throw new InvalidOperationException($"Test suite '{suiteKey}' declares unsupported runner kind '{runner}'.")
        };

    internal static string GetCurrentRuntimeIdentifier()
    {
        var operatingSystem = OperatingSystem.IsWindows() ? "Windows"
            : OperatingSystem.IsLinux() ? "Linux"
            : OperatingSystem.IsMacOS() ? "macOS"
            : null;
        return GetRuntimeIdentifier(operatingSystem, System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture);
    }

    internal static string GetRuntimeIdentifier(string? operatingSystem, System.Runtime.InteropServices.Architecture architecture)
    {
        var platform = operatingSystem switch
        {
            "Windows" => "win",
            "Linux" => "linux",
            "macOS" => "osx",
            _ => throw new InvalidOperationException("Unsupported operating system. Native AOT tests support Linux, macOS and Windows.")
        };
        var architectureName = architecture switch
        {
            System.Runtime.InteropServices.Architecture.X64 => "x64",
            System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
            _ => throw new InvalidOperationException("Unsupported architecture. Native AOT tests support X64 and Arm64.")
        };
        return $"{platform}-{architectureName}";
    }
}

internal enum TestRunnerKind
{
    Mtp,
    AotNative
}
