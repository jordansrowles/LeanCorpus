using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkWorkflowContractTests
{
    [Fact]
    public void WorkflowUsesManagedConfigurationAndFixedBudgetedStages()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        string path = Path.Combine(checkout.RepositoryRoot, ".github", "workflows", "benchmark.yml");
        string workflow = File.ReadAllText(path);

        Assert.Contains("config validate", workflow, StringComparison.Ordinal);
        Assert.Contains("Plan bounded case shards", workflow, StringComparison.Ordinal);
        Assert.Contains("Build benchmark host", workflow, StringComparison.Ordinal);
        Assert.Contains("Smoke one BDN case", workflow, StringComparison.Ordinal);
        Assert.Contains("Shard 1 - collapse top-score", workflow, StringComparison.Ordinal);
        Assert.Contains("Shard 2 - facet page", workflow, StringComparison.Ordinal);
        Assert.Contains("Shard 3 - MLT prebuilt search", workflow, StringComparison.Ordinal);
        Assert.Contains("Shard 4 - MLT cold extraction", workflow, StringComparison.Ordinal);
        Assert.Contains("Collect run evidence", workflow, StringComparison.Ordinal);
        Assert.Contains("Write qualification summary", workflow, StringComparison.Ordinal);
        Assert.Contains("benchmark-qualification-summary.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("--max-cases 2", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Import-PowerShellDataFile", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("benchmark-suites.psd1", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--strat exhaustive", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--strat stress", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkflowShardCasesExistInTheManagedCaseManifest()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        string workflow = File.ReadAllText(Path.Combine(checkout.RepositoryRoot, ".github", "workflows", "benchmark.yml"));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(checkout.RepositoryRoot, ".config", "devops", "benchmark-cases.json")));
        JsonElement families = manifest.RootElement.GetProperty("caseFamilies");
        string[] caseIds =
        [
            "collapse-complete-leancorpus", "collapse-complete-lucenenet",
            "facet-offset-page-leancorpus", "facet-offset-page-lucenenet",
            "mlt-bval03-leancorpus-fixtureprebuiltsearch", "mlt-bval03-lucenenet-fixtureprebuiltsearch",
            "mlt-bval03-leancorpus-fixturecoldextraction", "mlt-bval03-lucenenet-fixturecoldextraction"
        ];

        foreach (string caseId in caseIds)
        {
            Assert.Contains(caseId, workflow, StringComparison.Ordinal);
            Assert.Contains(families.EnumerateArray(), item => item.GetProperty("id").GetString() == caseId);
        }

        Assert.Contains("leancorpus-search-v1", workflow, StringComparison.Ordinal);
        Assert.Contains("bval-03-morelikethis-cache-boundaries-v1", workflow, StringComparison.Ordinal);
        AssertPair("collapse-complete-leancorpus", "collapse-complete-lucenenet", "collapse-top-score-25-20k");
        AssertPair("facet-offset-page-leancorpus", "facet-offset-page-lucenenet", "facet-count-desc-offset-100-page-10-20k");
        AssertPair("mlt-bval03-leancorpus-fixtureprebuiltsearch", "mlt-bval03-lucenenet-fixtureprebuiltsearch", "bval03-prebuiltquerysearch");
        AssertPair("mlt-bval03-leancorpus-fixturecoldextraction", "mlt-bval03-lucenenet-fixturecoldextraction", "bval03-coldextractquerysearch");

        void AssertPair(string leftId, string rightId, string expectedGroup)
        {
            string? leftGroup = families.EnumerateArray().Single(item => item.GetProperty("id").GetString() == leftId)
                .GetProperty("comparisonGroup").GetString();
            string? rightGroup = families.EnumerateArray().Single(item => item.GetProperty("id").GetString() == rightId)
                .GetProperty("comparisonGroup").GetString();
            Assert.Equal(expectedGroup, leftGroup);
            Assert.Equal(expectedGroup, rightGroup);
        }
    }

    [Fact]
    public void QualificationSummaryFailsClosedForMissingAndCancelledShards()
    {
        SummaryResult complete = RunSummary(planOnly: false, reportCount: 5,
            new Dictionary<string, string?> { ["SHARD1_EXIT"] = "0", ["SHARD2_EXIT"] = "0", ["SHARD3_EXIT"] = "0", ["SHARD4_EXIT"] = "0" });
        SummaryResult missing = RunSummary(planOnly: false, reportCount: 5,
            new Dictionary<string, string?> { ["SHARD1_EXIT"] = "0", ["SHARD2_EXIT"] = "0", ["SHARD4_EXIT"] = "0" });
        SummaryResult cancelled = RunSummary(planOnly: false, reportCount: 5,
            new Dictionary<string, string?> { ["SHARD1_EXIT"] = "0", ["SHARD2_EXIT"] = "0", ["SHARD3_EXIT"] = "130", ["SHARD4_EXIT"] = "0" });
        SummaryResult missingReport = RunSummary(planOnly: false, reportCount: 4,
            new Dictionary<string, string?> { ["SHARD1_EXIT"] = "0", ["SHARD2_EXIT"] = "0", ["SHARD3_EXIT"] = "0", ["SHARD4_EXIT"] = "0" });
        SummaryResult planOnly = RunSummary(planOnly: true, reportCount: 0, new Dictionary<string, string?>());

        Assert.Equal(0, complete.ExitCode);
        Assert.Empty(complete.FailureReasons);
        Assert.NotEqual(0, missing.ExitCode);
        Assert.Contains(missing.FailedShards, value => value.Contains("SHARD3_EXIT (missing)", StringComparison.Ordinal));
        Assert.NotEqual(0, cancelled.ExitCode);
        Assert.Contains(cancelled.FailedShards, value => value.Contains("SHARD3_EXIT (130)", StringComparison.Ordinal));
        Assert.NotEqual(0, missingReport.ExitCode);
        Assert.Contains(missingReport.FailureReasons, value => value.Contains("Expected 5 run reports", StringComparison.Ordinal));
        Assert.Equal(0, planOnly.ExitCode);
        Assert.Empty(planOnly.FailedShards);
    }

    private static SummaryResult RunSummary(bool planOnly, int reportCount, IReadOnlyDictionary<string, string?> shardExits)
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        string script = Path.Combine(checkout.RepositoryRoot, "scripts", "devops", "validation", "benchmark-qualification-summary.ps1");
        string directory = Path.Combine(Path.GetTempPath(), "benchmark-summary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string reportsPath = Path.Combine(directory, "collected-run-reports.json");
        string summaryPath = Path.Combine(directory, "qualification-summary.json");
        string stepSummaryPath = Path.Combine(directory, "step-summary.md");
        try
        {
            var reports = Enumerable.Range(0, reportCount).Select(index => new
            {
                path = $"run-{index}.json",
                report = new
                {
                    runId = Guid.NewGuid(),
                    status = "Passed",
                    execution = new { classification = "Completed" },
                    comparisonAssessment = new
                    {
                        qualificationState = "Qualified",
                        eligibleForRatio = true,
                        reasonCodes = Array.Empty<string>()
                    }
                }
            });
            File.WriteAllText(reportsPath, JsonSerializer.Serialize(reports));

            var start = new ProcessStartInfo("pwsh")
            {
                WorkingDirectory = checkout.RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(script);
            start.ArgumentList.Add("-CollectedReportsPath");
            start.ArgumentList.Add(reportsPath);
            start.ArgumentList.Add("-OutputPath");
            start.ArgumentList.Add(summaryPath);
            start.ArgumentList.Add("-PlanOnly");
            start.ArgumentList.Add(planOnly ? "true" : "false");
            start.Environment["GITHUB_STEP_SUMMARY"] = stepSummaryPath;
            foreach (string name in new[] { "SHARD1_EXIT", "SHARD2_EXIT", "SHARD3_EXIT", "SHARD4_EXIT" })
                start.Environment.Remove(name);
            foreach ((string name, string? value) in shardExits)
            {
                if (value is not null)
                    start.Environment[name] = value;
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException("PowerShell did not start.");
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000))
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
                throw new TimeoutException("The benchmark qualification summary script did not finish within 30 seconds.");
            }
            string standardOutput = standardOutputTask.GetAwaiter().GetResult();
            string standardError = standardErrorTask.GetAwaiter().GetResult();
            Assert.True(File.Exists(summaryPath), $"PowerShell failed to write its summary. stdout: {standardOutput}; stderr: {standardError}");
            using JsonDocument summary = JsonDocument.Parse(File.ReadAllBytes(summaryPath));
            JsonElement root = summary.RootElement;
            return new SummaryResult(process.ExitCode,
                root.GetProperty("failedShards").EnumerateArray().Select(static value => value.GetString()!).ToArray(),
                root.GetProperty("failureReasons").EnumerateArray().Select(static value => value.GetString()!).ToArray());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record SummaryResult(int ExitCode, IReadOnlyList<string> FailedShards, IReadOnlyList<string> FailureReasons);
}
