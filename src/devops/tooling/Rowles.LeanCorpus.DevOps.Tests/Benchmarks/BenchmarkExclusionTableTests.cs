using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkExclusionTableTests
{
    [Fact]
    public void AllSelectorListsFuzzyMultisegmentAsAnExcludedWorkloadWithItsClass()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var excluded = BenchmarkCommandPlanner.ResolveExcludedWorkloads(configuration, "all", ["compression", "core", "text"]);

        var fuzzy = Assert.Single(excluded, static workload => workload.SuiteId == "fuzzy-multisegment");
        Assert.Equal("explicit-only", fuzzy.Reason);
        Assert.Equal(["FuzzyMultiSegmentBenchmarks"], fuzzy.BenchmarkClasses);
    }

    [Fact]
    public void EveryCoreWorkloadSuiteNamesItsBenchmarkClasses()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var unresolved = configuration.BenchmarkSuites
            .Where(static pair => pair.Value.Project == "core" && !pair.Value.IsSelector && pair.Value.RegisteredClasses.IsDefaultOrEmpty)
            .Select(static pair => pair.Key)
            .ToArray();

        Assert.Empty(unresolved);
    }
}
