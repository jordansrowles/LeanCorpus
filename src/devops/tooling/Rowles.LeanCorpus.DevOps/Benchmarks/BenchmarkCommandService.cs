using System.Collections.Immutable;
using System.Text.Json;
using LibGit2Sharp;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Data;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal sealed record BenchmarkInvocation(
    string ProjectKey,
    string ProjectPath,
    IReadOnlyList<string> Arguments,
    string? BenchmarkClass = null,
    string? BenchmarkFilter = null)
{
    public IReadOnlyDictionary<string, ImmutableArray<string>>? ParameterValues { get; init; }

    public int? DocumentCount { get; init; }
}

internal sealed record BenchmarkExcludedWorkload(
    string SuiteId,
    string ProjectKey,
    IReadOnlyList<string> BenchmarkClasses,
    string State,
    string Reason,
    string RerunSuite);

internal sealed record BenchmarkExecutionPlan(
    string Suite,
    string Strategy,
    string Framework,
    string Dataset,
    int DocumentCount,
    bool CorpusOnly,
    string? ReferencePath,
    IReadOnlyList<BenchmarkInvocation> Invocations,
    IReadOnlyList<string> AffectedPaths,
    IReadOnlyList<string> AffectedGroups)
{
    public BenchmarkTimeoutBudget TimeoutBudget { get; init; } = new(360, 30, 360, "strategy");

    public IReadOnlyList<string> BenchmarkDotNetArguments { get; init; } = [];

    public IReadOnlyList<string> SelectedCaseIds { get; init; } = [];

    public int SelectedCaseCount { get; init; }

    public int CaseBudget { get; init; }

    public IReadOnlyList<BenchmarkExcludedWorkload> ExcludedWorkloads { get; init; } = [];
}

internal sealed record BenchmarkTimeoutBudget(
    int AggregateTimeoutMinutes,
    int BuildTimeoutMinutes,
    int ProjectTimeoutMinutes,
    string AggregateTimeoutSource);

internal sealed record BenchmarkProjectResult(
    string Project,
    string Status,
    int? ExitCode,
    string Path,
    string? Failure,
    TimeSpan? BuildDuration = null,
    TimeSpan? RunDuration = null,
    string? TimeoutSource = null);

internal sealed record BenchmarkCommandResult(CommandResult CommandResult, Guid? RunId, string? RunDirectory);

internal static class BenchmarkCommandPlanner
{
    public static BenchmarkExecutionPlan Create(
        BenchmarkCommandOptions options,
        DevOpsConfiguration configuration,
        string repositoryRoot,
        IReadOnlyList<string>? changedPaths = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var requestedSuite = options.Suite.Trim().ToLowerInvariant();
        var suite = ResolveSuiteId(configuration, requestedSuite, out var suiteDescriptor);
        bool hasCaseSelectors = HasCaseSelectors(options);
        var dataset = string.IsNullOrWhiteSpace(options.Dataset)
            ? configuration.DefaultBenchmarkDataset
            : options.Dataset.Trim().ToLowerInvariant();
        var resolvedOptions = options with { Dataset = dataset };
        if (suite == "affected")
        {
            if (hasCaseSelectors)
                throw new InvalidOperationException("Affected benchmark selection cannot be combined with semantic case selectors.");
            if (changedPaths is null)
                throw new InvalidOperationException("Affected benchmark selection requires a working-tree change inventory.");
            return CreateAffected(resolvedOptions, configuration, repositoryRoot, changedPaths);
        }

        var strategyName = options.Strategy.Trim().ToLowerInvariant();
        if (suite == "remote")
            throw new InvalidOperationException($"The benchmark '{suite}' workflow requires the managed benchmark command route.");
        if (suite != "all" && !configuration.BenchmarkProjects.ContainsKey(suite) && suiteDescriptor is null)
            throw new InvalidOperationException($"Unknown benchmark suite '{suite}'. Use --list to see available suites.");
        if (!configuration.BenchmarkStrategies.TryGetValue(strategyName, out var strategy))
            throw new InvalidOperationException($"Unknown benchmark strategy '{strategyName}'. Use --list to see available strategies.");
        if (options.TimeoutMinutes is int timeoutMinutes && timeoutMinutes is < 1 or > BenchmarkStrategyConfiguration.MaximumTimeoutMinutes)
        {
            throw new InvalidOperationException(
                $"Timeout must be between 1 and {BenchmarkStrategyConfiguration.MaximumTimeoutMinutes} minutes.");
        }
        var timeoutBudget = new BenchmarkTimeoutBudget(
            options.TimeoutMinutes ?? strategy.AggregateTimeoutMinutes,
            strategy.BuildTimeoutMinutes,
            strategy.ProjectTimeoutMinutes,
            options.TimeoutMinutes is null ? "strategy" : "command-line");
        if (options.DocumentCount < 0)
            throw new InvalidOperationException("Document count cannot be negative.");
        if (options.BookCount < 1)
            throw new InvalidOperationException("Gutenberg book count must be a positive integer.");
        if (dataset != configuration.DefaultBenchmarkDataset && !configuration.BenchmarkDatasets.ContainsKey(dataset))
        {
            var supportedDatasets = new[] { configuration.DefaultBenchmarkDataset }
                .Concat(configuration.BenchmarkDatasets.Keys)
                .OrderBy(static value => value, StringComparer.Ordinal);
            throw new InvalidOperationException($"Unknown benchmark dataset '{dataset}'. Choose {string.Join(" or ", supportedDatasets)}.");
        }
        if (options.SourceManifest is not null && string.IsNullOrWhiteSpace(options.SourceManifest))
            throw new InvalidOperationException("Source manifest must be a non-empty path when specified.");

        var selectedProjects = ResolveProjects(configuration, suite, suiteDescriptor);
        var areaNames = SplitNames(options.Area);
        var groupNames = SplitNames(options.Group);
        if ((areaNames.Length != 0 || groupNames.Length != 0) && !hasCaseSelectors && suite is not ("core" or "text"))
            throw new InvalidOperationException("Area and group selection require the core or text benchmark suite.");
        if ((areaNames.Length != 0 || groupNames.Length != 0) && !hasCaseSelectors && selectedProjects.Any(project =>
                !configuration.BenchmarkProjects[project].Capabilities.Contains("Groups", StringComparer.Ordinal)))
            throw new InvalidOperationException("Area and group selection require a benchmark project with group support.");
        if ((areaNames.Length != 0 || groupNames.Length != 0) && options.PrepareData)
            throw new InvalidOperationException("PrepareData cannot be combined with area or group selection.");
        if (dataset == "wikipedia")
        {
            if (!configuration.BenchmarkDatasets.TryGetValue("wikipedia", out var wikipediaSuites) || !wikipediaSuites.Contains(suite, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Suite '{suite}' is not supported with the Wikipedia reference dataset.");
            if (areaNames.Length != 0 || groupNames.Length != 0)
                throw new InvalidOperationException("Area and group selection are not supported with the Wikipedia reference dataset.");
            if (options.PrepareData)
                throw new InvalidOperationException("PrepareData is not supported with the Wikipedia reference dataset.");
            if (suite is "merge" or "flush" && ContainsFilter(options.Arguments))
                throw new InvalidOperationException($"Wikipedia '{suite}' runs select only the plain-text benchmark method; custom filters are not supported.");
        }
        else if (options.ReferencePath is not null)
        {
            throw new InvalidOperationException("ReferencePath requires the Wikipedia dataset.");
        }

        var needsGutenbergData = NeedsGutenbergData(configuration, suiteDescriptor, selectedProjects, options.Arguments);
        if (options.PrepareData && !needsGutenbergData)
            throw new InvalidOperationException($"PrepareData is not required or supported for benchmark suite '{suite}'.");

        if ((areaNames.Length != 0 || groupNames.Length != 0) && ContainsFilter(options.Arguments) && !hasCaseSelectors)
            throw new InvalidOperationException("A custom BenchmarkDotNet filter cannot be combined with area or group selection.");
        if (suiteDescriptor is { BenchmarkClasses.IsDefaultOrEmpty: false } && ContainsFilter(options.Arguments) && !hasCaseSelectors)
            throw new InvalidOperationException("A custom BenchmarkDotNet filter cannot be combined with a configured benchmark class selection.");

        foreach (var projectKey in selectedProjects)
        {
            if (!configuration.BenchmarkProjects[projectKey].Frameworks.Contains(options.Framework, StringComparer.Ordinal))
                throw new InvalidOperationException($"Benchmark project '{projectKey}' does not support framework '{options.Framework}'.");
        }

        var documentCount = options.DocumentCount > 0 ? options.DocumentCount : strategy.DocCount;
        if (options.Controlled)
        {
            if (documentCount <= 0)
                documentCount = 1000;
        }
        if (dataset == "wikipedia" && documentCount > 20_000)
            throw new InvalidOperationException($"The Wikipedia reference contains 20,000 records; requested document count {documentCount} exceeds that limit.");
        var corpusOnly = options.CorpusOnly || suiteDescriptor?.CorpusOnly == true ||
            (options.Controlled && (suiteDescriptor?.ControlledCorpusOnly ?? true));
        var referencePath = dataset == "wikipedia"
            ? ResolvePath(repositoryRoot, options.ReferencePath ?? "artifacts/dataforge/reference/leancorpus-wikipedia-en-v1")
            : null;

        if (hasCaseSelectors)
        {
            var selection = SelectCaseMethods(configuration, options, dataset, selectedProjects, suiteDescriptor, areaNames, groupNames);
            var passThrough = RemoveFilterArguments(options.Arguments);
            var selectedInvocations = selection.Methods.Select(method =>
            {
                int? invocationDocumentCount = ReadSingleDocumentCount(method.ParameterValues);
                int argumentDocumentCount = method.ParameterValues.ContainsKey("DocumentCount")
                    ? invocationDocumentCount ?? 0
                    : documentCount;
                string filter = $"*{method.Family.BenchmarkClass}.{method.Method}";
                int? effectiveInvocationDocumentCount = method.ParameterValues.ContainsKey("DocumentCount")
                    ? invocationDocumentCount
                    : documentCount;
                return new BenchmarkInvocation(
                    method.Family.Project,
                    GetProjectPath(configuration, method.Family.Project, repositoryRoot),
                    BuildArguments(method.Family.Project,
                        method.Family.Project,
                        strategy.Job, passThrough,
                        argumentDocumentCount, corpusOnly, options.GcDump, dataset, method.Family.BenchmarkClass, filter,
                        exactBenchmarkClass: method.Family.Project == "core"),
                    method.Family.BenchmarkClass,
                    filter)
                {
                    ParameterValues = method.ParameterValues,
                    DocumentCount = effectiveInvocationDocumentCount
                };
            }).ToArray();
            int plannedDocumentCount = ResolveSelectedDocumentCount(selection.Methods, documentCount);
            return new BenchmarkExecutionPlan(suite, strategyName, options.Framework, dataset, plannedDocumentCount,
                corpusOnly, referencePath, selectedInvocations, [], [])
            {
                TimeoutBudget = timeoutBudget,
                BenchmarkDotNetArguments = RemoveFilterArguments(options.Arguments).ToArray(),
                SelectedCaseIds = selection.Methods.Select(static method => method.Family.Id)
                    .Distinct(StringComparer.Ordinal).OrderBy(static id => id, StringComparer.Ordinal).ToArray(),
                SelectedCaseCount = selection.CaseCount,
                CaseBudget = options.MaxCases,
                ExcludedWorkloads = ResolveExcludedWorkloads(configuration, suite, selectedProjects)
            };
        }

        var invocations = new List<BenchmarkInvocation>();

        foreach (var project in selectedProjects)
        {
            var benchmarks = areaNames.Length == 0 && groupNames.Length == 0
                ? []
                : SelectBenchmarks(configuration, project, areaNames, groupNames);
            if (areaNames.Length != 0 || groupNames.Length != 0)
            {
                if (benchmarks.Length == 0)
                    throw new InvalidOperationException($"No benchmark groups matched suite '{suite}', Area='{options.Area}', Group='{options.Group}'.");
                foreach (var benchmark in benchmarks)
                {
                    invocations.Add(new BenchmarkInvocation(project, GetProjectPath(configuration, project, repositoryRoot),
                        BuildArguments(project, suite, strategy.Job, options.Arguments, documentCount, corpusOnly, options.GcDump, dataset, benchmark), benchmark));
                }
            }
            else if (suiteDescriptor is { BenchmarkClasses.IsDefaultOrEmpty: false } && suiteDescriptor.Project.Equals(project, StringComparison.Ordinal))
            {
                foreach (var benchmark in suiteDescriptor.BenchmarkClasses)
                {
                    invocations.Add(new BenchmarkInvocation(project, GetProjectPath(configuration, project, repositoryRoot),
                        BuildArguments(project, suite, strategy.Job, options.Arguments, documentCount, corpusOnly, options.GcDump, dataset, benchmark), benchmark));
                }
            }
            else
            {
                invocations.Add(new BenchmarkInvocation(project, GetProjectPath(configuration, project, repositoryRoot),
                    BuildArguments(project, suite, strategy.Job, options.Arguments, documentCount, corpusOnly, options.GcDump, dataset, null)));
            }
        }

        return new BenchmarkExecutionPlan(suite, strategyName, options.Framework, dataset, documentCount, corpusOnly, referencePath,
            invocations.ToImmutableArray(), [], [])
        {
            TimeoutBudget = timeoutBudget,
            BenchmarkDotNetArguments = RemoveFilterArguments(options.Arguments).ToArray(),
            ExcludedWorkloads = ResolveExcludedWorkloads(configuration, suite, selectedProjects)
        };
    }

    private static IReadOnlyList<BenchmarkExcludedWorkload> ResolveExcludedWorkloads(
        DevOpsConfiguration configuration,
        string suite,
        IReadOnlyList<string> selectedProjects)
    {
        bool includePrimary = suite is "all" or "all-with-explicit";
        bool includeExplicit = suite is "explicit" or "all-with-explicit";
        if (!includePrimary && !includeExplicit)
            return [];

        var selectedProjectSet = selectedProjects.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return configuration.BenchmarkSuites
            .Where(pair => !pair.Value.IsSelector && selectedProjectSet.Contains(pair.Value.Project))
            .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                var descriptor = pair.Value;
                bool includeInAll = descriptor.IncludeInAll ?? (!descriptor.ExplicitOnly && !descriptor.RequiresGutenbergData);
                bool includeWithExplicit = descriptor.IncludeWithExplicit ?? descriptor.ExplicitOnly;
                bool selected = (includePrimary && includeInAll) || (includeExplicit && includeWithExplicit);
                var reason = descriptor.ExplicitOnly || (!includeInAll && includeWithExplicit)
                    ? "explicit-only"
                    : descriptor.RequiresGutenbergData
                        ? "requires-gutenberg-data"
                        : "outside-selector";
                return (pair.Key, descriptor.Project, descriptor.BenchmarkClasses, Selected: selected, Reason: reason);
            })
            .Where(static item => !item.Selected)
            .Select(static item => new BenchmarkExcludedWorkload(
                item.Key,
                item.Project,
                item.BenchmarkClasses,
                "excluded",
                item.Reason,
                item.Key))
            .ToArray();
    }

