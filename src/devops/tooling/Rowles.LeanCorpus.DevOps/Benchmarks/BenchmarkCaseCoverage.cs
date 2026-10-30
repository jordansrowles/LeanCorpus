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
    string? BenchmarkMethod,
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
            if (rerunClasses.Length == 0 && (unfinishedCases.Length > 0 || projectResult?.Status != "Passed"))
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
            SchemaVersion: 2,
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
        var diagnostics = new List<string>();
        var inventoryKnown = false;
        var collectingInventory = false;
        var inDetailedResults = false;
        int? expectedInventoryCount = null;
        bool inventoryCountParsingFailed = false;
        PlannedCase? currentBenchmark = null;
        int lineNumber = 0;

        if (File.Exists(stdoutPath))
        {
            foreach (var line in File.ReadLines(stdoutPath))
            {
                lineNumber++;
                if (line.StartsWith("// Found ", StringComparison.Ordinal) && line.Contains(" benchmarks:", StringComparison.Ordinal))
                {
                    inDetailedResults = false;
                    currentBenchmark = null;
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

                if (line.StartsWith("// * Detailed results *", StringComparison.Ordinal))
                {
                    currentBenchmark = null;
                    collectingInventory = false;
                    inDetailedResults = true;
                    continue;
                }

                if (inDetailedResults)
                    continue;

                if (collectingInventory)
                {
                    if (line.StartsWith("//   ", StringComparison.Ordinal))
                    {
                        var displayInfo = line[5..].TrimEnd();
                        if (displayInfo.Length > 0)
                            AddPlanned(planned, displayInfo, lineNumber, allowDuplicate: true);
                        continue;
                    }
                    if (line.StartsWith("//", StringComparison.Ordinal))
                        collectingInventory = false;
                }

                const string benchmarkPrefix = "// Benchmark: ";
                if (line.StartsWith(benchmarkPrefix, StringComparison.Ordinal))
                {
                    var displayInfo = line[benchmarkPrefix.Length..].TrimEnd();
                    currentBenchmark = planned.FirstOrDefault(item => item.DisplayInfo.Equals(displayInfo, StringComparison.Ordinal) &&
                        item.StartedAtLine is null) ?? AddPlanned(planned, displayInfo, allowDuplicate: true);
                    currentBenchmark.StartedAtLine = lineNumber;
                    continue;
                }

                if (currentBenchmark is not null && line.StartsWith("// Execute: ", StringComparison.Ordinal))
                {
                    currentBenchmark.SetIdentity(ParseBenchmarkFullName(ExtractBenchmarkName(line)));
                    continue;
                }

                const string exitMarker = " has exited with code ";
                var exitMarkerIndex = line.IndexOf(exitMarker, StringComparison.Ordinal);
                if (currentBenchmark is not null && line.StartsWith("// Benchmark Process ", StringComparison.Ordinal) && exitMarkerIndex >= 0)
                {
                    var codeText = line[(exitMarkerIndex + exitMarker.Length)..].TrimEnd('.');
                    if (int.TryParse(codeText, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var exitCode) && exitCode != 0)
                        currentBenchmark.Failed = true;
                    continue;
                }

                if (currentBenchmark is not null && line.StartsWith("Mean = ", StringComparison.Ordinal))
                    currentBenchmark.CompletedAtLine = lineNumber;
            }
        }

        var rawResults = ReadRawResults(runDirectory, resultsDirectory, diagnostics);
        foreach (var result in rawResults)
        {
            var matchingCase = result.FullName is null
                ? null
                : planned.FirstOrDefault(item => item.DisplayInfo.Equals(result.DisplayInfo, StringComparison.Ordinal) &&
                    item.FullName?.Equals(result.FullName, StringComparison.Ordinal) == true);
            matchingCase ??= planned.FirstOrDefault(item => item.DisplayInfo.Equals(result.DisplayInfo, StringComparison.Ordinal) &&
                item.FullName is null && !item.HasRawResult);
            matchingCase ??= result.FullName is null
                ? planned.FirstOrDefault(item => item.DisplayInfo.Equals(result.DisplayInfo, StringComparison.Ordinal) && item.HasRawResult)
                : null;
            matchingCase ??= AddPlanned(planned, result.DisplayInfo, identity: new BenchmarkIdentity(
                result.BenchmarkClass, result.BenchmarkMethod, result.FullName), allowDuplicate: true);
            matchingCase.SetIdentity(new BenchmarkIdentity(result.BenchmarkClass, result.BenchmarkMethod, result.FullName));
            matchingCase.RawCompleted |= result.Completed;
            matchingCase.RawFailed |= result.Failed;
            matchingCase.HasRawResult = true;
            matchingCase.ResultLocations.AddRange(result.ResultLocations);
        }

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
        foreach (var (item, occurrence) in planned.Select(static (item, occurrence) => (item, occurrence)))
        {
            var hasStarted = item.StartedAtLine is not null;
            var isFailed = item.Failed || item.RawFailed;
            var isCompleted = !isFailed && (item.CompletedAtLine is not null || item.RawCompleted);
            var state = isFailed
                ? "failed"
                : isCompleted
                    ? "completed"
                    : hasStarted
                        ? runWasInterrupted
                            ? "interrupted"
                            : "failed"
                        : "never-started";
            var locations = new List<string>();
            if (item.ListedAtLine is int discoveredLine)
                locations.Add($"{Relative(runDirectory, stdoutPath)}#L{discoveredLine}");
            if (item.StartedAtLine is int startLine)
                locations.Add($"{Relative(runDirectory, stdoutPath)}#L{startLine}");
            if (item.CompletedAtLine is int completionLine)
                locations.Add($"{Relative(runDirectory, stdoutPath)}#L{completionLine}");
            locations.AddRange(item.ResultLocations);
            parsedCases.Add(new BenchmarkCaseCoverageRecord(
                CreateCaseId(projectKey, item.DisplayInfo + "\0" + (item.FullName ?? string.Empty) + "\0" +
                    occurrence.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                projectKey,
                invocationName,
                item.BenchmarkClass ?? string.Empty,
                item.BenchmarkMethod,
                item.DisplayInfo,
                state,
                item.ListedAtLine,
                item.StartedAtLine,
                item.CompletedAtLine,
                locations.Distinct(StringComparer.Ordinal).ToArray()));
        }

        if (!inventoryKnown && parsedCases.Count > 0)
            diagnostics.Add("The BenchmarkDotNet discovery list was not present; only identities found in retained result files are known.");
        if (projectResult is null && (outcome is OutcomeKind.Timeout or OutcomeKind.Cancelled))
            diagnostics.Add("The child invocation did not reach a terminal result before the run was interrupted.");

        return new ParsedInvocation(inventoryKnown, expectedInventoryCount ?? planned.Count,
            unenumeratedCaseCount, parsedCases, diagnostics);
    }

    private static IReadOnlyList<RawResult> ReadRawResults(
        string runDirectory,
        string resultsDirectory,
        ICollection<string> diagnostics)
    {
        var results = new List<RawResult>();
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
                    var identity = ReadBenchmarkIdentity(benchmark, displayInfo);
                    var completed = benchmark.TryGetProperty("Statistics", out var statistics) &&
                        statistics.ValueKind == JsonValueKind.Object &&
                        statistics.TryGetProperty("N", out var count) && count.TryGetInt32(out var samples) && samples > 0;
                    var failed = benchmark.TryGetProperty("Result", out var resultElement) && resultElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
                    var location = Relative(runDirectory, path);
                    results.Add(new RawResult(displayInfo, identity.BenchmarkClass, identity.BenchmarkMethod,
                        identity.FullName, completed, failed, [location]));
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

    private static PlannedCase AddPlanned(
        List<PlannedCase> planned,
        string displayInfo,
        int? listedAtLine = null,
        BenchmarkIdentity? identity = null,
        bool allowDuplicate = false)
    {
        if (!allowDuplicate)
        {
            var existing = planned.FirstOrDefault(item => item.DisplayInfo.Equals(displayInfo, StringComparison.Ordinal));
            if (existing is not null)
            {
                if (existing.ListedAtLine is null)
                    existing.ListedAtLine = listedAtLine;
                if (identity is not null)
                    existing.SetIdentity(identity);
                return existing;
            }
        }

        var item = new PlannedCase(displayInfo, listedAtLine);
        item.SetIdentity(identity ?? ParseDisplayInfoIdentity(displayInfo));
        planned.Add(item);
        return item;
    }

    private static string CreateCaseId(string projectKey, string displayInfo)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(projectKey + "\0" + displayInfo));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ExtractBenchmarkName(string executeLine)
    {
        const string marker = "--benchmarkName ";
        var markerIndex = executeLine.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return string.Empty;

        var value = executeLine[(markerIndex + marker.Length)..].TrimStart();
        if (value.Length == 0)
            return string.Empty;
        var quote = value[0];
        if (quote is not ('"' or '\''))
            return value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];

        var builder = new StringBuilder(value.Length);
        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (character == quote)
                return builder.ToString();
            if (character == '\\' && index + 1 < value.Length && value[index + 1] == quote)
            {
                builder.Append(quote);
                index++;
                continue;
            }
            builder.Append(character);
        }

        return string.Empty;
    }

    private static BenchmarkIdentity ParseBenchmarkFullName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return new BenchmarkIdentity(null, null, null);

        var openParenthesis = fullName.IndexOf('(');
        var qualifiedMethod = openParenthesis >= 0 ? fullName[..openParenthesis] : fullName;
        var separator = qualifiedMethod.LastIndexOf('.');
        if (separator <= 0 || separator == qualifiedMethod.Length - 1)
            return new BenchmarkIdentity(null, null, fullName);

        var method = qualifiedMethod[(separator + 1)..];
        return IsIdentifier(method)
            ? new BenchmarkIdentity(qualifiedMethod[..separator], method, fullName)
            : new BenchmarkIdentity(null, null, fullName);
    }

    private static BenchmarkIdentity ParseDisplayInfoIdentity(string displayInfo)
    {
        // BenchmarkDotNet wraps a friendly method title in quotes for custom names.
        // The class prefix remains useful for a safe class filter, while the title
        // must not be mistaken for a method or appended to the class name.
        var quotedTitleSeparator = displayInfo.IndexOf(".'", StringComparison.Ordinal);
        if (quotedTitleSeparator >= 0)
        {
            var className = displayInfo[..quotedTitleSeparator];
            return new BenchmarkIdentity(className.Length > 0 ? className : null, null, null);
        }

        var colon = displayInfo.IndexOf(':');
        var qualifiedMethod = colon >= 0 ? displayInfo[..colon] : displayInfo;
        var separator = qualifiedMethod.LastIndexOf('.');
        if (separator <= 0 || separator == qualifiedMethod.Length - 1)
            return new BenchmarkIdentity(null, null, null);

        var method = qualifiedMethod[(separator + 1)..];
        return IsIdentifier(method)
            ? new BenchmarkIdentity(qualifiedMethod[..separator], method, null)
            : new BenchmarkIdentity(null, null, null);
    }

    private static BenchmarkIdentity ReadBenchmarkIdentity(JsonElement benchmark, string displayInfo)
    {
        var fullName = ReadStringProperty(benchmark, "FullName");
        var parsedFullName = ParseBenchmarkFullName(fullName ?? string.Empty);
        var benchmarkNamespace = ReadStringProperty(benchmark, "Namespace");
        var benchmarkType = ReadStringProperty(benchmark, "Type");
        var benchmarkClass = !string.IsNullOrWhiteSpace(benchmarkType)
            ? string.IsNullOrWhiteSpace(benchmarkNamespace)
                ? benchmarkType
                : $"{benchmarkNamespace}.{benchmarkType}"
            : parsedFullName.BenchmarkClass;
        var benchmarkMethod = ReadStringProperty(benchmark, "Method") ?? parsedFullName.BenchmarkMethod;

        if (benchmarkClass is null || benchmarkMethod is null)
        {
            var displayIdentity = ParseDisplayInfoIdentity(displayInfo);
            benchmarkClass ??= displayIdentity.BenchmarkClass;
            benchmarkMethod ??= displayIdentity.BenchmarkMethod;
        }

        return new BenchmarkIdentity(benchmarkClass, benchmarkMethod, fullName ?? parsedFullName.FullName);
    }

    private static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool IsIdentifier(string value)
    {
        if (value.Length == 0 || !(char.IsLetter(value[0]) || value[0] == '_'))
            return false;
        for (var index = 1; index < value.Length; index++)
        {
            if (!(char.IsLetterOrDigit(value[index]) || value[index] == '_'))
                return false;
        }
        return true;
    }

    private static void AddExistingLocation(string runDirectory, string path, ICollection<string> locations)
    {
        if (File.Exists(path))
            locations.Add(Relative(runDirectory, path));
    }

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private sealed class PlannedCase(string displayInfo, int? listedAtLine)
    {
        public string DisplayInfo { get; } = displayInfo;
        public int? ListedAtLine { get; set; } = listedAtLine;
        public string? BenchmarkClass { get; private set; }
        public string? BenchmarkMethod { get; private set; }
        public string? FullName { get; private set; }
        public int? StartedAtLine { get; set; }
        public int? CompletedAtLine { get; set; }
        public bool Failed { get; set; }
        public bool RawFailed { get; set; }
        public bool RawCompleted { get; set; }
        public bool HasRawResult { get; set; }
        public List<string> ResultLocations { get; } = [];

        public void SetIdentity(BenchmarkIdentity? identity)
        {
            if (identity is null)
                return;
            if (!string.IsNullOrWhiteSpace(identity.BenchmarkClass))
                BenchmarkClass = identity.BenchmarkClass;
            if (!string.IsNullOrWhiteSpace(identity.BenchmarkMethod))
                BenchmarkMethod = identity.BenchmarkMethod;
            if (!string.IsNullOrWhiteSpace(identity.FullName))
                FullName = identity.FullName;
        }
    }

    private sealed record BenchmarkIdentity(string? BenchmarkClass, string? BenchmarkMethod, string? FullName);

    private sealed record RawResult(
        string DisplayInfo,
        string? BenchmarkClass,
        string? BenchmarkMethod,
        string? FullName,
        bool Completed,
        bool Failed,
        IReadOnlyList<string> ResultLocations);

    private sealed record ParsedInvocation(
        bool InventoryKnown,
        int PlannedCaseCount,
        int UnenumeratedCaseCount,
        IReadOnlyList<BenchmarkCaseCoverageRecord> Cases,
        IReadOnlyList<string> Diagnostics);
}
