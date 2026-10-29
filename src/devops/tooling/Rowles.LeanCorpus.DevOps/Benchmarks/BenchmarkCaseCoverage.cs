using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal sealed record BenchmarkCaseCoverageRecord(
    string CaseId,
    string ProjectKey,
    string Invocation,
    string BenchmarkClass,
    string DisplayInfo,
    string State,
    int? DiscoveredAtLine,
    int? StartedAtLine,
    int? CompletedAtLine,
    IReadOnlyList<string> ResultLocations);

internal sealed record BenchmarkInvocationCoverage(
    string ProjectKey,
    string Invocation,
    string State,
    bool CaseInventoryKnown,
    int? PlannedCaseCount,
    int UnenumeratedCaseCount,
    TimeSpan? BuildDuration,
    TimeSpan? RunDuration,
    IReadOnlyList<string> ResultLocations,
    IReadOnlyList<string> Diagnostics);

internal sealed record BenchmarkProjectCoverageSummary(
    string ProjectKey,
    string State,
    int SelectedInvocationCount,
    int StartedInvocationCount,
    bool CompleteInventoryKnown,
    int? PlannedCaseCount,
    int IdentifiedCaseCount,
    int UnenumeratedCaseCount,
    int CompletedCaseCount,
    int InterruptedCaseCount,
    int FailedCaseCount,
    int NeverStartedCaseCount,
    TimeSpan? BuildDuration,
    TimeSpan? RunDuration);

internal sealed record BenchmarkCaseCoverageReport(
    int SchemaVersion,
    string Outcome,
    string SelectionScope,
    string? TimeoutSource,
    BenchmarkTimeoutBudget TimeoutBudget,
    bool CompleteInventoryKnown,
    int PlannedCaseCount,
    int UnenumeratedCaseCount,
    int UnenumeratedInvocationCount,
    int StartedInvocationCount,
    int StartedCaseCount,
    int CompletedCaseCount,
    int InterruptedCaseCount,
    int FailedCaseCount,
    int NeverStartedCaseCount,
    IReadOnlyList<BenchmarkProjectCoverageSummary> Projects,
    IReadOnlyList<BenchmarkExcludedWorkload> ExcludedWorkloads,
    IReadOnlyList<BenchmarkInvocationCoverage> Invocations,
    IReadOnlyList<BenchmarkCaseCoverageRecord> Cases,
    IReadOnlyList<IReadOnlyList<string>> TargetedRerunArguments,
    bool ExistingResultsMayBeStitched);

