using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class AffectedBenchmarkPlannerTests
{
    [Fact]
    public void SelectsAndDeduplicatesClassesForDirtyCorePaths()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var selection = AffectedBenchmarkPlanner.Create(configuration,
            ["src/core/Rowles.LeanCorpus/Search/Searcher/IndexSearcher.cs"], null, null);

        Assert.Contains("Search", selection.MatchedGroups);
        Assert.Contains("TermCaches", selection.MatchedGroups);
        var classes = selection.BenchmarksByProject["core"];
        Assert.Contains("TermQueryBenchmarks", classes);
        Assert.Contains("QualifiedTermCacheBenchmarks", classes);
        Assert.Equal(classes.Count, classes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void AppliesAreaAndGroupFiltersToAffectedSelection()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var selection = AffectedBenchmarkPlanner.Create(configuration,
            ["src/core/Rowles.Text/Analysis/Filters/CachingTokenFilter.cs"], "Filters", "CachingGraphEdges");

        Assert.Equal(["CachingGraphEdges"], selection.MatchedGroups);
        Assert.Equal(["CachingGraphEdgeBenchmarks"], selection.BenchmarksByProject["text"]);
    }

    [Fact]
    public void RefusesToRunWhenNoConfiguredGroupMatchesDirtyPaths()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var exception = Assert.Throws<InvalidOperationException>(() => AffectedBenchmarkPlanner.Create(
            configuration, ["docs/content/readme.md"], null, null));

        Assert.Contains("Refusing to run zero affected benchmarks", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnknownAreaAndGroupNames()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var areaException = Assert.Throws<InvalidOperationException>(() => AffectedBenchmarkPlanner.Create(
            configuration, ["src/core/Rowles.LeanCorpus/Search/Searcher/IndexSearcher.cs"], "missing", null));
        var groupException = Assert.Throws<InvalidOperationException>(() => AffectedBenchmarkPlanner.Create(
            configuration, ["src/core/Rowles.LeanCorpus/Search/Searcher/IndexSearcher.cs"], null, "missing"));

        Assert.Contains("Unknown benchmark area", areaException.Message, StringComparison.Ordinal);
        Assert.Contains("Unknown benchmark group", groupException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreatesClassScopedInvocationsWithSourceSelectionProvenance()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = new BenchmarkCommandOptions("affected", "fast", "net10.0", 0, "synthetic", null,
            null, null, null, null, null, false, false, false, false, 200, true, false, []);

        var plan = BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot,
            ["src/core/Rowles.LeanCorpus/Search/Searcher/IndexSearcher.cs"]);

        Assert.Equal("affected", plan.Suite);
        Assert.Equal(["src/core/Rowles.LeanCorpus/Search/Searcher/IndexSearcher.cs"], plan.AffectedPaths);
        Assert.NotEmpty(plan.AffectedGroups);
        Assert.All(plan.Invocations, invocation => Assert.Contains("--filter", invocation.Arguments));
        Assert.All(plan.Invocations, invocation => Assert.NotNull(invocation.BenchmarkClass));
    }

    [Fact]
    public void AffectedSelectionUsesConfiguredDefaultDatasetWhenDatasetIsOmitted()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load() with { DefaultBenchmarkDataset = "fixture" };
        var options = new BenchmarkCommandOptions("affected", "fast", "net10.0", 0, string.Empty, null,
            null, null, null, null, null, false, false, false, false, 200, true, false, []);

        var plan = BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot,
            ["src/core/Rowles.LeanCorpus/Search/Searcher/IndexSearcher.cs"]);

        Assert.Equal("fixture", plan.Dataset);
        Assert.NotEmpty(plan.Invocations);
    }

    [Fact]
    public void RejectsCustomFilterForAffectedClassRuns()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = new BenchmarkCommandOptions("affected", "fast", "net10.0", 0, "synthetic", null,
            null, null, null, null, null, false, false, false, false, 200, true, false, ["--filter", "*Query*"]);

        var exception = Assert.Throws<InvalidOperationException>(() => BenchmarkCommandPlanner.Create(options, configuration,
            checkout.RepositoryRoot, ["src/core/Rowles.LeanCorpus/Search/Searcher/IndexSearcher.cs"]));

        Assert.Contains("cannot be combined with affected class selection", exception.Message, StringComparison.Ordinal);
    }
}
