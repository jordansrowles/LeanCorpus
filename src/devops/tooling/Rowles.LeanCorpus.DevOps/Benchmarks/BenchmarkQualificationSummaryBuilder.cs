using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal sealed record BenchmarkQualificationRunSummary(
    string? RunId,
    string? Status,
    string? ExecutionClassification,
    string? QualificationState,
    bool EligibleForRatio,
    IReadOnlyList<string> ReasonCodes,
    string? ReportPath,
    string? SourceCommit);

internal sealed record BenchmarkQualificationSummaryDocument(
    int SchemaVersion,
    bool PlanOnly,
    string SourceCommit,
    int ExpectedRunCount,
    int RunCount,
    int QualifiedPairCount,
    IReadOnlyList<string> FailedShards,
    IReadOnlyList<string> FailureReasons,
    IReadOnlyList<string> FailureReasonCodes,
    IReadOnlyList<BenchmarkQualificationRunSummary> Runs,
    bool ComparativeValuesPublished);

internal static class BenchmarkQualificationSummaryBuilder
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static BenchmarkQualificationSummaryDocument Build(
        string? collectedReportsJson,
        string sourceCommit,
        bool planOnly,
        int expectedRunCount,
        IReadOnlyDictionary<string, string?> shardExitCodes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCommit);
        ArgumentNullException.ThrowIfNull(shardExitCodes);
        if (!planOnly && expectedRunCount < 1)
            throw new ArgumentOutOfRangeException(nameof(expectedRunCount), "The expected run count must include at least the smoke run.");

        var expected = planOnly ? 0 : expectedRunCount;
        var failures = new List<string>();
        var failureCodes = new SortedSet<string>(StringComparer.Ordinal);
        var failedShards = new List<string>();
        var runs = new List<BenchmarkQualificationRunSummary>();

        void Fail(string code, string message)
        {
            failureCodes.Add(code);
            if (!failures.Contains(message, StringComparer.Ordinal)) failures.Add(message);
        }

        if (collectedReportsJson is null)
        {
            Fail("CollectedReportsMissing", "Collected run reports file is missing.");
        }
        else
        {
            try
            {
                using JsonDocument collected = JsonDocument.Parse(collectedReportsJson);
                if (collected.RootElement.ValueKind != JsonValueKind.Array)
                {
                    Fail("CollectedReportsInvalid", "Collected run reports must be a JSON array.");
                }
                else
                {
                    foreach (JsonElement item in collected.RootElement.EnumerateArray())
                    {
                        if (!TryReadRun(item, out BenchmarkQualificationRunSummary? run))
                        {
                            Fail("RunReportInvalid", "One or more collected benchmark run reports are malformed.");
                            continue;
                        }
                        runs.Add(run!);
                    }
                }
            }
            catch (JsonException)
            {
                Fail("CollectedReportsInvalid", "Collected run reports are not valid JSON.");
            }
        }

        if (!planOnly && runs.Count != expected)
            Fail("ExpectedRunCountMismatch", $"Expected {expected} run reports but collected {runs.Count}.");

        if (!planOnly)
        {
            for (var shard = 1; shard < expected; shard++)
            {
                var name = $"SHARD{shard}_EXIT";
                if (!shardExitCodes.TryGetValue(name, out var rawExitCode) || string.IsNullOrWhiteSpace(rawExitCode))
                {
                    failedShards.Add($"{name} (missing)");
                    continue;
                }

                if (!int.TryParse(rawExitCode, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var exitCode))
                {
                    failedShards.Add($"{name} (invalid exit code)");
                }
                else if (exitCode != 0)
                {
                    failedShards.Add($"{name} ({exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                }
            }

            if (failedShards.Count > 0)
                Fail("BenchmarkShardFailed", "One or more fixed benchmark shards failed, were cancelled, or did not report an exit code.");
        }

        if (runs.Any(static run => run.Status != "Passed" || run.ExecutionClassification != "Completed"))
            Fail("BenchmarkRunIncomplete", "One or more benchmark runs did not complete successfully.");

        if (runs.Any(static run => string.IsNullOrWhiteSpace(run.SourceCommit)))
            Fail("RunSourceCommitMissing", "One or more benchmark run reports are missing source-commit identity.");
        if (runs.Any(run => !string.IsNullOrWhiteSpace(run.SourceCommit) &&
                            !string.Equals(run.SourceCommit, sourceCommit, StringComparison.OrdinalIgnoreCase)))
            Fail("RunSourceCommitMismatch", "One or more benchmark run reports do not match the checked-out source commit.");

        runs.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.ReportPath, right.ReportPath));

        return new BenchmarkQualificationSummaryDocument(
            SchemaVersion: 1,
            PlanOnly: planOnly,
            SourceCommit: sourceCommit.ToLowerInvariant(),
            ExpectedRunCount: expected,
            RunCount: runs.Count,
            QualifiedPairCount: runs.Count(static run => run.EligibleForRatio),
            FailedShards: failedShards,
            FailureReasons: failures,
            FailureReasonCodes: failureCodes.ToArray(),
            Runs: runs,
            ComparativeValuesPublished: false);
    }

    public static string CreateStepSummary(BenchmarkQualificationSummaryDocument summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var lines = new List<string>
        {
            "## Benchmark qualification summary",
            string.Empty,
            $"Source commit: `{summary.SourceCommit}`",
            $"Runs collected: {summary.RunCount} of {summary.ExpectedRunCount}. Qualified pairs: {summary.QualifiedPairCount}.",
            "Comparative values remain unpublished by this bounded workflow."
        };
        if (summary.FailedShards.Count > 0)
            lines.Add($"Failed shards: {string.Join(", ", summary.FailedShards)}.");
        if (summary.FailureReasons.Count > 0)
            lines.Add($"Failure reasons: {string.Join("; ", summary.FailureReasons)}.");
        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines);
    }

    private static bool TryReadRun(JsonElement item, out BenchmarkQualificationRunSummary? run)
    {
        run = null;
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("report", out JsonElement report) ||
            report.ValueKind != JsonValueKind.Object)
            return false;

        JsonElement execution = GetObject(report, "execution");
        JsonElement comparison = GetObject(report, "comparisonAssessment");
        JsonElement source = GetObject(report, "source");
        var eligible = comparison.TryGetProperty("eligibleForRatio", out JsonElement eligibleValue) &&
                       eligibleValue.ValueKind == JsonValueKind.True;

        run = new BenchmarkQualificationRunSummary(
            ReadScalar(report, "runId"),
            ReadScalar(report, "status"),
            ReadScalar(execution, "classification"),
            ReadScalar(comparison, "qualificationState"),
            eligible,
            ReadStringArray(comparison, "reasonCodes"),
            ReadScalar(item, "path"),
            ReadScalar(source, "commit"));
        return true;
    }

    private static JsonElement GetObject(JsonElement value, string propertyName)
        => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(propertyName, out JsonElement result) &&
           result.ValueKind == JsonValueKind.Object
            ? result
            : default;

    private static string? ReadScalar(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out JsonElement property)) return null;
        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement value, string propertyName)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(propertyName, out JsonElement property) ||
            property.ValueKind != JsonValueKind.Array)
            return [];
        return property.EnumerateArray().Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()!).ToArray();
    }
}