    private static bool HasCaseSelectors(BenchmarkCommandOptions options)
        => !string.IsNullOrWhiteSpace(options.Profile) || !string.IsNullOrWhiteSpace(options.Case) ||
           !string.IsNullOrWhiteSpace(options.Engine) || !string.IsNullOrWhiteSpace(options.WorkloadId);

    private static BenchmarkCaseSelection SelectCaseMethods(
        DevOpsConfiguration configuration,
        BenchmarkCommandOptions options,
        string dataset,
        IReadOnlyList<string> selectedProjects,
        BenchmarkSuiteConfiguration? suiteDescriptor,
        IReadOnlyList<string> areas,
        IReadOnlyList<string> groups)
    {
        if (options.MaxCases is < 1 or > 64)
            throw new InvalidOperationException("MaxCases must be between 1 and 64 for bounded manifest selection.");

        string[] profiles = SplitNames(options.Profile);
        string[] caseIds = SplitNames(options.Case);
        string[] engines = SplitNames(options.Engine);
        string[] workloads = SplitNames(options.WorkloadId);
        string[] filterPatterns = ExtractFilterPatterns(options.Arguments).ToArray();
        var allowedClasses = suiteDescriptor is { BenchmarkClasses.IsDefaultOrEmpty: false }
            ? suiteDescriptor.BenchmarkClasses.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        var candidates = configuration.BenchmarkCases
            .Where(family => selectedProjects.Contains(family.Project, StringComparer.OrdinalIgnoreCase))
            .Where(family => allowedClasses is null || allowedClasses.Contains(family.BenchmarkClass))
            .Where(family => dataset != "wikipedia" || family.DatasetProfile.StartsWith("wikipedia", StringComparison.OrdinalIgnoreCase))
            .Where(family => profiles.Length == 0 || profiles.Contains(family.DatasetProfile, StringComparer.OrdinalIgnoreCase))
            .Where(family => caseIds.Length == 0 || caseIds.Contains(family.Id, StringComparer.OrdinalIgnoreCase))
            .Where(family => engines.Length == 0 || engines.Contains(family.EngineId, StringComparer.OrdinalIgnoreCase))
            .Where(family => workloads.Length == 0 || workloads.Contains(family.WorkloadId, StringComparer.OrdinalIgnoreCase))
            .Where(family => areas.Count == 0 || areas.Contains(family.Category, StringComparer.OrdinalIgnoreCase))
            .Where(family => MatchesGroupSelection(family, groups, configuration))
            .ToArray();

        var selectedMethods = candidates
            .SelectMany(family => family.BenchmarkMethods
                .Where(method => filterPatterns.Length == 0 || filterPatterns.Any(pattern =>
                    System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(
                        pattern, $"{family.BenchmarkClass}.{method}", ignoreCase: true)))
                .Select(method => CreateSelectedCaseMethod(family, method, options.DocumentCount)))
            .OrderBy(static selected => selected.Family.Project, StringComparer.Ordinal)
            .ThenBy(static selected => selected.Family.BenchmarkClass, StringComparer.Ordinal)
            .ThenBy(static selected => selected.Method, StringComparer.Ordinal)
            .ToArray();
        if (selectedMethods.Length == 0)
            throw new InvalidOperationException("No benchmark cases remain after intersecting suite, profile, case, engine, area, group, dataset, and filter selectors.");
        long selectedCaseCount = selectedMethods.Sum(static selected => selected.CaseCount);
        if (selectedCaseCount > options.MaxCases)
            throw new InvalidOperationException($"The selection contains {selectedCaseCount} benchmark cases, above the --max-cases budget of {options.MaxCases}. Narrow the selection or raise the budget (maximum 64).");

        return new BenchmarkCaseSelection(selectedMethods, checked((int)selectedCaseCount));
    }

    private static SelectedCaseMethod CreateSelectedCaseMethod(
        BenchmarkCaseConfiguration family,
        string method,
        int requestedDocumentCount)
    {
        var parameterValues = family.ParameterValues;
        if (requestedDocumentCount > 0 && parameterValues.TryGetValue("DocumentCount", out var documentCounts))
        {
            string requested = requestedDocumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!documentCounts.Contains(requested, StringComparer.Ordinal))
                throw new InvalidOperationException(
                    $"Requested document count {requestedDocumentCount} does not match the parameter values registered for benchmark case '{family.Id}'.");
            parameterValues = parameterValues.SetItem("DocumentCount", [requested]);
        }

        long caseCount = 1;
        foreach (var values in parameterValues.Values)
        {
            if (values.IsDefaultOrEmpty)
                throw new InvalidOperationException($"Benchmark case '{family.Id}' has an empty parameter value list.");
            caseCount = caseCount > long.MaxValue / values.Length
                ? long.MaxValue
                : caseCount * values.Length;
        }

