using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Tests.Benchmarks;

public sealed class BenchmarkCommandPlannerTests
{
    [Fact]
    public void CreatesConfiguredCoreInvocationAndPreservesBenchmarkDotNetArguments()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = Options(arguments: ["--filter", "*TermQueryBenchmarks*", "--min-iteration-count", "2"]);

        var plan = BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot);

        var invocation = Assert.Single(plan.Invocations);
        Assert.Equal("core", invocation.ProjectKey);
        Assert.Equal("net10.0", plan.Framework);
        Assert.Equal(
            ["--suite", "query", "--doccount", "500", "--job", "dry", "--filter", "*TermQueryBenchmarks*", "--min-iteration-count", "2"],
            invocation.Arguments);
    }

    [Fact]
    public void CreatesTextProjectInvocationForMultilingualBenchmarks()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = Options(suite: "text", arguments: ["--filter", "*MultiLanguageAnalysisBenchmarks*"]);

        var plan = BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot);

        var invocation = Assert.Single(plan.Invocations);
        Assert.Equal("text", invocation.ProjectKey);
        Assert.Equal("src/devops/Rowles.Text.Benchmarks/Rowles.Text.Benchmarks.csproj", Path.GetRelativePath(checkout.RepositoryRoot, invocation.ProjectPath).Replace('\\', '/'));
        Assert.Equal(["--job", "dry", "--filter", "*MultiLanguageAnalysisBenchmarks*"], invocation.Arguments);
    }

    [Fact]
    public void RejectsCustomFilterWhenAreaSelectionAddsPerClassFilters()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = Options(suite: "core", area: "Search", arguments: ["--filter", "*Term*"]);

        var exception = Assert.Throws<InvalidOperationException>(() => BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot));

        Assert.Contains("cannot be combined with area or group selection", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsWikipediaForSuitesOutsideTheFrozenDatasetAllowList()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = Options(suite: "gutenberg-index", dataset: "wikipedia");

        var exception = Assert.Throws<InvalidOperationException>(() => BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot));

        Assert.Contains("not supported with the Wikipedia reference dataset", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsWikipediaDocumentCountAboveTheFrozenReferenceSize()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = Options(dataset: "wikipedia", documentCount: 20_001);

        var exception = Assert.Throws<InvalidOperationException>(() => BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot));

        Assert.Contains("20,000", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DryRunShowsBenchmarkChildBuildServersAreDisabled()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var paths = new ArtifactPaths(checkout.RepositoryRoot);
        var processRunner = new ProcessRunner();
        var progress = new List<string>();
        var service = new BenchmarkCommandService(
            paths,
            new ArtifactRunStore(paths, runCategory: "benchmark"),
            processRunner,
            new DotNetSdkResolver(processRunner),
            new GitRepositoryProvenanceReader(processRunner),
            progress.Add);

        var result = await service.RunAsync(new BenchmarkCommandRequest(
            checkout,
            Options(suite: "core", arguments: ["--filter", "*FuzzyQueryBenchmarks*"], dry: true)),
            TestContext.Current.CancellationToken);

        Assert.Equal(OutcomeKind.Success, result.CommandResult.Outcome);
        Assert.Contains("--disable-build-servers", Assert.Single(progress), StringComparison.Ordinal);
    }

    private static BenchmarkCommandOptions Options(
        string suite = "query",
        string strategy = "fast",
        string framework = "net10.0",
        string dataset = "synthetic",
        int documentCount = 0,
        string? area = null,
        IReadOnlyList<string>? arguments = null,
        bool dry = false)
        => new(
            suite,
            strategy,
            framework,
            DocumentCount: documentCount,
            dataset,
            ReferencePath: null,
            area,
            Group: null,
            SourceCommit: null,
            SourceRef: null,
            SourceManifest: null,
            Controlled: false,
            CorpusOnly: false,
            GcDump: false,
            PrepareData: false,
            BookCount: 200,
            Dry: dry,
            List: false,
            arguments ?? []);
}
