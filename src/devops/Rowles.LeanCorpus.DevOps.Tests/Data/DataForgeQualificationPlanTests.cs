using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Data;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Data;

public sealed class DataForgeQualificationPlanTests
{
    [Fact]
    public void ParserAcceptsLegacyAliasesAndResolvesRelativeReferencePath()
    {
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus qualification repository");

        var parsed = DataForgeQualificationArgumentParser.Parse(
            ["-Dry", "-ReferencePath", Path.Combine("datasets", "wiki")], repositoryRoot);

        Assert.True(parsed.Success, parsed.Failure);
        Assert.True(parsed.Options!.Dry);
        Assert.Equal(Path.GetFullPath(Path.Combine(repositoryRoot, "datasets", "wiki")), parsed.Options.ReferencePath);
    }

    [Fact]
    public void ParserRejectsUnknownOptionsAndPositionals()
    {
        var repositoryRoot = Path.GetTempPath();

        var unknown = DataForgeQualificationArgumentParser.Parse(["--force"], repositoryRoot);
        var positional = DataForgeQualificationArgumentParser.Parse(["unexpected"], repositoryRoot);

        Assert.False(unknown.Success);
        Assert.False(positional.Success);
        Assert.Contains("Usage:", unknown.Failure, StringComparison.Ordinal);
        Assert.Contains("Usage:", positional.Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanContainsTheExpectedQualificationGroupsWithoutPowerShellReentry()
    {
        var plan = DataForgeQualificationPlan.Create(
            new DataForgeQualificationOptions("/tmp/frozen wikipedia", Dry: true));

        Assert.Equal(24, plan.Steps.Count);
        Assert.Equal(1, plan.Steps.Count(step => step.Group == "build"));
        Assert.Equal(1, plan.Steps.Count(step => step.Group == "profiles"));
        Assert.Equal(1, plan.Steps.Count(step => step.Group == "wikipedia-reference"));
        Assert.Equal(4, plan.Steps.Count(step => step.Group == "synthetic-tests"));
        Assert.Equal(5, plan.Steps.Count(step => step.Group == "synthetic-benchmarks"));
        Assert.Equal(6, plan.Steps.Count(step => step.Group == "wikipedia-smoke"));
        Assert.Equal(6, plan.Steps.Count(step => step.Group == "wikipedia-benchmarks"));
        Assert.All(plan.Steps, step => Assert.DoesNotContain("pwsh", step.Command, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlanSelectorsValidateAgainstTheCurrentTestBenchmarkAndDatasetRegistries()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var plan = DataForgeQualificationPlan.Create(new DataForgeQualificationOptions("/tmp/wikipedia", Dry: true));

        var failures = plan.Validate(configuration, checkout.RepositoryRoot);

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void PlanRejectsUnregisteredSuiteAndUnsupportedFrameworkBeforeExecution()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var plan = DataForgeQualificationPlan.Create(new DataForgeQualificationOptions("/tmp/wikipedia", Dry: true));
        var steps = plan.Steps.ToArray();
        var testIndex = Array.FindIndex(steps, static step => step.Kind == DataForgeQualificationStepKind.Test);
        var benchmarkIndex = Array.FindIndex(steps, static step => step.Kind == DataForgeQualificationStepKind.Benchmark);
        steps[testIndex] = steps[testIndex] with { Framework = "net9.0" };
        steps[benchmarkIndex] = steps[benchmarkIndex] with { Suite = "query-misspelled" };

        var failures = new DataForgeQualificationPlan(steps).Validate(configuration, checkout.RepositoryRoot);

        Assert.Contains(failures, failure => failure.Contains("net9.0", StringComparison.Ordinal));
        Assert.Contains(failures, failure => failure.Contains("query-misspelled", StringComparison.Ordinal));
    }
}
