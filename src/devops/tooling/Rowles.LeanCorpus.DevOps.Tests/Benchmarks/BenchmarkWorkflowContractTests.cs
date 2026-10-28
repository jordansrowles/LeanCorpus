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
        Assert.Contains("'benchmark', 'qualification-summary'", workflow, StringComparison.Ordinal);
        Assert.Contains("EXPECTED_BENCHMARK_RUN_COUNT=$($shards.Count + 1)", workflow, StringComparison.Ordinal);
        Assert.Contains("--max-cases 2", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Import-PowerShellDataFile", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("benchmark-suites.psd1", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--strat exhaustive", workflow, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("--strat stress", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorkflowAlwaysWritesAndUploadsPlanOnlyOrNormalQualificationEvidence()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        string workflow = File.ReadAllText(Path.Combine(checkout.RepositoryRoot, ".github", "workflows", "benchmark.yml"));
        int summaryStart = workflow.IndexOf("- name: Write qualification summary", StringComparison.Ordinal);
        int summaryEnd = workflow.IndexOf("- name: Upload benchmark run evidence", summaryStart, StringComparison.Ordinal);
        Assert.True(summaryStart >= 0 && summaryEnd > summaryStart);
        string summaryStep = workflow[summaryStart..summaryEnd];

        Assert.Contains("if: always()", summaryStep, StringComparison.Ordinal);
        Assert.Contains("PLAN_ONLY: ${{ inputs.plan_only }}", summaryStep, StringComparison.Ordinal);
        Assert.Contains("if ($env:PLAN_ONLY -eq 'true')", summaryStep, StringComparison.Ordinal);
        Assert.Contains("$arguments += '--plan-only'", summaryStep, StringComparison.Ordinal);
        Assert.Contains("$arguments += @('--expected-run-count', $env:EXPECTED_BENCHMARK_RUN_COUNT)", summaryStep, StringComparison.Ordinal);
        Assert.Contains("exit $LASTEXITCODE", summaryStep, StringComparison.Ordinal);

        string uploadStep = workflow[summaryEnd..];
        Assert.Contains("if: always()", uploadStep, StringComparison.Ordinal);
        Assert.Contains("collected-run-reports.json", uploadStep, StringComparison.Ordinal);
        Assert.Contains("qualification-summary.json", uploadStep, StringComparison.Ordinal);
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

}
