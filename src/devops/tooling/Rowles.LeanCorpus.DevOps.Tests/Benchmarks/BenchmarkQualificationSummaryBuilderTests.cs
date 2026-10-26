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
    public void PreservesTheSchemaV1NestedExecutionObject()
    {
        var summary = BenchmarkQualificationSummaryBuilder.Build(
            CreateReports(1, SourceCommit), SourceCommit, planOnly: false, expectedRunCount: 1,
            new Dictionary<string, string?>());

        using JsonDocument output = JsonDocument.Parse(JsonSerializer.Serialize(summary, BenchmarkQualificationSummaryBuilder.JsonOptions));
        JsonElement root = output.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.False(root.GetProperty("planOnly").GetBoolean());
        Assert.Equal(SourceCommit, root.GetProperty("sourceCommit").GetString());
        JsonElement run = Assert.Single(root.GetProperty("runs").EnumerateArray());
        Assert.Equal("run-0", run.GetProperty("runId").GetString());
        Assert.Equal("Passed", run.GetProperty("status").GetString());
        Assert.Equal("Completed", run.GetProperty("execution").GetProperty("classification").GetString());
        Assert.False(run.TryGetProperty("executionClassification", out _));
    }

    [Fact]
    public void FailsClosedWhenTheExecutionObjectIsMissing()
    {
        string reports = CreateReports(1, SourceCommit).Replace(
            "\"execution\":{\"classification\":\"Completed\"},", string.Empty, StringComparison.Ordinal);

        BenchmarkQualificationSummaryDocument summary = BenchmarkQualificationSummaryBuilder.Build(
            reports, SourceCommit, planOnly: true, expectedRunCount: 0, new Dictionary<string, string?>());

        Assert.Contains(summary.FailureReasonCodes, code => code == "RunExecutionInvalid");
        Assert.Contains(summary.FailureReasonCodes, code => code == "BenchmarkRunIncomplete");
        Assert.Single(summary.Runs);
        Assert.NotEmpty(summary.FailureReasons);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"Completed\"")]
    [InlineData("{}")]
    [InlineData("[]")]
    public void FailsClosedWhenExecutionClassificationIsMalformed(string executionJson)
    {
        string reports = CreateReports(1, SourceCommit).Replace(
            "\"execution\":{\"classification\":\"Completed\"}",
            $"\"execution\":{executionJson}", StringComparison.Ordinal);

        BenchmarkQualificationSummaryDocument summary = BenchmarkQualificationSummaryBuilder.Build(
            reports, SourceCommit, planOnly: true, expectedRunCount: 0, new Dictionary<string, string?>());

        Assert.Contains(summary.FailureReasonCodes, code => code == "RunExecutionInvalid");
        Assert.Contains(summary.FailureReasonCodes, code => code == "BenchmarkRunIncomplete");
        Assert.Single(summary.Runs);
        Assert.NotEmpty(summary.FailureReasons);
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