        return new SelectedCaseMethod(family, method, parameterValues, caseCount);
    }

    private static int? ReadSingleDocumentCount(IReadOnlyDictionary<string, ImmutableArray<string>> parameterValues)
    {
        if (!parameterValues.TryGetValue("DocumentCount", out var values) || values.Length != 1)
            return null;
        return int.TryParse(values[0], System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out int documentCount) && documentCount > 0
            ? documentCount
            : throw new InvalidOperationException($"Invalid DocumentCount benchmark parameter '{values[0]}'.");
    }

    private static int ResolveSelectedDocumentCount(IReadOnlyList<SelectedCaseMethod> methods, int defaultDocumentCount)
    {
        var selectedCounts = methods.SelectMany(method =>
            method.ParameterValues.TryGetValue("DocumentCount", out var values)
                ? values.Select(value => int.TryParse(value, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int parsed) && parsed > 0
                    ? parsed
                    : throw new InvalidOperationException($"Invalid DocumentCount benchmark parameter '{value}'."))
                : [defaultDocumentCount])
            .Distinct()
            .Take(2)
            .ToArray();
        return selectedCounts.Length == 1 ? selectedCounts[0] : 0;
    }

    private static bool MatchesGroupSelection(
        BenchmarkCaseConfiguration family,
        IReadOnlyList<string> groups,
        DevOpsConfiguration configuration)
    {
        if (groups.Count == 0)
            return true;
        foreach (string group in groups)
        {
            if (string.Equals(family.ComparisonGroup, group, StringComparison.OrdinalIgnoreCase))
                return true;
            if (configuration.BenchmarkGroups.TryGetValue(group, out BenchmarkGroupConfiguration? configured) &&
                configured.Project.Equals(family.Project, StringComparison.OrdinalIgnoreCase) &&
                configured.Benchmarks.Contains(family.BenchmarkClass, StringComparer.Ordinal))
                return true;
        }
        return false;
    }

    private static IEnumerable<string> ExtractFilterPatterns(IReadOnlyList<string> arguments)
    {
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase))
                yield return argument[9..];
            else if (argument.StartsWith("-f=", StringComparison.OrdinalIgnoreCase))
                yield return argument[3..];
            else if (argument.Equals("--filter", StringComparison.OrdinalIgnoreCase) || argument.Equals("-f", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < arguments.Count && !arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
                    yield return arguments[++index];
            }
        }
    }

    private static string[] RemoveFilterArguments(IReadOnlyList<string> arguments)
    {
        var result = new List<string>(arguments.Count);
        for (int index = 0; index < arguments.Count; index++)
        {
            string argument = arguments[index];
            if (argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase) || argument.StartsWith("-f=", StringComparison.OrdinalIgnoreCase))
                continue;
            if (argument.Equals("--filter", StringComparison.OrdinalIgnoreCase) || argument.Equals("-f", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 < arguments.Count && !arguments[index + 1].StartsWith("-", StringComparison.Ordinal))
                    index++;
                continue;
            }
            result.Add(argument);
        }
        return result.ToArray();
    }

    private sealed record SelectedCaseMethod(
        BenchmarkCaseConfiguration Family,
        string Method,
        ImmutableDictionary<string, ImmutableArray<string>> ParameterValues,
        long CaseCount);

    private sealed record BenchmarkCaseSelection(IReadOnlyList<SelectedCaseMethod> Methods, int CaseCount);

    private static BenchmarkExecutionPlan CreateAffected(
        BenchmarkCommandOptions options,
        DevOpsConfiguration configuration,
        string repositoryRoot,
        IReadOnlyList<string> changedPaths)
    {
        if (!options.Dataset.Equals(configuration.DefaultBenchmarkDataset, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Affected benchmark selection supports only the default dataset '{configuration.DefaultBenchmarkDataset}'.");
        if (options.PrepareData)
            throw new InvalidOperationException("Affected benchmark selection cannot prepare benchmark data.");
        if (ContainsFilter(options.Arguments))
            throw new InvalidOperationException("A custom BenchmarkDotNet filter cannot be combined with affected class selection.");

        var baseOptions = options with { Suite = "all", Area = null, Group = null };
        var basePlan = Create(baseOptions, configuration, repositoryRoot);
        var selection = AffectedBenchmarkPlanner.Create(configuration, changedPaths, options.Area, options.Group);
        var invocations = selection.BenchmarksByProject
            .SelectMany(pair => pair.Value.Select(benchmark => new BenchmarkInvocation(
                pair.Key,
                GetProjectPath(configuration, pair.Key, repositoryRoot),
                BuildArguments(pair.Key, pair.Key, configuration.BenchmarkStrategies[basePlan.Strategy].Job,
                    options.Arguments, basePlan.DocumentCount, basePlan.CorpusOnly, options.GcDump,
                    basePlan.Dataset, benchmark), benchmark)))
            .ToArray();

        return basePlan with
        {
            Suite = "affected",
            Invocations = invocations,
            AffectedPaths = selection.ChangedPaths,
            AffectedGroups = selection.MatchedGroups,
            ExcludedWorkloads = []
        };
    }

    private static string[] BuildArguments(
        string project,
        string suite,
        IReadOnlyList<string> jobArguments,
        IReadOnlyList<string> passThrough,
        int documentCount,
        bool corpusOnly,
        bool gcDump,
        string dataset,
        string? benchmarkClass,
        string? benchmarkFilter = null,
        bool exactBenchmarkClass = false)
    {
        var arguments = new List<string>();
        if (project == "core")
        {
            string selectedSuite = exactBenchmarkClass
                ? "all-with-explicit"
                : suite is "all" or "core" ? "all" : suite;
            arguments.AddRange(["--suite", selectedSuite]);
            if (exactBenchmarkClass && benchmarkClass is not null)
                arguments.AddRange(["--benchmark-class", benchmarkClass]);
            if (documentCount > 0)
                arguments.AddRange(["--doccount", documentCount.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            if (corpusOnly)
                arguments.Add("--corpus-only");
            if (gcDump)
                arguments.Add("--gcdump");
            if (dataset == "wikipedia" && suite == "merge")
                arguments.AddRange(["--filter", "*LeanCorpus_Merge_PlainText*"]);
            if (dataset == "wikipedia" && suite == "flush")
                arguments.AddRange(["--filter", "*LeanCorpus_Flush_TextOnly*"]);
            if (benchmarkClass is not null)
                arguments.Add("--");
        }
        arguments.AddRange(jobArguments);
        arguments.AddRange(passThrough);
        if (benchmarkFilter is not null)
            arguments.AddRange(["--filter", benchmarkFilter]);
        else if (benchmarkClass is not null)
            arguments.AddRange(["--filter", $"*{benchmarkClass}*"]);
        return arguments.ToArray();
    }

    private static string[] SelectBenchmarks(
        DevOpsConfiguration configuration,
        string project,
        IReadOnlyList<string> areas,
        IReadOnlyList<string> groups)
    {
        var relevant = configuration.BenchmarkGroups
            .Where(pair => pair.Value.Project.Equals(project, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var knownAreas = relevant.Select(pair => pair.Value.Area).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownGroups = relevant.Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownArea = areas.FirstOrDefault(area => !knownAreas.Contains(area));
        if (unknownArea is not null)
            throw new InvalidOperationException($"Unknown benchmark area '{unknownArea}' for suite '{project}'.");
        var unknownGroup = groups.FirstOrDefault(group => !knownGroups.Contains(group));
        if (unknownGroup is not null)
            throw new InvalidOperationException($"Unknown benchmark group '{unknownGroup}' for suite '{project}'.");

        return relevant
            .Where(pair => (areas.Count == 0 || areas.Contains(pair.Value.Area, StringComparer.OrdinalIgnoreCase)) &&
                (groups.Count == 0 || groups.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)))
            .SelectMany(pair => pair.Value.Benchmarks)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] SplitNames(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool ContainsFilter(IReadOnlyList<string> arguments)
        => arguments.Any(argument => argument.Equals("--filter", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("-f=", StringComparison.OrdinalIgnoreCase));

    private static string ResolveSuiteId(DevOpsConfiguration configuration, string requestedSuite,
        out BenchmarkSuiteConfiguration? descriptor)
    {
        if (configuration.BenchmarkSuites.TryGetValue(requestedSuite, out descriptor))
            return requestedSuite;

        foreach (var (suiteId, candidate) in configuration.BenchmarkSuites.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            if (!candidate.Aliases.Contains(requestedSuite, StringComparer.OrdinalIgnoreCase))
                continue;
            descriptor = candidate;
            return suiteId;
        }

        descriptor = null;
        return requestedSuite;
    }

    private static string[] ResolveProjects(DevOpsConfiguration configuration, string suite,
        BenchmarkSuiteConfiguration? suiteDescriptor)
    {
        if (suite == "all")
        {
            return configuration.BenchmarkProjects
                .Where(static pair => pair.Value.IncludeInAll)
                .Select(static pair => pair.Key)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();
        }

        if (suite == "all-with-explicit")
        {
            return configuration.BenchmarkProjects
                .Where(static pair => pair.Value.IncludeWithExplicit)
                .Select(static pair => pair.Key)
                .OrderBy(static key => key, StringComparer.Ordinal)
                .ToArray();
        }

        if (configuration.BenchmarkProjects.ContainsKey(suite))
            return [suite];

        if (suiteDescriptor is null || !configuration.BenchmarkProjects.ContainsKey(suiteDescriptor.Project))
            throw new InvalidOperationException($"Benchmark suite '{suite}' refers to an unavailable project.");
        return [suiteDescriptor.Project];
    }

    private static bool NeedsGutenbergData(DevOpsConfiguration configuration, BenchmarkSuiteConfiguration? suite,
        IReadOnlyList<string> selectedProjects, IReadOnlyList<string> arguments)
        => suite?.RequiresGutenbergData == true ||
            (selectedProjects.Any(project => configuration.BenchmarkProjects[project].Capabilities.Contains("Gutenberg", StringComparer.Ordinal)) &&
                arguments.Any(argument => argument.Contains("GutenbergAnalysisBenchmarks", StringComparison.OrdinalIgnoreCase)));

    private static string GetProjectPath(DevOpsConfiguration configuration, string project, string repositoryRoot)
        => Path.Combine(repositoryRoot, configuration.BenchmarkProjects[project].Project.Replace('/', Path.DirectorySeparatorChar));

    private static string ResolvePath(string repositoryRoot, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repositoryRoot, path));
}

internal sealed class BenchmarkCommandService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    Action<string>? progress = null,
    Action<string>? errorOutput = null,
    string dotnetExecutable = "dotnet")
{
    internal static readonly string BenchmarkHostLeaseRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus.DevOps.BenchmarkHost");
    private static readonly TimeSpan BenchmarkLeaseWait = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<BenchmarkCommandResult> RunAsync(BenchmarkCommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Options.Workload is not null)
            return await new BenchmarkWorkloadCommandService(artifactPaths, runStore, processRunner,
                sdkResolver, provenanceReader, progress, errorOutput, dotnetExecutable)
                .RunAsync(request, cancellationToken).ConfigureAwait(false);
        var configuration = new ConfigurationLoader(artifactPaths.RepositoryRoot).Load();
        if (request.Options.List)
        {
            var suites = configuration.BenchmarkProjects.Keys
                .Concat(configuration.BenchmarkSuites.Keys)
                .Append("all")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase);
            progress?.Invoke("Available benchmark suites: " + string.Join(", ", suites));
            progress?.Invoke("Available strategies: " + string.Join(", ", configuration.BenchmarkStrategies.Keys.OrderBy(static value => value, StringComparer.Ordinal)));
            progress?.Invoke("Datasets: " + string.Join(", ", new[] { configuration.DefaultBenchmarkDataset }
                .Concat(configuration.BenchmarkDatasets.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static value => value, StringComparer.Ordinal)));
            progress?.Invoke("Benchmark projects: " + string.Join(", ", configuration.BenchmarkProjects
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => $"{pair.Key} ({pair.Value.DisplayName})")));
            progress?.Invoke("Profiles: " + string.Join(", ", configuration.BenchmarkCases.Select(static item => item.DatasetProfile)
                .Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal)));
            progress?.Invoke("Engines: " + string.Join(", ", configuration.BenchmarkCases.Select(static item => item.EngineId)
                .Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal)));
            progress?.Invoke("Workloads: " + string.Join(", ", configuration.BenchmarkCases.Select(static item => item.WorkloadId)
                .Distinct(StringComparer.Ordinal).OrderBy(static value => value, StringComparer.Ordinal)));
            progress?.Invoke("Cases: " + string.Join(", ", configuration.BenchmarkCases.Select(static item => item.Id)
                .OrderBy(static value => value, StringComparer.Ordinal)));
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.Success, "Benchmark options listed."), null, null);
        }

        BenchmarkExecutionPlan plan;
        try
        {
            var changedPaths = request.Options.Suite.Equals("affected", StringComparison.OrdinalIgnoreCase)
                ? ReadChangedPaths(request.Checkout.RepositoryRoot)
                : null;
            plan = BenchmarkCommandPlanner.Create(request.Options, configuration, artifactPaths.RepositoryRoot, changedPaths);
        }
        catch (InvalidOperationException exception)
        {
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), null, null);
        }

        if (request.Options.PrepareData)
        {
            using var httpClient = new HttpClient();
            var download = await new GutenbergDownloadService(httpClient, progress).DownloadAsync(new GutenbergDownloadRequest
            {
                RepositoryRoot = artifactPaths.RepositoryRoot,
                BookCount = request.Options.BookCount
            }, cancellationToken).ConfigureAwait(false);
            if (download.CommandResult.Outcome != OutcomeKind.Success)
                return new BenchmarkCommandResult(download.CommandResult, null, null);
        }

        if (request.Options.Dry)
        {
            progress?.Invoke(FormatTimeoutBudget(plan.TimeoutBudget));
            if (plan.SelectedCaseCount > 0)
                progress?.Invoke($"Bounded manifest selection: {plan.SelectedCaseCount} cases across {plan.SelectedCaseIds.Count} families; budget {plan.CaseBudget}. Cases: {string.Join(", ", plan.SelectedCaseIds)}.");
            foreach (var invocation in plan.Invocations)
            {
                if (invocation.ParameterValues is { Count: > 0 } parameterValues)
                    progress?.Invoke($"Benchmark parameter selection for {invocation.ProjectKey}/{invocation.BenchmarkClass}: {JsonSerializer.Serialize(parameterValues)}; documentCount={invocation.DocumentCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "from parameters"}.");
                progress?.Invoke("dotnet " + string.Join(" ", QuoteArguments(BuildDotNetBuildArguments(invocation, plan))));
                progress?.Invoke("dotnet " + string.Join(" ", QuoteArguments(BuildDotNetArguments(invocation, plan))));
            }
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.Success, "Benchmark dry run completed; no benchmark targets were executed."), null, null);
        }

        if (plan.SelectedCaseCount > 0)
            progress?.Invoke($"Bounded manifest selection: {plan.SelectedCaseCount} cases across {plan.SelectedCaseIds.Count} families; budget {plan.CaseBudget}. Cases: {string.Join(", ", plan.SelectedCaseIds)}.");
        progress?.Invoke(FormatTimeoutBudget(plan.TimeoutBudget));

        if (request.Options.SourceManifest is { } sourceManifest && !File.Exists(ResolvePath(sourceManifest, artifactPaths.RepositoryRoot)))
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure, $"Source manifest '{sourceManifest}' does not exist."), null, null);
        if (request.Options.GcDump && !await IsDotNetToolAvailableAsync(request.Checkout, cancellationToken).ConfigureAwait(false))
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure, "The dotnet-gcdump tool is required for --gc-dump."), null, null);

        var hostIdentity = BenchmarkHostIdentity.Capture();
        var runInputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["suite"] = plan.Suite,
            ["strategy"] = plan.Strategy,
            ["framework"] = plan.Framework,
            ["dataset"] = plan.Dataset,
            ["documentCount"] = plan.DocumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["projectKeys"] = string.Join(",", plan.Invocations.Select(static invocation => invocation.ProjectKey).Distinct(StringComparer.Ordinal)),
            ["aggregateTimeoutMinutes"] = plan.TimeoutBudget.AggregateTimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["aggregateTimeoutSource"] = plan.TimeoutBudget.AggregateTimeoutSource,
            ["buildTimeoutMinutes"] = plan.TimeoutBudget.BuildTimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["projectTimeoutMinutes"] = plan.TimeoutBudget.ProjectTimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["selectedCaseIds"] = JsonSerializer.Serialize(plan.SelectedCaseIds),
            ["selectedCaseInvocations"] = JsonSerializer.Serialize(plan.Invocations
                .Where(static invocation => invocation.ParameterValues is not null)
                .Select(static invocation => new
                {
                    invocation.ProjectKey,
                    invocation.BenchmarkClass,
                    invocation.BenchmarkFilter,
                    invocation.ParameterValues,
                    invocation.DocumentCount
                })),
            ["arguments"] = JsonSerializer.Serialize(request.Options.Arguments),
            ["referencePath"] = plan.ReferencePath ?? string.Empty,
            ["sourceCommit"] = request.Options.SourceCommit ?? string.Empty,
            ["sourceRef"] = request.Options.SourceRef ?? string.Empty,
            ["sourceManifest"] = request.Options.SourceManifest is null ? string.Empty : ResolvePath(request.Options.SourceManifest, artifactPaths.RepositoryRoot),
            ["controlled"] = request.Options.Controlled.ToString().ToLowerInvariant(),
            ["corpusOnly"] = plan.CorpusOnly.ToString().ToLowerInvariant(),
            ["gcDump"] = request.Options.GcDump.ToString().ToLowerInvariant(),
            ["benchmarkLock"] = "host-exclusive"
        };
        foreach (var (name, value) in hostIdentity.ToRunInputs())
            runInputs[name] = value;
        runInputs["identityStatus"] = "Pending";
        runInputs["qualificationState"] = "Unqualified";
        runInputs["eligibleForRatio"] = "false";
        runInputs["comparable"] = "false";
        runInputs["qualificationReasonCodes"] = JsonSerializer.Serialize(new[] { "IdentityValidationPending" });
        if (plan.Suite == "affected")
        {
            runInputs["affectedPaths"] = JsonSerializer.Serialize(plan.AffectedPaths);
            runInputs["affectedGroups"] = JsonSerializer.Serialize(plan.AffectedGroups);
        }
        var manifest = await runStore.StartAsync("benchmark", runInputs, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = artifactPaths.GetRunDirectory("benchmark", manifest.RunId);
        var childExitCodes = new List<int>();
        var artifacts = new List<string>();
        var results = new List<BenchmarkProjectResult>();
        var failure = (OutcomeKind?)null;
        string? failureDetails = null;
        using var runTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(plan.TimeoutBudget.AggregateTimeoutMinutes));
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runTimeout.Token);
        var runToken = runCancellation.Token;
        var runStartedTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        string? timeoutSource = null;

        try
        {
            var provenance = await provenanceReader.ReadAsync(request.Checkout, runDirectory, runToken).ConfigureAwait(false);
            if (provenance.Outcome != OutcomeKind.Success)
                return await FinishAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure, provenance.Failure, childExitCodes, artifacts, results).ConfigureAwait(false);
            manifest = await runStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
                cancellationToken: runToken).ConfigureAwait(false);

            var sdk = await sdkResolver.ResolveAsync(request.Checkout, runDirectory, runToken).ConfigureAwait(false);
            if (sdk.Outcome != OutcomeKind.Success)
                return await FinishAsync(manifest, runDirectory, sdk.Outcome, sdk.Failure, childExitCodes, artifacts, results).ConfigureAwait(false);
            manifest = await runStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dotnet-sdk-requested"] = sdk.RequestedVersion ?? string.Empty,
                    ["dotnet-sdk-selected"] = sdk.SelectedVersion ?? string.Empty,
                    ["dotnet-sdk-roll-forward"] = sdk.RollForward ?? "default"
                }, runToken).ConfigureAwait(false);

            var referenceIdentity = plan.Dataset == "wikipedia"
                ? await VerifyWikipediaReferenceAsync(request.Checkout, plan, runDirectory, artifacts, childExitCodes, runToken).ConfigureAwait(false)
                : null;
            if (plan.Dataset == "wikipedia" && referenceIdentity is null)
                return await FinishAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure,
                    "The frozen Wikipedia reference could not be verified.", childExitCodes, artifacts, results).ConfigureAwait(false);

            progress?.Invoke("Waiting for the exclusive benchmark host lease.");
            await using var hostLease = await ExecutionLease.AcquireExclusiveAsync(
                BenchmarkHostLeaseRoot, BenchmarkLeaseWait, runToken).ConfigureAwait(false);
            progress?.Invoke("Benchmark host lease acquired; waiting for the exclusive repository lease.");
            await using var repositoryLease = await ExecutionLease.AcquireExclusiveAsync(
                artifactPaths.RepositoryRoot, BenchmarkLeaseWait, runToken).ConfigureAwait(false);
            progress?.Invoke("Exclusive benchmark host and repository leases acquired.");

            foreach (var (invocation, index) in plan.Invocations.Select((value, index) => (value, index)))
            {
                runToken.ThrowIfCancellationRequested();
                var projectDirectory = Path.Combine(runDirectory, invocation.ProjectKey, index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(projectDirectory);
                var buildStdout = Path.Combine(projectDirectory, "build.stdout.log");
                var buildStderr = Path.Combine(projectDirectory, "build.stderr.log");
                var stdout = Path.Combine(projectDirectory, "process.stdout.log");
                var stderr = Path.Combine(projectDirectory, "process.stderr.log");
                var invocationName = invocation.BenchmarkClass is null
                    ? invocation.ProjectKey
                    : $"{invocation.ProjectKey}/{invocation.BenchmarkClass}" +
                      (invocation.BenchmarkFilter is null ? string.Empty : $" {invocation.BenchmarkFilter}");
                progress?.Invoke($"Building benchmark target {invocationName} ({index + 1}/{plan.Invocations.Count}; build deadline {plan.TimeoutBudget.BuildTimeoutMinutes} min; aggregate remaining {RemainingMinutes(plan.TimeoutBudget.AggregateTimeoutMinutes, runStartedTimestamp)} min).");
                var build = await processRunner.RunAsync(new ProcessRequest
                {
                    ExecutablePath = dotnetExecutable,
                    Arguments = BuildDotNetBuildArguments(invocation, plan),
                    WorkingDirectory = artifactPaths.RepositoryRoot,
                    StandardOutputLogPath = buildStdout,
                    StandardErrorLogPath = buildStderr,
                    EnvironmentVariables = BuildEnvironment(manifest, projectDirectory, request.Options, plan, invocation),
                    Timeout = TimeSpan.FromMinutes(plan.TimeoutBudget.BuildTimeoutMinutes),
                    ProgressInterval = TimeSpan.FromSeconds(60),
                    Progress = value => progress?.Invoke(
                        $"Benchmark build target {invocationName} is still running " +
                        $"({value.Elapsed:hh\\:mm\\:ss} elapsed; {RemainingMinutes(plan.TimeoutBudget.BuildTimeoutMinutes, value.Elapsed)} min build budget and {RemainingMinutes(plan.TimeoutBudget.AggregateTimeoutMinutes, runStartedTimestamp)} min aggregate budget remain; {index}/{plan.Invocations.Count} invocations completed; " +
                        $"{value.StandardOutputLineCount} stdout and {value.StandardErrorLineCount} stderr lines; " +
                        $"last stdout: {value.LastStandardOutputLine ?? "<none>"}; " +
                        $"last stderr: {value.LastStandardErrorLine ?? "<none>"}).")
                }, runToken).ConfigureAwait(false);
                if (build.RawExitCode is int buildExitCode)
                    childExitCodes.Add(buildExitCode);
                artifacts.Add(Relative(runDirectory, buildStdout));
                artifacts.Add(Relative(runDirectory, buildStderr));
                foreach (var line in build.StandardOutputTail)
                    progress?.Invoke(line);
                foreach (var line in build.StandardErrorTail)
                    errorOutput?.Invoke(line);

                if (build.Outcome != OutcomeKind.Success)
                {
                    if (build.Outcome == OutcomeKind.Cancelled && runToken.IsCancellationRequested)
                    {
                        timeoutSource = runTimeout.IsCancellationRequested ? "aggregate" : "external-cancellation";
                        results.Add(new BenchmarkProjectResult(invocationName, "Interrupted", build.RawExitCode,
                            Relative(runDirectory, projectDirectory), build.Failure, BuildDuration: build.Duration,
                            TimeoutSource: timeoutSource));
                        runToken.ThrowIfCancellationRequested();
                    }
                    var buildTimeoutSource = build.Outcome == OutcomeKind.Timeout ? "build" : null;
                    if (buildTimeoutSource is not null)
                        timeoutSource = buildTimeoutSource;
                    results.Add(new BenchmarkProjectResult(invocationName,
                        build.Outcome == OutcomeKind.Timeout ? "Interrupted" : "Failed", build.RawExitCode,
                        Relative(runDirectory, projectDirectory), build.Failure, BuildDuration: build.Duration,
                        TimeoutSource: buildTimeoutSource));
                    failure = build.Outcome;
                    failureDetails = build.Failure ?? $"Building benchmark target '{invocationName}' exited with code {build.RawExitCode}.";
                    break;
                }

                progress?.Invoke($"Running benchmark target {invocationName} ({index + 1}/{plan.Invocations.Count}; project deadline {plan.TimeoutBudget.ProjectTimeoutMinutes} min; aggregate remaining {RemainingMinutes(plan.TimeoutBudget.AggregateTimeoutMinutes, runStartedTimestamp)} min).");
                var process = await processRunner.RunAsync(new ProcessRequest
                {
                    ExecutablePath = dotnetExecutable,
                    Arguments = BuildDotNetArguments(invocation, plan),
                    WorkingDirectory = artifactPaths.RepositoryRoot,
                    StandardOutputLogPath = stdout,
                    StandardErrorLogPath = stderr,
                    EnvironmentVariables = BuildEnvironment(manifest, projectDirectory, request.Options, plan, invocation),
                    Timeout = TimeSpan.FromMinutes(plan.TimeoutBudget.ProjectTimeoutMinutes),
                    ProgressInterval = TimeSpan.FromSeconds(60),
                    Progress = value => progress?.Invoke(
                        $"Benchmark target {invocationName} is still running " +
                        $"({value.Elapsed:hh\\:mm\\:ss} elapsed; {RemainingMinutes(plan.TimeoutBudget.ProjectTimeoutMinutes, value.Elapsed)} min project budget and {RemainingMinutes(plan.TimeoutBudget.AggregateTimeoutMinutes, runStartedTimestamp)} min aggregate budget remain; {index}/{plan.Invocations.Count} invocations completed; " +
                        $"{value.StandardOutputLineCount} stdout and {value.StandardErrorLineCount} stderr lines; " +
                        $"last stdout: {value.LastStandardOutputLine ?? "<none>"}; " +
                        $"last stderr: {value.LastStandardErrorLine ?? "<none>"}).")
                }, runToken).ConfigureAwait(false);
                if (process.RawExitCode is int exitCode)
                    childExitCodes.Add(exitCode);
                artifacts.Add(Relative(runDirectory, stdout));
                artifacts.Add(Relative(runDirectory, stderr));
                foreach (var line in process.StandardOutputTail)
                    progress?.Invoke(line);
                foreach (var line in process.StandardErrorTail)
                    errorOutput?.Invoke(line);

                if (process.Outcome != OutcomeKind.Success)
                {
                    if (process.Outcome == OutcomeKind.Cancelled && runToken.IsCancellationRequested)
                    {
                        timeoutSource = runTimeout.IsCancellationRequested ? "aggregate" : "external-cancellation";
                        results.Add(new BenchmarkProjectResult(invocationName, "Interrupted", process.RawExitCode,
                            Relative(runDirectory, projectDirectory), process.Failure, BuildDuration: build.Duration,
                            RunDuration: process.Duration, TimeoutSource: timeoutSource));
                        runToken.ThrowIfCancellationRequested();
                    }
                    var projectTimeoutSource = process.Outcome == OutcomeKind.Timeout ? "project" : null;
                    if (projectTimeoutSource is not null)
                        timeoutSource = projectTimeoutSource;
                    results.Add(new BenchmarkProjectResult(invocationName,
                        process.Outcome == OutcomeKind.Timeout ? "Interrupted" : "Failed", process.RawExitCode,
                        Relative(runDirectory, projectDirectory), process.Failure, BuildDuration: build.Duration,
                        RunDuration: process.Duration, TimeoutSource: projectTimeoutSource));
                    failure = process.Outcome;
                    failureDetails = process.Failure ?? $"Benchmark target '{invocationName}' exited with code {process.RawExitCode}.";
                    break;
                }

                var resultError = ValidateResults(invocation.ProjectKey, projectDirectory);
                if (resultError is not null)
                {
                    results.Add(new BenchmarkProjectResult(invocationName, "Incomplete", process.RawExitCode,
                        Relative(runDirectory, projectDirectory), resultError, BuildDuration: build.Duration,
                        RunDuration: process.Duration));
                    failure = OutcomeKind.InfrastructureFailure;
                    failureDetails = resultError;
                    break;
                }
                results.Add(new BenchmarkProjectResult(invocationName, "Passed", process.RawExitCode,
                    Relative(runDirectory, projectDirectory), null, BuildDuration: build.Duration,
                    RunDuration: process.Duration));
            }

            var identityEvidence = await WriteReportsAsync(runDirectory, manifest, hostIdentity, plan, configuration.BenchmarkCases, results,
                failure ?? OutcomeKind.Success, referenceIdentity, artifacts, runToken, timeoutSource).ConfigureAwait(false);
            AddCoreReportArtifacts(runDirectory, identityEvidence, artifacts);
            artifacts.Add("run-report.json");
            artifacts.Add("report.md");
            if (failure is null)
                return await FinishAsync(manifest, runDirectory, OutcomeKind.Success, null, childExitCodes, artifacts, results, identityEvidence).ConfigureAwait(false);
            return await FinishAsync(manifest, runDirectory, failure.Value, failureDetails, childExitCodes, artifacts, results, identityEvidence).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (runTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            timeoutSource = "aggregate";
            var identityEvidence = await WriteReportsAsync(runDirectory, manifest, hostIdentity, plan, configuration.BenchmarkCases, results,
                OutcomeKind.Timeout, null, artifacts, CancellationToken.None, timeoutSource).ConfigureAwait(false);
            AddCoreReportArtifacts(runDirectory, identityEvidence, artifacts);
            artifacts.AddRange(["run-report.json", "report.md"]);
            return await FinishAsync(manifest, runDirectory, OutcomeKind.Timeout,
                $"Benchmark run exceeded its {plan.TimeoutBudget.AggregateTimeoutMinutes}-minute aggregate budget. {exception.Message}", childExitCodes, artifacts, results, identityEvidence).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            timeoutSource = "external-cancellation";
            var identityEvidence = await WriteReportsAsync(runDirectory, manifest, hostIdentity, plan, configuration.BenchmarkCases, results,
                OutcomeKind.Cancelled, null, artifacts, CancellationToken.None, timeoutSource).ConfigureAwait(false);
            AddCoreReportArtifacts(runDirectory, identityEvidence, artifacts);
            artifacts.AddRange(["run-report.json", "report.md"]);
            return await FinishAsync(manifest, runDirectory, OutcomeKind.Cancelled, exception.Message, childExitCodes, artifacts, results, identityEvidence).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            timeoutSource = "lease";
            var identityEvidence = await WriteReportsAsync(runDirectory, manifest, hostIdentity, plan, configuration.BenchmarkCases, results,
                OutcomeKind.Timeout, null, artifacts, CancellationToken.None, timeoutSource).ConfigureAwait(false);
            AddCoreReportArtifacts(runDirectory, identityEvidence, artifacts);
            artifacts.AddRange(["run-report.json", "report.md"]);
            return await FinishAsync(manifest, runDirectory, OutcomeKind.Timeout,
                $"Benchmark execution could not acquire its exclusive lease within {BenchmarkLeaseWait.TotalMinutes:0} minutes. {exception.Message}", childExitCodes, artifacts, results, identityEvidence).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            var identityEvidence = await WriteReportsAsync(runDirectory, manifest, hostIdentity, plan, configuration.BenchmarkCases, results,
                OutcomeKind.InfrastructureFailure, null, artifacts, CancellationToken.None, timeoutSource).ConfigureAwait(false);
            AddCoreReportArtifacts(runDirectory, identityEvidence, artifacts);
            artifacts.AddRange(["run-report.json", "report.md"]);
            return await FinishAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure, exception.Message, childExitCodes, artifacts, results, identityEvidence).ConfigureAwait(false);
        }
    }

    private async Task<Dictionary<string, object?>?> VerifyWikipediaReferenceAsync(
        CheckoutIdentity checkout,
        BenchmarkExecutionPlan plan,
        string runDirectory,
        List<string> artifacts,
        List<int> childExitCodes,
        CancellationToken cancellationToken)
    {
        var toolProject = RepositoryProjectCatalog.GetExistingProjectPath(artifactPaths.RepositoryRoot, RepositoryProjectRole.DataForgeTool);
        var stdout = Path.Combine(runDirectory, "wikipedia-reference.stdout.log");
        var stderr = Path.Combine(runDirectory, "wikipedia-reference.stderr.log");
        var result = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = ["run", "--project", toolProject, "--framework", "net10.0", "--configuration", "Release", "--", "reference", "verify", "-ReferencePath", plan.ReferencePath!],
            WorkingDirectory = checkout.RepositoryRoot,
            StandardOutputLogPath = stdout,
            StandardErrorLogPath = stderr,
            Timeout = TimeSpan.FromMinutes(10),
            ProgressInterval = TimeSpan.FromSeconds(30),
            Progress = value => progress?.Invoke($"Verifying the frozen Wikipedia reference ({value.Elapsed:hh\\:mm\\:ss}).")
        }, cancellationToken).ConfigureAwait(false);
        if (result.RawExitCode is int exitCode)
            childExitCodes.Add(exitCode);
        artifacts.Add(Relative(runDirectory, stdout));
        artifacts.Add(Relative(runDirectory, stderr));
        if (result.Outcome != OutcomeKind.Success)
            return null;

        var referenceIdentity = DataForgeQualificationEvidence.ReadReferenceIdentity(plan.ReferencePath!, out var failure);
        if (referenceIdentity is null)
            throw new InvalidDataException(failure ?? "The verified artefact manifest does not identify the required Wikipedia reference.");
        var identity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sourceKind"] = referenceIdentity.SourceKind,
            ["datasetId"] = referenceIdentity.DatasetId,
            ["datasetVersion"] = referenceIdentity.DatasetVersion,
            ["recordCount"] = referenceIdentity.RecordCount,
            ["logicalByteCount"] = referenceIdentity.LogicalByteCount,
            ["contentSha256"] = referenceIdentity.ContentSha256,
            ["artefactSha256"] = referenceIdentity.ArtefactSha256,
            ["dumpDate"] = referenceIdentity.DumpDate,
            ["primarySha1"] = referenceIdentity.PrimarySha1,
            ["indexSha1"] = referenceIdentity.IndexSha1
        };
        return identity;
    }

    private IReadOnlyDictionary<string, string?> BuildEnvironment(
        RunManifest manifest,
        string artifactDirectory,
        BenchmarkCommandOptions options,
        BenchmarkExecutionPlan plan,
        BenchmarkInvocation invocation)
    {
        var sourceManifest = options.SourceManifest is null ? string.Empty : ResolvePath(options.SourceManifest, artifactPaths.RepositoryRoot);
        string documentCount = invocation.ParameterValues is not null &&
            invocation.ParameterValues.TryGetValue("DocumentCount", out var selectedDocumentCounts)
                ? selectedDocumentCounts.Length == 1 ? selectedDocumentCounts[0] : string.Empty
                : invocation.DocumentCount is > 0
                    ? invocation.DocumentCount.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : plan.DocumentCount > 0
                        ? plan.DocumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        : string.Empty;
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["LEANCORPUS_RUN_ID"] = manifest.RunId.ToString("D"),
            ["LEANCORPUS_ARTIFACT_DIR"] = artifactDirectory,
            ["BENCH_DATASET_MODE"] = plan.Dataset,
            ["BENCH_REFERENCE_PATH"] = plan.ReferencePath ?? string.Empty,
            ["BENCH_DOC_COUNT"] = documentCount,
            ["LEANCORPUS_BENCHMARK_PARAMETER_FILTER"] = invocation.ParameterValues is { Count: > 0 } parameterValues
                ? JsonSerializer.Serialize(parameterValues)
                : string.Empty,
            ["BENCH_SOURCE_COMMIT"] = manifest.SourceCommit ?? options.SourceCommit ?? string.Empty,
            ["BENCH_SOURCE_REF"] = options.SourceRef ?? manifest.SourceBranch ?? string.Empty,
            ["BENCH_SOURCE_MANIFEST"] = sourceManifest
        };
    }

    private static IReadOnlyList<string> BuildDotNetBuildArguments(BenchmarkInvocation invocation, BenchmarkExecutionPlan plan)
        => ["build", invocation.ProjectPath, "-c", "Release", "-f", plan.Framework, "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false", "--tl:off"];

    private static IReadOnlyList<string> BuildDotNetArguments(BenchmarkInvocation invocation, BenchmarkExecutionPlan plan)
        => ["run", "--no-build", "--disable-build-servers", "--configuration", "Release", "--framework", plan.Framework, "--project", invocation.ProjectPath, "--", .. invocation.Arguments];

    internal static string? ValidateResults(string project, string projectDirectory)
    {
        var resultsDirectory = Path.Combine(projectDirectory, "_runner", "results");
        if (!Directory.Exists(resultsDirectory))
            return $"BenchmarkDotNet did not create a results directory for project '{project}'.";
        var resultFiles = Directory.EnumerateFiles(resultsDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(static path => Path.GetExtension(path) is ".md" or ".csv" or ".json")
            .Where(static path => new FileInfo(path).Length > 0)
            .ToArray();
        if (resultFiles.Length == 0)
            return $"BenchmarkDotNet produced no non-empty result files for project '{project}'.";
        foreach (var resultJson in resultFiles.Where(static path => Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                using var parsedResult = JsonDocument.Parse(File.ReadAllBytes(resultJson));
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                return $"BenchmarkDotNet result file '{Path.GetFileName(resultJson)}' is truncated or invalid: {exception.Message}";
            }
        }
        if (project == "core")
        {
            var reportPath = Path.Combine(projectDirectory, "report.json");
            if (!File.Exists(reportPath))
                return "The core benchmark runner did not produce its required report.json.";
            try
            {
                using var report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
                var root = report.RootElement;
                if (root.GetProperty("schemaVersion").GetInt32() != 5 || root.GetProperty("totalBenchmarkCount").GetInt32() < 1)
                    return "The core benchmark report is incomplete or uses an unsupported schema.";
                foreach (var suite in root.GetProperty("suites").EnumerateArray())
                {
                    if (suite.GetProperty("failedBenchmarkCount").GetInt32() != 0 ||
                        suite.GetProperty("intermediateBenchmarkCount").GetInt32() != 0 ||
                        suite.GetProperty("missingBenchmarkCount").GetInt32() != 0)
                        return "The core benchmark report contains failed, intermediate or missing benchmark results.";
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                return $"The core benchmark report is invalid: {exception.Message}";
            }
        }
        return null;
    }

    private static IReadOnlyList<string> ReadChangedPaths(string repositoryRoot)
    {
        using var repository = new LibGit2Sharp.Repository(repositoryRoot);
        var status = repository.RetrieveStatus(new StatusOptions
        {
            IncludeUntracked = true,
            IncludeIgnored = false,
            RecurseUntrackedDirs = true,
            DetectRenamesInIndex = true,
            DetectRenamesInWorkDir = true
        });
        return status
            .SelectMany(entry => new[]
            {
                entry.FilePath,
                entry.HeadToIndexRenameDetails?.OldFilePath,
                entry.IndexToWorkDirRenameDetails?.OldFilePath
            })
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path!.Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<BenchmarkRunIdentityEvidence> WriteReportsAsync(
        string runDirectory,
        RunManifest manifest,
        BenchmarkHostIdentity hostIdentity,
        BenchmarkExecutionPlan plan,
        IReadOnlyList<BenchmarkCaseConfiguration> caseFamilies,
        IReadOnlyList<BenchmarkProjectResult> results,
        OutcomeKind terminalOutcome,
        IReadOnlyDictionary<string, object?>? datasetIdentity,
        ICollection<string> artifacts,
        CancellationToken cancellationToken,
        string? timeoutSource)
    {
        CollectRawBenchmarkResults(runDirectory, plan, artifacts);
        var coverage = BenchmarkCaseCoverage.Create(runDirectory, plan, results, terminalOutcome, timeoutSource);
        var coveragePath = Path.Combine(runDirectory, "coverage.json");
        await WriteAtomicAsync(coveragePath, JsonSerializer.Serialize(coverage, JsonOptions), cancellationToken).ConfigureAwait(false);
        if (!artifacts.Contains("coverage.json", StringComparer.Ordinal))
            artifacts.Add("coverage.json");
        var coreReports = ReadCoreReportIdentities(runDirectory, plan);
        var expectedCoreReportCount = plan.Invocations.Count(static invocation => invocation.ProjectKey == "core");
        var assessment = BenchmarkRunIdentityValidator.Assess(new BenchmarkRunIdentityInput(
            manifest.SourceCommit,
            manifest.SourceDirty,
            hostIdentity,
            coreReports,
            expectedCoreReportCount));
        var comparisonAssessment = BenchmarkComparisonAssessor.Assess(new BenchmarkComparisonEvidence(
            manifest.SourceCommit ?? string.Empty,
            manifest.SourceDirty,
            hostIdentity,
            caseFamilies,
            coreReports.Select(static report => BenchmarkComparisonSourceReport.Read(report.ReportPath)).ToArray()));
        var execution = BuildExecutionSummary(runDirectory, plan, results, terminalOutcome, timeoutSource, coverage);
        var report = new
        {
            schemaVersion = 1,
            runId = manifest.RunId,
            status = results.All(static result => result.Status == "Passed") && results.Count == plan.Invocations.Count ? "Passed" : "Failed",
            framework = plan.Framework,
            strategy = plan.Strategy,
            sourceIdentity = new
            {
                commit = manifest.SourceCommit,
                branch = manifest.SourceBranch,
                dirty = manifest.SourceDirty
            },
            hostIdentity,
            dataset = new
            {
                mode = plan.Dataset,
                referenceIdentity = datasetIdentity,
                coreReports = coreReports.Select(coreReport => new
                {
                    project = coreReport.Project,
                    path = Relative(runDirectory, coreReport.ReportPath),
                    schemaVersion = coreReport.SchemaVersion,
                    generatedAtUtc = coreReport.GeneratedAtUtc,
                    dataFingerprintSha256 = coreReport.DatasetFingerprintSha256,
                    identities = coreReport.Datasets
                })
            },
            qualification = new
            {
                identityStatus = assessment.IdentityStatus,
                state = assessment.QualificationState,
                eligibleForRatio = assessment.EligibleForRatio,
                comparable = assessment.Comparable,
                reasonCodes = assessment.ReasonCodes
            },
            selectionScope = coverage.SelectionScope,
            coverage = new
            {
                path = "coverage.json",
                completeInventoryKnown = coverage.CompleteInventoryKnown,
                selectionScope = coverage.SelectionScope,
                projects = coverage.Projects,
                excludedWorkloadCount = coverage.ExcludedWorkloads.Count
            },
            comparisonAssessment,
            execution,
            comparable = assessment.Comparable,
            affectedSelection = plan.Suite == "affected" ? new { paths = plan.AffectedPaths, groups = plan.AffectedGroups } : null,
            coreReportIdentities = coreReports.Select(coreReport => new
            {
                project = coreReport.Project,
                path = Relative(runDirectory, coreReport.ReportPath),
                schemaVersion = coreReport.SchemaVersion,
                generatedAtUtc = coreReport.GeneratedAtUtc,
                sourceCommit = coreReport.SourceCommit,
                sourceDirty = coreReport.SourceDirty,
                hostMachineName = coreReport.HostMachineName,
                osDescription = coreReport.OsDescription,
                runtimeIdentifier = coreReport.RuntimeIdentifier,
                processArchitecture = coreReport.ProcessArchitecture,
                runtimeFramework = coreReport.RuntimeFramework,
                benchmarkDotNetVersion = coreReport.BenchmarkDotNetVersion,
                dataFingerprintSha256 = coreReport.DatasetFingerprintSha256,
                datasetIdentityComplete = coreReport.HasDatasetIdentity,
                readFailure = coreReport.ReadFailure
            }),
            projects = results
        };
        await WriteAtomicAsync(Path.Combine(runDirectory, "run-report.json"), JsonSerializer.Serialize(report, JsonOptions), cancellationToken).ConfigureAwait(false);
        var markdown = new List<string>
        {
            string.Concat("# Benchmark run ", manifest.RunId.ToString("D")),
            string.Empty,
            string.Concat("Status: ", report.status),
            string.Concat("Identity: ", assessment.IdentityStatus),
            string.Concat("Qualification: ", assessment.QualificationState),
            string.Concat("Semantic comparisons: ", comparisonAssessment.QualificationState,
                " (", comparisonAssessment.Comparisons.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), " pairs; ratio eligible: ",
                comparisonAssessment.EligibleForRatio.ToString(), ")"),
            string.Concat("Comparable: ", assessment.Comparable.ToString()),
            string.Concat("Execution: ", execution.Classification, "; cases completed/interrupted/failed/never-started/unidentified: ",
                execution.TerminalCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "/",
                execution.IntermediateCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "/",
                execution.FailedCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "/",
                execution.MissingCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "/",
                execution.UnenumeratedCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            string.Concat("Planned case count known: ", execution.PlannedCaseCountKnown.ToString(),
                "; invocations with unknown case inventory: ", execution.UnenumeratedInvocationCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            string.Concat("Selection scope: ", coverage.SelectionScope),
            "Case-by-case coverage: coverage.json",
            string.Empty,
            "## Project case coverage",
            string.Empty,
            "| Project | State | Inventory | Planned | Identified | Completed | Interrupted | Failed | Never started | Unenumerated | Build | Run |",
            "| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |"
        };
        markdown.AddRange(coverage.Projects.Select(project => string.Concat(
            "| ", FormatBenchmarkProjectName(project.ProjectKey),
            " | ", project.State,
            " | ", project.CompleteInventoryKnown ? "known" : "incomplete",
            " | ", FormatNullableCount(project.PlannedCaseCount),
            " | ", project.IdentifiedCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            " | ", project.CompletedCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            " | ", project.InterruptedCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            " | ", project.FailedCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            " | ", project.NeverStartedCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            " | ", project.UnenumeratedCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            " | ", FormatDuration(project.BuildDuration),
            " | ", FormatDuration(project.RunDuration), " |")));
        markdown.Add(string.Empty);
        markdown.Add("## Excluded workload suites");
        markdown.Add(string.Empty);
        if (coverage.ExcludedWorkloads.Count == 0)
        {
            markdown.Add("No workload suites are excluded by this selector.");
        }
        else
        {
            markdown.Add("| Suite | Project | State | Reason | Benchmark classes | Rerun selector |");
            markdown.Add("| --- | --- | --- | --- | --- | --- |");
            markdown.AddRange(coverage.ExcludedWorkloads.Select(workload => string.Concat(
                "| ", workload.SuiteId,
                " | ", FormatBenchmarkProjectName(workload.ProjectKey),
                " | ", workload.State,
                " | ", workload.Reason,
                " | ", workload.BenchmarkClasses.Count == 0 ? "unknown" : string.Join(", ", workload.BenchmarkClasses),
                " | `--suite ", workload.RerunSuite, "` |")));
        }
        markdown.Add(string.Empty);
        markdown.Add("| Project invocation | Status | Path | Build | Run |");
        markdown.Add("| --- | --- | --- | ---: | ---: |");
        markdown.AddRange(results.Select(result => string.Concat(
            "| ", result.Project,
            " | ", result.Status,
            " | ", result.Path,
            " | ", FormatDuration(result.BuildDuration),
            " | ", FormatDuration(result.RunDuration), " |")));
        await WriteAtomicAsync(Path.Combine(runDirectory, "report.md"), string.Join(Environment.NewLine, markdown) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        return new BenchmarkRunIdentityEvidence(hostIdentity, coreReports, assessment, comparisonAssessment, execution);
    }

    private static IReadOnlyList<BenchmarkCoreReportIdentity> ReadCoreReportIdentities(
        string runDirectory,
        BenchmarkExecutionPlan plan)
        => plan.Invocations
            .Select((invocation, index) => (invocation, index))
            .Where(static pair => pair.invocation.ProjectKey == "core")
            .Select(pair => BenchmarkCoreReportIdentity.Read(
                pair.invocation.ProjectKey,
                Path.Combine(runDirectory, pair.invocation.ProjectKey,
                    pair.index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                    "report.json")))
            .ToArray();

    private static void AddCoreReportArtifacts(
        string runDirectory,
        BenchmarkRunIdentityEvidence evidence,
        ICollection<string> artifacts)
    {
        foreach (var report in evidence.CoreReports)
        {
            if (File.Exists(report.ReportPath))
                artifacts.Add(Relative(runDirectory, report.ReportPath));
        }
    }

    private void CollectRawBenchmarkResults(
        string runDirectory,
        BenchmarkExecutionPlan plan,
        ICollection<string> artifacts)
    {
        for (int index = 0; index < plan.Invocations.Count; index++)
        {
            string projectDirectory = Path.Combine(runDirectory, plan.Invocations[index].ProjectKey,
                index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            string resultsDirectory = Path.Combine(projectDirectory, "_runner", "results");
            if (!Directory.Exists(resultsDirectory))
                continue;
            foreach (string path in Directory.EnumerateFiles(resultsDirectory, "*", SearchOption.AllDirectories)
                         .OrderBy(static path => path, StringComparer.Ordinal))
            {
                string relative = Relative(runDirectory, path);
                if (!artifacts.Contains(relative, StringComparer.Ordinal))
                    artifacts.Add(relative);
            }
        }
    }

    private static BenchmarkExecutionSummary BuildExecutionSummary(
        string runDirectory,
        BenchmarkExecutionPlan plan,
        IReadOnlyList<BenchmarkProjectResult> results,
        OutcomeKind terminalOutcome,
        string? timeoutSource,
        BenchmarkCaseCoverageReport coverage)
    {
        int discoveredCaseCount = 0;
        int terminalCaseCount = 0;
        int failedCaseCount = 0;
        int intermediateCaseCount = 0;
        int missingCaseCount = 0;
        int rawResultFileCount = 0;
        for (int index = 0; index < plan.Invocations.Count; index++)
        {
            string projectDirectory = Path.Combine(runDirectory, plan.Invocations[index].ProjectKey,
                index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            string reportPath = Path.Combine(projectDirectory, "report.json");
            if (File.Exists(reportPath))
            {
                try
                {
                    using var report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
                    JsonElement root = report.RootElement;
                    if (root.TryGetProperty("suites", out JsonElement suites) && suites.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement suite in suites.EnumerateArray())
                        {
                            int registered = ReadInt32(suite, "registeredBenchmarkCount") ?? 0;
                            int terminal = ReadInt32(suite, "measuredBenchmarkCount") ?? 0;
                            int failed = ReadInt32(suite, "failedBenchmarkCount") ?? 0;
                            int intermediate = ReadInt32(suite, "intermediateBenchmarkCount") ?? 0;
                            int missing = ReadInt32(suite, "missingBenchmarkCount") ?? 0;
                            discoveredCaseCount += registered;
                            terminalCaseCount += terminal;
                            failedCaseCount += failed;
                            intermediateCaseCount += intermediate;
                            missingCaseCount += missing;
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
                {
                }
            }

            string resultsDirectory = Path.Combine(projectDirectory, "_runner", "results");
            if (Directory.Exists(resultsDirectory))
                rawResultFileCount += Directory.EnumerateFiles(resultsDirectory, "*", SearchOption.AllDirectories).Count();
        }

        int terminalInvocations = results.Count(static result => result.Status == "Passed");
        int partialInvocations = Math.Max(0, plan.Invocations.Count - terminalInvocations);
        bool noOutput = results.Any(static result => result.Failure is { } failure &&
            (failure.Contains("did not create a results directory", StringComparison.OrdinalIgnoreCase) ||
             failure.Contains("no non-empty result files", StringComparison.OrdinalIgnoreCase)));
        if (coverage.PlannedCaseCount > 0 || coverage.CompleteInventoryKnown)
        {
            discoveredCaseCount = coverage.PlannedCaseCount;
            terminalCaseCount = coverage.CompletedCaseCount;
            failedCaseCount = coverage.FailedCaseCount;
            intermediateCaseCount = coverage.InterruptedCaseCount;
            missingCaseCount = coverage.NeverStartedCaseCount;
        }
        int plannedCaseCount = coverage.PlannedCaseCount > 0 || coverage.CompleteInventoryKnown
            ? coverage.PlannedCaseCount
            : plan.SelectedCaseCount > 0
                ? plan.SelectedCaseCount
                : discoveredCaseCount > 0
                    ? discoveredCaseCount
                    : terminalCaseCount + failedCaseCount + intermediateCaseCount + missingCaseCount;
        bool failedPair = failedCaseCount > 0 || results.Any(static result => result.Failure is { } failure &&
            failure.Contains("contains failed, intermediate or missing benchmark results", StringComparison.OrdinalIgnoreCase));
        string classification = terminalOutcome switch
        {
            OutcomeKind.Timeout => "TimedOut",
            OutcomeKind.Cancelled => "Cancelled",
            _ when noOutput => "NoOutput",
            _ when failedPair => "FailedPair",
            _ when terminalOutcome != OutcomeKind.Success => "Failed",
            _ when terminalInvocations == plan.Invocations.Count && results.Count == plan.Invocations.Count => "Completed",
            _ when results.Any(static result => result.Status is "Incomplete" or "Interrupted") || results.Count < plan.Invocations.Count => "Partial",
            _ => "Failed"
        };

        return new BenchmarkExecutionSummary(classification, plan.Invocations.Count, terminalInvocations,
            partialInvocations, plannedCaseCount, terminalCaseCount, failedCaseCount, intermediateCaseCount,
            missingCaseCount, rawResultFileCount, coverage.UnenumeratedCaseCount, coverage.CompleteInventoryKnown,
            coverage.UnenumeratedInvocationCount, plan.TimeoutBudget, timeoutSource);
    }

    private static string FormatTimeoutBudget(BenchmarkTimeoutBudget budget)
        => $"Resolved timeouts: aggregate {budget.AggregateTimeoutMinutes} minutes ({budget.AggregateTimeoutSource}); " +
           $"build {budget.BuildTimeoutMinutes} minutes per build; project {budget.ProjectTimeoutMinutes} minutes per benchmark child.";

    private static string FormatBenchmarkProjectName(string projectKey)
        => projectKey switch
        {
            "compression" => "Compression",
            "core" => "Core",
            "text" => "Text",
            _ => projectKey
        };

    private static string FormatNullableCount(int? count)
        => count?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";

    private static string FormatDuration(TimeSpan? duration)
        => duration?.ToString("c", System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";

    private static int RemainingMinutes(int timeoutMinutes, long startedTimestamp)
        => RemainingMinutes(timeoutMinutes, System.Diagnostics.Stopwatch.GetElapsedTime(startedTimestamp));

    private static int RemainingMinutes(int timeoutMinutes, TimeSpan elapsed)
        => Math.Max(0, (int)Math.Ceiling(timeoutMinutes - elapsed.TotalMinutes));

    private static int? ReadInt32(JsonElement value, string propertyName)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out JsonElement property) &&
           property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out int result)
            ? result
            : null;

    private async Task<BenchmarkCommandResult> FinishAsync(
        RunManifest manifest,
        string runDirectory,
        OutcomeKind outcome,
        string? failure,
        IReadOnlyList<int> childExitCodes,
        IReadOnlyList<string> artifacts,
        IReadOnlyList<BenchmarkProjectResult> results,
        BenchmarkRunIdentityEvidence? identityEvidence = null)
    {
        var state = outcome switch
        {
            OutcomeKind.Success => RunState.Succeeded,
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        var finished = await runStore.FinishAsync(manifest, state, artifacts: artifacts, failure: failure,
            cancellationToken: CancellationToken.None, childExitCodes: childExitCodes,
            additionalInputs: identityEvidence is null ? null : BuildIdentityRunInputs(runDirectory, identityEvidence)).ConfigureAwait(false);
        var commandResult = new CommandResult(outcome, outcome == OutcomeKind.Success
            ? $"Benchmark run succeeded with {results.Count} project invocations."
            : $"Benchmark run {state.ToString().ToLowerInvariant()}: {failure}",
            Details: runDirectory);
        return new BenchmarkCommandResult(commandResult, finished.RunId, runDirectory);
    }

    private static IReadOnlyDictionary<string, string> BuildIdentityRunInputs(
        string runDirectory,
        BenchmarkRunIdentityEvidence evidence)
        => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["identityStatus"] = evidence.Assessment.IdentityStatus,
            ["qualificationState"] = evidence.Assessment.QualificationState,
            ["eligibleForRatio"] = evidence.Assessment.EligibleForRatio.ToString().ToLowerInvariant(),
            ["comparable"] = evidence.Assessment.Comparable.ToString().ToLowerInvariant(),
            ["qualificationReasonCodes"] = JsonSerializer.Serialize(evidence.Assessment.ReasonCodes),
            ["timeoutSource"] = evidence.Execution.TimeoutSource ?? "none",
            ["comparisonQualificationState"] = evidence.ComparisonAssessment.QualificationState,
            ["comparisonEligibleForRatio"] = evidence.ComparisonAssessment.EligibleForRatio.ToString().ToLowerInvariant(),
            ["comparisonReasonCodes"] = JsonSerializer.Serialize(evidence.ComparisonAssessment.ReasonCodes),
            ["comparisonPairCount"] = evidence.ComparisonAssessment.Comparisons.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["executionClassification"] = evidence.Execution.Classification,
            ["plannedInvocationCount"] = evidence.Execution.PlannedInvocationCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["terminalInvocationCount"] = evidence.Execution.TerminalInvocationCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["partialInvocationCount"] = evidence.Execution.PartialInvocationCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["plannedCaseCount"] = evidence.Execution.PlannedCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["terminalCaseCount"] = evidence.Execution.TerminalCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["failedCaseCount"] = evidence.Execution.FailedCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["intermediateCaseCount"] = evidence.Execution.IntermediateCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["missingCaseCount"] = evidence.Execution.MissingCaseCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["rawResultFileCount"] = evidence.Execution.RawResultFileCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["coreReportIdentities"] = JsonSerializer.Serialize(evidence.CoreReports.Select(report => new
            {
                project = report.Project,
                path = Relative(runDirectory, report.ReportPath),
                schemaVersion = report.SchemaVersion,
                generatedAtUtc = report.GeneratedAtUtc,
                sourceCommit = report.SourceCommit,
                sourceDirty = report.SourceDirty,
                hostMachineName = report.HostMachineName,
                osDescription = report.OsDescription,
                runtimeIdentifier = report.RuntimeIdentifier,
                processArchitecture = report.ProcessArchitecture,
                runtimeFramework = report.RuntimeFramework,
                benchmarkDotNetVersion = report.BenchmarkDotNetVersion,
                dataFingerprintSha256 = report.DatasetFingerprintSha256,
                datasetIdentityComplete = report.HasDatasetIdentity,
                readFailure = report.ReadFailure,
                datasets = report.Datasets
            }))
        };

    private async Task<bool> IsDotNetToolAvailableAsync(CheckoutIdentity checkout, CancellationToken cancellationToken)
    {
        var probeDirectory = Path.Combine(artifactPaths.ArtifactRoot, "temp", "benchmark-gcdump-probe", Guid.NewGuid().ToString("N"));
        var result = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = ["gcdump", "--version"],
            WorkingDirectory = checkout.RepositoryRoot,
            StandardOutputLogPath = Path.Combine(probeDirectory, "stdout.log"),
            StandardErrorLogPath = Path.Combine(probeDirectory, "stderr.log"),
            Timeout = TimeSpan.FromSeconds(30)
        }, cancellationToken).ConfigureAwait(false);
        return result.Outcome == OutcomeKind.Success;
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string ResolvePath(string path, string repositoryRoot)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repositoryRoot, path));

    private static string QuoteArguments(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(argument => argument.Any(char.IsWhiteSpace) ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument));
}

internal sealed record BenchmarkCommandRequest(CheckoutIdentity Checkout, BenchmarkCommandOptions Options);
