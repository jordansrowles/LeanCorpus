using System.Globalization;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record ManagedTestTelemetrySummary(
    int Executions,
    long TestsStarted,
    long TestsFinished,
    long TestsPassed,
    long TestsFailed,
    long TestsSkipped,
    long RetainedTestSummaries,
    long Activities,
    long Metrics,
    long SwallowedExceptions,
    long OrphanedActivities,
    long TelemetryErrors,
    bool Truncated,
    IReadOnlyList<object> Streams,
    IReadOnlyList<string> RetainedTestSummaryPaths,
    IReadOnlyList<JsonElement> RetainedTests,
    IReadOnlyList<string> SummaryPaths,
    IReadOnlyList<string> Errors);

internal sealed class TestReportWriter(IAtomicFileWriter writer)
{
    private const long MaximumSummaryBytes = 1024 * 1024;
    private const long MaximumRetainedTestSummaryBytes = 16 * 1024 * 1024;
    private static readonly string[] TimingHeaders =
    [
        "recordType", "iteration", "suite", "framework", "target", "test", "outcome", "durationMs"
    ];

    public async Task WriteSupplementaryReportsAsync(
        string runDirectory,
        RunManifest manifest,
        TestExecutorRequest request,
        TestRunAggregate? aggregate,
        CommandResult commandResult,
        IReadOnlyList<TestPreparationTiming> preparationTimings,
        ManagedTestTelemetrySummary telemetry,
        DateTimeOffset completedUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(commandResult);
        ArgumentNullException.ThrowIfNull(preparationTimings);
        ArgumentNullException.ThrowIfNull(telemetry);

        var markdown = BuildMarkdown(runDirectory, manifest, request, aggregate, commandResult, preparationTimings, telemetry, completedUtc);
        await writer.WriteReplaceAsync(Path.Combine(runDirectory, "report.md"), Encoding.UTF8.GetBytes(markdown), cancellationToken)
            .ConfigureAwait(false);
        var timings = BuildTimingsCsv(preparationTimings, aggregate);
        await writer.WriteReplaceAsync(Path.Combine(runDirectory, "timings.csv"), Encoding.UTF8.GetBytes(timings), cancellationToken)
            .ConfigureAwait(false);
    }