internal static class BenchmarkCaseCoverage
{
    public static BenchmarkCaseCoverageReport Create(
        string runDirectory,
        BenchmarkExecutionPlan plan,
        IReadOnlyList<BenchmarkProjectResult> results,
        OutcomeKind outcome,
        string? timeoutSource)
    {
        var invocationCoverage = new List<BenchmarkInvocationCoverage>(plan.Invocations.Count);
        var cases = new List<BenchmarkCaseCoverageRecord>();
        var reruns = new List<IReadOnlyList<string>>();

        for (var index = 0; index < plan.Invocations.Count; index++)
        {
            var invocation = plan.Invocations[index];
            var invocationName = invocation.BenchmarkClass is null
                ? invocation.ProjectKey
                : $"{invocation.ProjectKey}/{invocation.BenchmarkClass}" +
                  (invocation.BenchmarkFilter is null ? string.Empty : $" {invocation.BenchmarkFilter}");
            var projectDirectory = Path.Combine(runDirectory, invocation.ProjectKey,
                index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            var stdoutPath = Path.Combine(projectDirectory, "process.stdout.log");
            var resultsDirectory = Path.Combine(projectDirectory, "_runner", "results");
            var parsed = ParseInvocation(runDirectory, invocation.ProjectKey, invocationName, stdoutPath,
                resultsDirectory, outcome, timeoutSource, results.FirstOrDefault(result => result.Project == invocationName));
            cases.AddRange(parsed.Cases);

            var projectResult = results.FirstOrDefault(result => result.Project == invocationName);
            var buildStdoutPath = Path.Combine(projectDirectory, "build.stdout.log");
            var state = projectResult?.Status.ToLowerInvariant() ??
                ((File.Exists(stdoutPath) || File.Exists(buildStdoutPath)) ? "interrupted" : "never-started");
            var invocationLocations = new List<string>();
            AddExistingLocation(runDirectory, buildStdoutPath, invocationLocations);
            AddExistingLocation(runDirectory, Path.Combine(projectDirectory, "build.stderr.log"), invocationLocations);
            AddExistingLocation(runDirectory, stdoutPath, invocationLocations);
            AddExistingLocation(runDirectory, Path.Combine(projectDirectory, "process.stderr.log"), invocationLocations);
            if (Directory.Exists(resultsDirectory))
            {
                foreach (var resultPath in Directory.EnumerateFiles(resultsDirectory, "*", SearchOption.AllDirectories)
                             .OrderBy(static path => path, StringComparer.Ordinal))
                    AddExistingLocation(runDirectory, resultPath, invocationLocations);
            }

            invocationCoverage.Add(new BenchmarkInvocationCoverage(invocation.ProjectKey, invocationName, state,
                parsed.InventoryKnown, parsed.InventoryKnown ? parsed.PlannedCaseCount : null, parsed.UnenumeratedCaseCount,
                projectResult?.BuildDuration, projectResult?.RunDuration, invocationLocations, parsed.Diagnostics));

            var unfinishedCases = parsed.Cases.Where(static item => item.State != "completed").ToArray();
            var rerunClasses = unfinishedCases.Select(static item => item.BenchmarkClass)
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToArray();
            if (invocation.BenchmarkClass is not null && (unfinishedCases.Length > 0 || projectResult?.Status != "Passed"))
                rerunClasses = [invocation.BenchmarkClass];
            if (rerunClasses.Length == 0 && projectResult?.Status != "Passed" && unfinishedCases.Length == 0)
                reruns.Add(BuildRerunArguments(plan, invocation, null));
            else
                foreach (var benchmarkClass in rerunClasses)
                    reruns.Add(BuildRerunArguments(plan, invocation, benchmarkClass));
        }

        var completed = cases.Count(static item => item.State == "completed");
        var interrupted = cases.Count(static item => item.State == "interrupted");
        var failed = cases.Count(static item => item.State == "failed");
        var neverStarted = cases.Count(static item => item.State == "never-started");
        var projectSummaries = BuildProjectSummaries(invocationCoverage, cases);
        return new BenchmarkCaseCoverageReport(
            SchemaVersion: 1,
            Outcome: outcome.ToString(),
            SelectionScope: plan.Suite,
            TimeoutSource: timeoutSource,
            TimeoutBudget: plan.TimeoutBudget,
            CompleteInventoryKnown: invocationCoverage.All(static item => item.CaseInventoryKnown),
            PlannedCaseCount: cases.Count + invocationCoverage.Sum(static item => item.UnenumeratedCaseCount),
            UnenumeratedCaseCount: invocationCoverage.Sum(static item => item.UnenumeratedCaseCount),
            UnenumeratedInvocationCount: invocationCoverage.Count(static item => !item.CaseInventoryKnown),
            StartedInvocationCount: invocationCoverage.Count(static item => item.State != "never-started"),
            StartedCaseCount: cases.Count(static item => item.State is "completed" or "interrupted" or "failed"),
            CompletedCaseCount: completed,
            InterruptedCaseCount: interrupted,
            FailedCaseCount: failed,
            NeverStartedCaseCount: neverStarted,
            Projects: projectSummaries,
            ExcludedWorkloads: plan.ExcludedWorkloads,
            Invocations: invocationCoverage,
            Cases: cases,
            TargetedRerunArguments: reruns,
            ExistingResultsMayBeStitched: false);
    }

    private static IReadOnlyList<BenchmarkProjectCoverageSummary> BuildProjectSummaries(
        IReadOnlyList<BenchmarkInvocationCoverage> invocationCoverage,
        IReadOnlyList<BenchmarkCaseCoverageRecord> cases)
    {
        var summaries = new List<BenchmarkProjectCoverageSummary>();
        foreach (var projectGroup in invocationCoverage.GroupBy(static item => item.ProjectKey, StringComparer.Ordinal))
        {
            var projectInvocations = projectGroup.ToArray();
            var invocationNames = projectInvocations.Select(static item => item.Invocation).ToHashSet(StringComparer.Ordinal);
            var projectCases = cases.Where(item => invocationNames.Contains(item.Invocation)).ToArray();
            bool inventoryKnown = projectInvocations.All(static item => item.CaseInventoryKnown);
            int? plannedCaseCount = inventoryKnown
                ? projectInvocations.Sum(static item => item.PlannedCaseCount ?? 0)
                : null;
            var states = projectInvocations.Select(static item => item.State).ToArray();
            bool allNeverStarted = states.All(static item => item == "never-started");
            int failedCaseCount = projectCases.Count(static item => item.State == "failed");
            int interruptedCaseCount = projectCases.Count(static item => item.State == "interrupted");
            int neverStartedCaseCount = projectCases.Count(static item => item.State == "never-started");
            int unenumeratedCaseCount = projectInvocations.Sum(static item => item.UnenumeratedCaseCount);
            string state = allNeverStarted
                ? "never-started"
                : failedCaseCount > 0 || states.Any(static item => item == "failed")
                    ? "failed"
                    : interruptedCaseCount > 0 || states.Any(static item => item == "interrupted")
                        ? "interrupted"
                        : !inventoryKnown || neverStartedCaseCount > 0 || unenumeratedCaseCount > 0
                            ? "partial"
                            : states.All(static item => item == "passed")
                            ? "passed"
                            : "partial";

            summaries.Add(new BenchmarkProjectCoverageSummary(
                projectGroup.Key,
                state,
                projectInvocations.Length,
                projectInvocations.Count(static item => item.State != "never-started"),
                inventoryKnown,
                plannedCaseCount,
                projectCases.Length,
                unenumeratedCaseCount,
                projectCases.Count(static item => item.State == "completed"),
                interruptedCaseCount,
                failedCaseCount,
                neverStartedCaseCount,
                SumDurations(projectInvocations.Select(static item => item.BuildDuration)),
                SumDurations(projectInvocations.Select(static item => item.RunDuration))));
        }

        return summaries;
    }

    private static TimeSpan? SumDurations(IEnumerable<TimeSpan?> durations)
    {
        var total = TimeSpan.Zero;
        bool hasDuration = false;
        foreach (var duration in durations)
        {
            if (duration is not TimeSpan value)
                continue;
            total += value;
            hasDuration = true;
        }

        return hasDuration ? total : null;
    }

    private static ParsedInvocation ParseInvocation(
        string runDirectory,
        string projectKey,
        string invocationName,
        string stdoutPath,
        string resultsDirectory,
        OutcomeKind outcome,
        string? timeoutSource,
        BenchmarkProjectResult? projectResult)
    {
        var planned = new List<PlannedCase>();
        var startedAt = new Dictionary<string, int>(StringComparer.Ordinal);
        var completedAt = new Dictionary<string, int>(StringComparer.Ordinal);
        var failed = new HashSet<string>(StringComparer.Ordinal);
        var diagnostics = new List<string>();
        var inventoryKnown = false;
        var collectingInventory = false;
        int? expectedInventoryCount = null;
        bool inventoryCountParsingFailed = false;
        string? currentBenchmark = null;
        int lineNumber = 0;

        if (File.Exists(stdoutPath))
        {
            foreach (var line in File.ReadLines(stdoutPath))
            {
                lineNumber++;
                if (line.StartsWith("// Found ", StringComparison.Ordinal) && line.Contains(" benchmarks:", StringComparison.Ordinal))
                {
                    inventoryKnown = true;
                    collectingInventory = true;
                    var countText = line[9..line.IndexOf(" benchmarks:", StringComparison.Ordinal)];
                    if (int.TryParse(countText, System.Globalization.NumberStyles.None,
                            System.Globalization.CultureInfo.InvariantCulture, out var count))
                        expectedInventoryCount = (expectedInventoryCount ?? 0) + count;
                    else
                        inventoryCountParsingFailed = true;
                    continue;
                }

                if (collectingInventory)
                {
                    if (line.StartsWith("//   ", StringComparison.Ordinal))
                    {
                        var displayInfo = line[5..].TrimEnd();
                        if (displayInfo.Length > 0)
                            AddPlanned(planned, displayInfo, lineNumber);
                        continue;
                    }
                    if (line.StartsWith("//", StringComparison.Ordinal))
                        collectingInventory = false;
                }

                const string benchmarkPrefix = "// Benchmark: ";
                if (line.StartsWith(benchmarkPrefix, StringComparison.Ordinal))
                {
                    currentBenchmark = line[benchmarkPrefix.Length..].TrimEnd();
                    AddPlanned(planned, currentBenchmark);
                    startedAt.TryAdd(currentBenchmark, lineNumber);
                    continue;
                }

                if (currentBenchmark is not null && line.StartsWith("Mean = ", StringComparison.Ordinal))
                {
                    completedAt[currentBenchmark] = lineNumber;
                    continue;
                }

                const string exitMarker = " has exited with code ";
                var exitMarkerIndex = line.IndexOf(exitMarker, StringComparison.Ordinal);
                if (currentBenchmark is not null && line.StartsWith("// Benchmark Process ", StringComparison.Ordinal) && exitMarkerIndex >= 0)
                {
                    var codeText = line[(exitMarkerIndex + exitMarker.Length)..].TrimEnd('.');
                    if (int.TryParse(codeText, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var exitCode) && exitCode != 0)
                        failed.Add(currentBenchmark);
                }
            }
        }

        var rawResults = ReadRawResults(runDirectory, resultsDirectory, diagnostics);
        foreach (var result in rawResults.Values)
            AddPlanned(planned, result.DisplayInfo);
        if (inventoryCountParsingFailed)
        {
            inventoryKnown = false;
            diagnostics.Add("One or more BenchmarkDotNet discovery counts could not be parsed.");
        }
        var unenumeratedCaseCount = 0;
        if (expectedInventoryCount is int expected && expected != planned.Count && inventoryKnown)
        {
            unenumeratedCaseCount = Math.Max(0, expected - planned.Count);
            diagnostics.Add($"BenchmarkDotNet listed {expected} cases but {planned.Count} case identities were recovered.");
            inventoryKnown = false;
        }

        var parsedCases = new List<BenchmarkCaseCoverageRecord>(planned.Count);
        var runWasInterrupted = outcome is OutcomeKind.Timeout or OutcomeKind.Cancelled ||
            timeoutSource is "aggregate" or "project" or "external-cancellation";
        foreach (var item in planned)
        {
            rawResults.TryGetValue(item.DisplayInfo, out var rawResult);
            var hasCompletedOutput = completedAt.TryGetValue(item.DisplayInfo, out var completionLine);
            var hasStarted = startedAt.TryGetValue(item.DisplayInfo, out var startLine);
            var isFailed = failed.Contains(item.DisplayInfo) || rawResult?.Failed == true;
            var isCompleted = hasCompletedOutput || rawResult?.Completed == true;
            var state = isCompleted
                ? "completed"
                : isFailed
                    ? "failed"
                    : hasStarted
                        ? runWasInterrupted
                            ? "interrupted"
                            : "failed"
                        : "never-started";
            var locations = new List<string>();
            if (item.ListedAtLine is int discoveredLine)
                locations.Add($"{Relative(runDirectory, stdoutPath)}#L{discoveredLine}");
            if (hasStarted)
                locations.Add($"{Relative(runDirectory, stdoutPath)}#L{startLine}");
            if (hasCompletedOutput)
                locations.Add($"{Relative(runDirectory, stdoutPath)}#L{completionLine}");
            if (rawResult is not null)
                locations.AddRange(rawResult.Paths);
            parsedCases.Add(new BenchmarkCaseCoverageRecord(
                CreateCaseId(projectKey, item.DisplayInfo),
                projectKey,
                invocationName,
                GetBenchmarkClass(item.DisplayInfo),
                item.DisplayInfo,
                state,
                item.ListedAtLine,
                hasStarted ? startLine : null,
                hasCompletedOutput ? completionLine : null,
                locations.Distinct(StringComparer.Ordinal).ToArray()));
        }

        if (!inventoryKnown && parsedCases.Count > 0)
            diagnostics.Add("The BenchmarkDotNet discovery list was not present; only identities found in retained result files are known.");
        if (projectResult is null && (outcome is OutcomeKind.Timeout or OutcomeKind.Cancelled))
            diagnostics.Add("The child invocation did not reach a terminal result before the run was interrupted.");

        return new ParsedInvocation(inventoryKnown, expectedInventoryCount ?? planned.Count,
            unenumeratedCaseCount, parsedCases, diagnostics);
    }

    private static Dictionary<string, RawResult> ReadRawResults(
        string runDirectory,
        string resultsDirectory,
        ICollection<string> diagnostics)
    {
        var results = new Dictionary<string, RawResult>(StringComparer.Ordinal);
        if (!Directory.Exists(resultsDirectory))
            return results;

        foreach (var path in Directory.EnumerateFiles(resultsDirectory, "*-report-full.json", SearchOption.AllDirectories)
                     .OrderBy(static value => value, StringComparer.Ordinal))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(path));
                if (!document.RootElement.TryGetProperty("Benchmarks", out var benchmarks) || benchmarks.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var benchmark in benchmarks.EnumerateArray())
                {
                    if (!benchmark.TryGetProperty("DisplayInfo", out var displayInfoElement) || displayInfoElement.ValueKind != JsonValueKind.String)
                        continue;
                    var displayInfo = displayInfoElement.GetString();
                    if (string.IsNullOrWhiteSpace(displayInfo))
                        continue;
                    var completed = benchmark.TryGetProperty("Statistics", out var statistics) &&
                        statistics.ValueKind == JsonValueKind.Object &&
                        statistics.TryGetProperty("N", out var count) && count.TryGetInt32(out var samples) && samples > 0;
                    var failed = benchmark.TryGetProperty("Result", out var resultElement) && resultElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
                    var location = Relative(runDirectory, path);
                    if (results.TryGetValue(displayInfo, out var existing))
                        results[displayInfo] = existing with
                        {
                            Completed = existing.Completed || completed,
                            Failed = existing.Failed || failed,
                            Paths = existing.Paths.Append(location).Distinct(StringComparer.Ordinal).ToArray()
                        };
                    else
                        results.Add(displayInfo, new RawResult(displayInfo, completed, failed, [location]));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                diagnostics.Add($"Could not read result file '{Relative(runDirectory, path)}': {exception.Message}");
            }
        }

