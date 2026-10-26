using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Benchmarks;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkQualificationSummaryBuilderTests
{
    private const string SourceCommit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public void BuildsDeterministicQualifiedSummaryForThePlannedRunCount()
    {
        string reports = CreateReports(5, SourceCommit);
        var exits = Enumerable.Range(1, 4).ToDictionary(index => $"SHARD{index}_EXIT", _ => (string?)"0");

        BenchmarkQualificationSummaryDocument first = BenchmarkQualificationSummaryBuilder.Build(
            reports, SourceCommit, planOnly: false, expectedRunCount: 5, exits);
        BenchmarkQualificationSummaryDocument second = BenchmarkQualificationSummaryBuilder.Build(
            reports, SourceCommit, planOnly: false, expectedRunCount: 5, exits);

        Assert.Empty(first.FailureReasons);
        Assert.Empty(first.FailedShards);
        Assert.Equal(5, first.ExpectedRunCount);
        Assert.Equal(5, first.RunCount);
        Assert.Equal(5, first.QualifiedPairCount);
        Assert.False(first.ComparativeValuesPublished);
        Assert.Contains($"Source commit: `{SourceCommit}`", BenchmarkQualificationSummaryBuilder.CreateStepSummary(first), StringComparison.Ordinal);
        Assert.Contains("Comparative values remain unpublished", BenchmarkQualificationSummaryBuilder.CreateStepSummary(first), StringComparison.Ordinal);
        Assert.Equal(JsonSerializer.Serialize(first, BenchmarkQualificationSummaryBuilder.JsonOptions),
            JsonSerializer.Serialize(second, BenchmarkQualificationSummaryBuilder.JsonOptions));
    }

    [Fact]
    public void FailsClosedForMissingReportsAndMissingOrCancelledShardExits()
    {
        string reports = CreateReports(4, SourceCommit);
        var exits = new Dictionary<string, string?>
        {
            ["SHARD1_EXIT"] = "0",
            ["SHARD2_EXIT"] = "0",
            ["SHARD4_EXIT"] = "130"
        };

        BenchmarkQualificationSummaryDocument summary = BenchmarkQualificationSummaryBuilder.Build(
            reports, SourceCommit, planOnly: false, expectedRunCount: 5, exits);

        Assert.Contains(summary.FailedShards, value => value == "SHARD3_EXIT (missing)");
        Assert.Contains(summary.FailedShards, value => value == "SHARD4_EXIT (130)");
        Assert.Contains(summary.FailureReasons, value => value == "Expected 5 run reports but collected 4.");
    }

    [Fact]
    public void RejectsRunReportsFromAnotherSourceCommitAndIncompleteRuns()
    {
        string reports = CreateReports(1, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", status: "Failed", classification: "Aborted");

        BenchmarkQualificationSummaryDocument summary = BenchmarkQualificationSummaryBuilder.Build(
            reports, SourceCommit, planOnly: true, expectedRunCount: 0, new Dictionary<string, string?>());

        Assert.Contains(summary.FailureReasons, value => value == "One or more benchmark runs did not complete successfully.");
        Assert.Contains(summary.FailureReasons, value => value == "One or more benchmark run reports do not match the checked-out source commit.");
        Assert.Equal(0, summary.ExpectedRunCount);
        Assert.Empty(summary.FailedShards);
    }

    [Fact]
    public void ReportsInvalidJsonAsQualificationFailure()
    {
        BenchmarkQualificationSummaryDocument summary = BenchmarkQualificationSummaryBuilder.Build(
            "{ invalid", SourceCommit, planOnly: false, expectedRunCount: 1, new Dictionary<string, string?>());

        Assert.Contains(summary.FailureReasons, value => value == "Collected run reports are not valid JSON.");
        Assert.Equal(0, summary.RunCount);
    }

    private static string CreateReports(int count, string sourceCommit, string status = "Passed", string classification = "Completed")
        => JsonSerializer.Serialize(Enumerable.Range(0, count).Select(index => new
        {
            path = $"artifacts/benchmark/runs/run-{index}/run-report.json",
            report = new
            {
                runId = $"run-{index}",
                status,
                source = new { commit = sourceCommit },
                execution = new { classification },
                comparisonAssessment = new
                {
                    qualificationState = "Qualified",
                    eligibleForRatio = true,
                    reasonCodes = new[] { "Ready" }
                }
            }
        }));
}