    private static string BuildMarkdown(
        string runDirectory,
        RunManifest manifest,
        TestExecutorRequest request,
        TestRunAggregate? aggregate,
        CommandResult commandResult,
        IReadOnlyList<TestPreparationTiming> preparationTimings,
        ManagedTestTelemetrySummary telemetry,
        DateTimeOffset completedUtc)
    {
        var builder = new StringBuilder();
        var attempts = aggregate?.Attempts ?? [];
        var tests = aggregate?.Tests ?? [];
        var requestedExecutions = checked(request.Targets.Count * request.Count);
        var scheduledExecutions = attempts.Count;
        var completedExecutions = attempts.Count(static attempt => attempt.Completed);
        var completedIterations = CountCompletedIterations(request, attempts);
        var failureRate = scheduledExecutions == 0
            ? 0
            : attempts.Count(static attempt => attempt.Outcome != TestExecutionOutcome.Passed) / (double)scheduledExecutions;
        var durations = attempts.Select(static attempt => attempt.Duration.TotalMilliseconds).Order().ToArray();
        var totalDuration = Math.Max(0, (completedUtc - manifest.StartedUtc).TotalMilliseconds);

        builder.AppendLine("# Test run summary");
        builder.AppendLine();
        builder.AppendLine($"- Run ID: `{EscapeInline(manifest.RunId.ToString("D"))}`");
        builder.AppendLine($"- Command: `{EscapeInline(request.CommandLine)}`");
        builder.AppendLine($"- Started: {manifest.StartedUtc.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Finished: {completedUtc.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Result: **{(aggregate?.Succeeded == true ? "Passed" : "Not passed")}** ({EscapeCell(commandResult.Outcome.ToString())})");
        builder.AppendLine();

        builder.AppendLine("## Environment");
        builder.AppendLine();
        builder.AppendLine($"- Platform: {EscapeCell(manifest.Platform)}");
        builder.AppendLine($"- SDK: {EscapeCell(GetToolVersion(manifest, "dotnet-sdk-selected"))}");
        builder.AppendLine($"- Commit: `{EscapeInline(manifest.SourceCommit ?? string.Empty)}`");
        builder.AppendLine($"- Branch: `{EscapeInline(manifest.SourceBranch ?? string.Empty)}`");
        builder.AppendLine($"- Worktree dirty: {manifest.SourceDirty.ToString().ToLowerInvariant()}");
        builder.AppendLine();

        builder.AppendLine("## Targets");
        builder.AppendLine();
        builder.AppendLine("| Target | Runner | Framework | RID | Filter |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var target in request.Targets)
        {
            builder.AppendLine($"| {EscapeCell(target.Key)} | {EscapeCell(target.RunnerKind)} | {EscapeCell(target.Framework)} | {EscapeCell(target.RuntimeIdentifier)} | {EscapeCell(target.Filter)} |");
        }
        builder.AppendLine();

        builder.AppendLine("## Timing");
        builder.AppendLine();
        builder.AppendLine($"- Requested iterations: {request.Count}");
        builder.AppendLine($"- Completed iterations: {completedIterations}");
        builder.AppendLine($"- Scheduled target executions: {scheduledExecutions}/{requestedExecutions}");
        builder.AppendLine($"- Completed target executions: {completedExecutions}");
        builder.AppendLine($"- Failure rate: {failureRate.ToString("P1", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Target duration: min {FormatDuration(Percentile(durations, 0))}, median {FormatDuration(Percentile(durations, 0.5))}, P95 {FormatDuration(Percentile(durations, 0.95))}, max {FormatDuration(durations.Length == 0 ? 0 : durations[^1])}");
        builder.AppendLine($"- Total duration: {FormatDuration(totalDuration)}");
        builder.AppendLine();

        if (preparationTimings.Count > 0)
        {
            builder.AppendLine("### Preparation details");
            builder.AppendLine();
            builder.AppendLine("| Stage | Operation | Work item | Targets | Duration |");
            builder.AppendLine("| --- | --- | --- | --- | ---: |");
            foreach (var timing in preparationTimings)
            {
                builder.AppendLine($"| {EscapeCell(timing.Stage)} | {EscapeCell(timing.Operation)} | {EscapeCell(timing.WorkItem)} | {EscapeCell(string.Join(", ", timing.TargetKeys))} | {FormatDuration(timing.DurationMs)} |");
            }
            builder.AppendLine();
        }

        builder.AppendLine("## Target outcomes");
        builder.AppendLine();
        builder.AppendLine("| Iteration | Target | Outcome | Duration |");
        builder.AppendLine("| ---: | --- | --- | ---: |");
        foreach (var attempt in attempts)
        {
            builder.AppendLine($"| {attempt.Iteration} | {EscapeCell(attempt.Target.Key)} | {OutcomeForReport(attempt.Outcome)} | {FormatDuration(attempt.Duration.TotalMilliseconds)} |");
        }
        builder.AppendLine();

        builder.AppendLine("## Counts");
        builder.AppendLine();
        foreach (var outcome in new[] { "Passed", "Failed", "Crashed", "Hung", "Cancelled", "Incomplete", "InfrastructureError" })
        {
            var count = attempts.Count(attempt => OutcomeForReport(attempt.Outcome) == outcome);
            builder.AppendLine($"- {outcome}: {count}");
        }
        var failingIterations = attempts.Where(static attempt => attempt.Outcome != TestExecutionOutcome.Passed)
            .Select(static attempt => attempt.Iteration).Distinct().Order().ToArray();
        if (failingIterations.Length > 0)
        {
            builder.AppendLine($"- Failing iterations: {string.Join(", ", failingIterations)}");
        }
        builder.AppendLine();

        builder.AppendLine("## Tests");
        builder.AppendLine();
        if (tests.Count == 0)
        {
            builder.AppendLine("No test-case results were recorded.");
        }
        else
        {
            builder.AppendLine("| Test | Target | Classification | Attempts | Details |");
            builder.AppendLine("| --- | --- | --- | ---: | --- |");
            foreach (var test in tests)
            {
                var classification = TestClassification(test);
                var observations = string.Join("; ", test.Attempts.OrderBy(static attempt => attempt.Iteration)
                    .Select(static attempt => $"{attempt.Iteration}: {attempt.Outcome}"));
                var details = string.Join("; ", test.Attempts
                    .Select(static attempt => attempt.SkipReason ?? attempt.ErrorMessage)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal));
                builder.AppendLine($"| {EscapeCell(test.TestName)} | {EscapeCell(test.TargetKey)} | {EscapeCell(classification)} | {test.Attempts.Count}/{test.ExpectedAttemptCount} | {EscapeCell(details.Length == 0 ? observations : details)} |");
            }
        }
        builder.AppendLine();

        var unscheduled = GetUnscheduledTargets(request, aggregate);
        if (unscheduled.Count > 0)
        {
            builder.AppendLine("## Unscheduled targets");
            builder.AppendLine();
            foreach (var (target, iteration) in unscheduled)
            {
                builder.AppendLine($"- iteration {iteration}: `{EscapeInline(target)}`");
            }
            builder.AppendLine();
        }

        AppendTelemetrySummary(builder, telemetry);

        builder.AppendLine("## Diagnostics and artefacts");
        builder.AppendLine();
        builder.AppendLine($"- Run directory: `artifacts/test/runs/{manifest.RunId:D}`");
        var artifactPaths = attempts.SelectMany(static attempt => new[]
                { attempt.StandardOutputPath, attempt.StandardErrorPath, attempt.TrxPath }
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Cast<string>()
                .Concat(attempt.DiagnosticPaths))
            .Select(path => GetRelativePath(runDirectory, path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        foreach (var path in artifactPaths)
        {
            builder.AppendLine($"- `{EscapeInline(path)}`");
        }
        var failures = attempts.Where(static attempt => !string.IsNullOrWhiteSpace(attempt.Failure))
            .Select(static attempt => attempt.Failure!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (failures.Length > 0 || !string.IsNullOrWhiteSpace(commandResult.Details))
        {
            builder.AppendLine();
            builder.AppendLine("### Execution errors");
            foreach (var failure in failures.Append(commandResult.Details).Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
            {
                builder.AppendLine($"- {EscapeCell(failure)}");
            }
        }

        return builder.ToString();
    }

    internal static ManagedTestTelemetrySummary ReadTelemetrySummary(string runDirectory, TestRunAggregate? aggregate)
    {
        var executions = 0;
        long testsStarted = 0;
        long testsFinished = 0;
        long testsPassed = 0;
        long testsFailed = 0;
        long testsSkipped = 0;
        long retainedTestSummaries = 0;
        long activities = 0;
        long metrics = 0;
        long swallowedExceptions = 0;
        long orphanedActivities = 0;
        long telemetryErrors = 0;
        var truncated = false;
        var streams = new List<object>();
        var retainedTestSummaryPaths = new List<string>();
        var retainedTests = new List<JsonElement>();
        var summaryPaths = new List<string>();
        var errors = new List<string>();
        var seenSummaryPaths = new HashSet<string>(StringComparer.Ordinal);
        var seenRetainedPaths = new HashSet<string>(StringComparer.Ordinal);

        foreach (var attempt in aggregate?.Attempts ?? [])
        {
            if (string.IsNullOrWhiteSpace(attempt.ArtifactDirectory))
            {
                continue;
            }

            var telemetryDirectory = Path.Combine(attempt.ArtifactDirectory, "telemetry");
            var summaryPath = Path.Combine(telemetryDirectory, "summary.json");
            if (File.Exists(summaryPath) && seenSummaryPaths.Add(Path.GetFullPath(summaryPath)))
            {
                var relativePath = GetRelativePath(runDirectory, summaryPath);
                summaryPaths.Add(relativePath);
                try
                {
                    if (new FileInfo(summaryPath).Length > MaximumSummaryBytes)
                    {
                        errors.Add($"Telemetry summary '{relativePath}' exceeds the {MaximumSummaryBytes} byte limit.");
                    }
                    else
                    {
                        using var document = JsonDocument.Parse(File.ReadAllText(summaryPath));
                        var root = document.RootElement;
                        if (root.ValueKind != JsonValueKind.Object)
                        {
                            throw new JsonException("The summary root must be an object.");
                        }

                        executions++;
                        testsStarted += GetInt64(root, "testsStarted");
                        testsFinished += GetInt64(root, "testsFinished");
                        testsPassed += GetInt64(root, "testsPassed");
                        testsFailed += GetInt64(root, "testsFailed");
                        testsSkipped += GetInt64(root, "testsSkipped");
                        retainedTestSummaries += GetInt64(root, "retainedTestSummaries");
                        activities += GetInt64(root, "activities");
                        metrics += GetInt64(root, "metrics");
                        swallowedExceptions += GetInt64(root, "swallowedExceptions");
                        orphanedActivities += GetInt64(root, "orphanedActivities");
                        telemetryErrors += GetInt64(root, "telemetryErrors");
                        if (root.TryGetProperty("streams", out var streamDocument) && streamDocument.ValueKind == JsonValueKind.Object)
                        {
                            streams.Add(new
                            {
                                targetKey = attempt.Target.Key,
                                iteration = attempt.Iteration,
                                mode = GetString(root, "mode"),
                                streams = streamDocument.Clone()
                            });
                            truncated |= HasTruncatedStream(streamDocument);
                        }
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
                {
                    errors.Add($"Telemetry summary read failed for '{relativePath}': {exception.Message}");
                }
            }

            var retainedPath = Path.Combine(telemetryDirectory, "tests.ndjson");
            if (!File.Exists(retainedPath) || !seenRetainedPaths.Add(Path.GetFullPath(retainedPath)))
            {
                continue;
            }

            var retainedRelativePath = GetRelativePath(runDirectory, retainedPath);
            retainedTestSummaryPaths.Add(retainedRelativePath);
            try
            {
                if (new FileInfo(retainedPath).Length > MaximumRetainedTestSummaryBytes)
                {
                    errors.Add($"Retained telemetry summaries '{retainedRelativePath}' exceed the {MaximumRetainedTestSummaryBytes} byte limit.");
                    continue;
                }

                var lineNumber = 0;
                foreach (var line in File.ReadLines(retainedPath))
                {
                    lineNumber++;
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        if (document.RootElement.ValueKind != JsonValueKind.Object)
                        {
                            throw new JsonException("The summary record must be an object.");
                        }
                        retainedTests.Add(document.RootElement.Clone());
                    }
                    catch (JsonException exception)
                    {
                        errors.Add($"Retained telemetry summary '{retainedRelativePath}' line {lineNumber} is invalid: {exception.Message}");
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Retained telemetry summary read failed for '{retainedRelativePath}': {exception.Message}");
            }
        }

        return new ManagedTestTelemetrySummary(executions, testsStarted, testsFinished, testsPassed, testsFailed,
            testsSkipped, retainedTestSummaries, activities, metrics, swallowedExceptions, orphanedActivities,
            telemetryErrors, truncated, streams, retainedTestSummaryPaths, retainedTests, summaryPaths, errors);
    }

    private static void AppendTelemetrySummary(StringBuilder builder, ManagedTestTelemetrySummary telemetry)
    {
        builder.AppendLine("## Telemetry summary");
        builder.AppendLine();
        builder.AppendLine($"- Executions: {telemetry.Executions}; tests: {telemetry.TestsFinished} finished of {telemetry.TestsStarted} started ({telemetry.TestsPassed} passed, {telemetry.TestsFailed} failed, {telemetry.TestsSkipped} skipped).");
        builder.AppendLine($"- Activities: {telemetry.Activities}; metrics: {telemetry.Metrics}; swallowed exceptions: {telemetry.SwallowedExceptions}; orphaned activities: {telemetry.OrphanedActivities}; telemetry errors: {telemetry.TelemetryErrors}.");
        builder.AppendLine($"- Retained test summaries: {telemetry.RetainedTestSummaries}.");
        if (telemetry.Executions == 0)
        {
            builder.AppendLine("- No telemetry summary files were recorded.");
        }
        else if (telemetry.Truncated)
        {
            builder.AppendLine("- **TRUNCATED:** one or more telemetry streams reached their byte limit; see stream counters in `report.json`.");
        }
        else
        {
            builder.AppendLine("- Telemetry streams were not truncated.");
        }

        foreach (var retained in telemetry.RetainedTests)
        {
            var name = GetString(retained, "testName");
            var result = GetString(retained, "result");
            if (name.Length > 0 || result.Length > 0)
            {
                builder.AppendLine($"- Retained: `{EscapeInline(name)}` ({EscapeCell(result)}).");
            }
        }
        if (telemetry.Errors.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("### Telemetry report errors");
            foreach (var error in telemetry.Errors)
            {
                builder.AppendLine($"- {EscapeCell(error)}");
            }
        }
        builder.AppendLine();
    }

    private static string BuildTimingsCsv(
        IReadOnlyList<TestPreparationTiming> preparationTimings,
        TestRunAggregate? aggregate)
    {
        var rows = new List<string> { string.Join(",", TimingHeaders.Select(QuoteCsv)) };
        foreach (var timing in preparationTimings)
        {
            rows.Add(CsvRow("Preparation", string.Empty, string.Empty, string.Empty, string.Join(", ", timing.TargetKeys),
                timing.Operation, "Completed", timing.DurationMs.ToString("0.###", CultureInfo.InvariantCulture)));
        }
        foreach (var attempt in aggregate?.Attempts ?? [])
        {
            rows.Add(CsvRow("Target", attempt.Iteration.ToString(CultureInfo.InvariantCulture), attempt.Target.Suite,
                attempt.Target.Framework, attempt.Target.Key, string.Empty, OutcomeForReport(attempt.Outcome),
                Math.Round(attempt.Duration.TotalMilliseconds, 3).ToString("0.###", CultureInfo.InvariantCulture)));
            foreach (var test in attempt.Tests)
            {
                rows.Add(CsvRow("Test", attempt.Iteration.ToString(CultureInfo.InvariantCulture), attempt.Target.Suite,
                    attempt.Target.Framework, attempt.Target.Key, test.TestName, OutcomeForReport(test.Outcome),
                    Math.Round(test.Duration.TotalMilliseconds, 3).ToString("0.###", CultureInfo.InvariantCulture)));
            }
        }
        return string.Join("\n", rows) + "\n";
    }

    private static string CsvRow(params string[] values)
        => string.Join(",", values.Select(QuoteCsv));

    private static string QuoteCsv(string value)
        => $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    private static int CountCompletedIterations(TestExecutorRequest request, IReadOnlyList<TestExecutionAttempt> attempts)
    {
        var completed = 0;
        for (var iteration = 1; iteration <= request.Count; iteration++)
        {
            var current = attempts.Where(attempt => attempt.Iteration == iteration).ToArray();
            if (current.Length == request.Targets.Count && current.All(static attempt => attempt.Completed))
            {
                completed++;
            }
        }
        return completed;
    }

    private static List<(string Target, int Iteration)> GetUnscheduledTargets(TestExecutorRequest request, TestRunAggregate? aggregate)
    {
        var scheduled = aggregate?.Attempts
            .Select(static attempt => (attempt.Target.Key, attempt.Iteration))
            .ToHashSet() ?? new HashSet<(string Key, int Iteration)>();
        return request.Targets
            .SelectMany(target => Enumerable.Range(1, request.Count)
                .Where(iteration => !scheduled.Contains((target.Key, iteration)))
                .Select(iteration => (target.Key, iteration)))
            .ToList();
    }

    private static string GetToolVersion(RunManifest manifest, string key)
        => manifest.ToolVersions.TryGetValue(key, out var version) ? version : string.Empty;

    private static long GetInt64(JsonElement document, string propertyName)
        => document.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result)
            ? result
            : 0;

    private static string GetString(JsonElement document, string propertyName)
        => document.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool HasTruncatedStream(JsonElement streams)
    {
        foreach (var stream in streams.EnumerateObject())
        {
            if (stream.Value.ValueKind == JsonValueKind.Object &&
                stream.Value.TryGetProperty("truncated", out var truncated) &&
                truncated.ValueKind == JsonValueKind.True)
            {
                return true;
            }
        }
        return false;
    }

    private static string TestClassification(TestCaseAggregate test)
        => test.Stability switch
        {
            TestStability.AlwaysPasses when test.ExpectedAttemptCount == 1 => "Passed",
            TestStability.AlwaysPasses => "Always passes",
            TestStability.AlwaysFails when test.ExpectedAttemptCount == 1 => "Failed",
            TestStability.AlwaysFails => "Always fails",
            TestStability.IntermittentFailure => "Intermittent failure",
            TestStability.Skipped => "Skipped",
            _ => "Incomplete"
        };

    private static string OutcomeForReport(TestExecutionOutcome outcome)
        => outcome switch
        {
            TestExecutionOutcome.Passed => "Passed",
            TestExecutionOutcome.AssertionFailure => "Failed",
            TestExecutionOutcome.DiscoveryFailure => "Incomplete",
            TestExecutionOutcome.Crashed => "Crashed",
            TestExecutionOutcome.Hung => "Hung",
            TestExecutionOutcome.Cancelled => "Cancelled",
            TestExecutionOutcome.Incomplete => "Incomplete",
            _ => "InfrastructureError"
        };

    private static string OutcomeForReport(TestCaseOutcome outcome)
        => outcome switch
        {
            TestCaseOutcome.NotRunnable or TestCaseOutcome.Skipped => "Skipped",
            _ => outcome.ToString()
        };

    private static string GetRelativePath(string runDirectory, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(runDirectory), Path.GetFullPath(path)).Replace('\\', '/');
        return relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) ? Path.GetFileName(path) : relative;
    }

    private static double Percentile(IReadOnlyList<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
        {
            return 0;
        }
        var index = Math.Clamp((int)Math.Ceiling(sortedValues.Count * percentile) - 1, 0, sortedValues.Count - 1);
        return sortedValues[index];
    }

    private static string FormatDuration(double milliseconds)
        => milliseconds < 1000
            ? $"{milliseconds.ToString("0.###", CultureInfo.InvariantCulture)} ms"
            : $"{(milliseconds / 1000).ToString("0.###", CultureInfo.InvariantCulture)} s";

    private static string EscapeCell(string? value)
        => (value ?? string.Empty).Replace("|", "\\|", StringComparison.Ordinal)
            .Replace('\r', ' ').Replace('\n', ' ');

    private static string EscapeInline(string? value)
        => EscapeCell(value).Replace("`", "\\`", StringComparison.Ordinal);
}
