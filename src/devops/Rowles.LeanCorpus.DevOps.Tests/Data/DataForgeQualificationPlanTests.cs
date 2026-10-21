using Rowles.LeanCorpus.DevOps.Data;

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

        Assert.Equal(23, plan.Steps.Count);
        Assert.Equal(1, plan.Steps.Count(step => step.Group == "build"));
        Assert.Equal(1, plan.Steps.Count(step => step.Group == "wikipedia-reference"));
        Assert.Equal(4, plan.Steps.Count(step => step.Group == "synthetic-tests"));
        Assert.Equal(5, plan.Steps.Count(step => step.Group == "synthetic-benchmarks"));
        Assert.Equal(6, plan.Steps.Count(step => step.Group == "wikipedia-smoke"));
        Assert.Equal(6, plan.Steps.Count(step => step.Group == "wikipedia-benchmarks"));
        Assert.All(plan.Steps, step => Assert.DoesNotContain("pwsh", step.Command, StringComparison.OrdinalIgnoreCase));
    }
}