        return results;
    }

    private static IReadOnlyList<string> BuildRerunArguments(
        BenchmarkExecutionPlan plan,
        BenchmarkInvocation invocation,
        string? benchmarkClass)
    {
        var arguments = new List<string>
        {
            "benchmark", "--suite", invocation.ProjectKey, "--strat", plan.Strategy,
            "--framework", plan.Framework, "--dataset", plan.Dataset
        };
        var documentCount = invocation.DocumentCount is > 0 ? invocation.DocumentCount.Value : plan.DocumentCount;
        if (documentCount > 0)
        {
            arguments.Add("--doc-count");
            arguments.Add(documentCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (plan.TimeoutBudget.AggregateTimeoutSource == "command-line")
        {
            arguments.Add("--timeout-minutes");
            arguments.Add(plan.TimeoutBudget.AggregateTimeoutMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (plan.ReferencePath is not null)
        {
            arguments.Add("--reference-path");
            arguments.Add(plan.ReferencePath);
        }

        arguments.Add("--");
        arguments.AddRange(plan.BenchmarkDotNetArguments);
        if (benchmarkClass is not null)
        {
            arguments.Add("--filter");
            arguments.Add($"*{benchmarkClass}*");
        }
        return arguments;
    }

    private static void AddPlanned(List<PlannedCase> planned, string displayInfo, int? listedAtLine = null)
    {
        var existingIndex = planned.FindIndex(item => item.DisplayInfo.Equals(displayInfo, StringComparison.Ordinal));
        if (existingIndex < 0)
            planned.Add(new PlannedCase(displayInfo, listedAtLine));
        else if (listedAtLine is not null && planned[existingIndex].ListedAtLine is null)
            planned[existingIndex] = planned[existingIndex] with { ListedAtLine = listedAtLine };
    }

    private static string CreateCaseId(string projectKey, string displayInfo)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(projectKey + "\0" + displayInfo));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string GetBenchmarkClass(string displayInfo)
    {
        var methodName = displayInfo.Split(':', 2)[0];
        var separator = methodName.LastIndexOf('.');
        return separator > 0 ? methodName[..separator] : methodName;
    }

    private static void AddExistingLocation(string runDirectory, string path, ICollection<string> locations)
    {
        if (File.Exists(path))
            locations.Add(Relative(runDirectory, path));
    }

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private sealed record PlannedCase(string DisplayInfo, int? ListedAtLine);

    private sealed record RawResult(string DisplayInfo, bool Completed, bool Failed, IReadOnlyList<string> Paths);

    private sealed record ParsedInvocation(
        bool InventoryKnown,
        int PlannedCaseCount,
        int UnenumeratedCaseCount,
        IReadOnlyList<BenchmarkCaseCoverageRecord> Cases,
        IReadOnlyList<string> Diagnostics);
}
