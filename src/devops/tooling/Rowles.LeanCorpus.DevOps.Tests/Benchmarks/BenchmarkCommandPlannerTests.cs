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
    public void UsesConfiguredDefaultDatasetWhenDatasetIsOmitted()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load() with { DefaultBenchmarkDataset = "fixture" };

        var plan = BenchmarkCommandPlanner.Create(Options(dataset: string.Empty), configuration, checkout.RepositoryRoot);

        Assert.Equal("fixture", plan.Dataset);
    }

    [Fact]
    public void ResolvesStrategyBudgetWithIndependentBuildAndProjectDeadlines()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var exhaustive = BenchmarkCommandPlanner.Create(Options(suite: "core", strategy: "exhaustive"), configuration, checkout.RepositoryRoot);
        var quick = BenchmarkCommandPlanner.Create(Options(suite: "core", strategy: "quick-compare"), configuration, checkout.RepositoryRoot);

        Assert.Equal(new BenchmarkTimeoutBudget(1440, 30, 1440, "strategy"), exhaustive.TimeoutBudget);
        Assert.True(quick.TimeoutBudget.AggregateTimeoutMinutes < exhaustive.TimeoutBudget.AggregateTimeoutMinutes);
        Assert.Equal(30, quick.TimeoutBudget.BuildTimeoutMinutes);
    }

    [Fact]
    public void RoutesCapacityOneAsyncStressThroughItsIndependentBudgets()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var bounded = BenchmarkCommandPlanner.Create(
            Options(suite: "async-index-stress", strategy: "async-stress"), configuration, checkout.RepositoryRoot);
        var deep = BenchmarkCommandPlanner.Create(
            Options(suite: "async-index-stress", strategy: "async-stress-deep"), configuration, checkout.RepositoryRoot);

        var boundedInvocation = Assert.Single(bounded.Invocations);
        Assert.Equal("AsyncIndexingCapacityOneStressBenchmarks", boundedInvocation.BenchmarkClass);
        Assert.Equal(1_000, bounded.DocumentCount);
        Assert.Equal(new BenchmarkTimeoutBudget(30, 30, 30, "strategy"), bounded.TimeoutBudget);
        Assert.Contains("async-index-stress", boundedInvocation.Arguments);

        Assert.Equal(100_000, deep.DocumentCount);
        Assert.Equal(new BenchmarkTimeoutBudget(1_440, 30, 1_440, "strategy"), deep.TimeoutBudget);
    }

    [Fact]
    public void CommandLineTimeoutOverridesOnlyTheAggregateStrategyBudget()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var plan = BenchmarkCommandPlanner.Create(
            Options(suite: "core", strategy: "exhaustive") with { TimeoutMinutes = 90 },
            configuration,
            checkout.RepositoryRoot);

        Assert.Equal(new BenchmarkTimeoutBudget(90, 30, 1440, "command-line"), plan.TimeoutBudget);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(40_001)]
    public void RejectsInvalidCommandLineTimeout(int timeoutMinutes)
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var error = Assert.Throws<InvalidOperationException>(() => BenchmarkCommandPlanner.Create(
            Options(suite: "core") with { TimeoutMinutes = timeoutMinutes },
            configuration,
            checkout.RepositoryRoot));

        Assert.Contains("Timeout must be between 1 and 40000 minutes", error.Message, StringComparison.Ordinal);
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
        Assert.Equal("src/devops/benchmarks/Rowles.Text.Benchmarks/Rowles.Text.Benchmarks.csproj", Path.GetRelativePath(checkout.RepositoryRoot, invocation.ProjectPath).Replace('\\', '/'));
        Assert.Equal(["--job", "dry", "--filter", "*MultiLanguageAnalysisBenchmarks*"], invocation.Arguments);
    }

    [Fact]
    public void ResolvesConfiguredSuiteAliasesBeforeDispatch()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var plan = BenchmarkCommandPlanner.Create(Options(suite: "deletionqueue"), configuration, checkout.RepositoryRoot);

        Assert.Equal("deletion-queue", plan.Suite);
        var invocation = Assert.Single(plan.Invocations);
        Assert.Equal("core", invocation.ProjectKey);
        Assert.Contains("--suite", invocation.Arguments);
        Assert.Contains("deletion-queue", invocation.Arguments);
    }

    [Fact]
    public void RoutesConfiguredTextSuiteToItsRegisteredBenchmarkClass()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var plan = BenchmarkCommandPlanner.Create(Options(suite: "analysis-parity"), configuration, checkout.RepositoryRoot);

        var invocation = Assert.Single(plan.Invocations);
        Assert.Equal("text", invocation.ProjectKey);
        Assert.Equal("AnalyserParityBenchmarks", invocation.BenchmarkClass);
        Assert.Equal("src/devops/benchmarks/Rowles.Text.Benchmarks/Rowles.Text.Benchmarks.csproj",
            Path.GetRelativePath(checkout.RepositoryRoot, invocation.ProjectPath).Replace('\\', '/'));
        Assert.Contains("--filter", invocation.Arguments);
        Assert.Contains("*AnalyserParityBenchmarks*", invocation.Arguments);
    }

    [Theory]
    [InlineData("fuzzy", "FuzzyQueryBenchmarks")]
    [InlineData("fuzzy-parity", "FuzzyQueryBenchmarks")]
    [InlineData("fuzzy-cache", "FuzzyExpansionCacheBenchmarks")]
    public void RoutesFuzzyAliasesToDisjointBenchmarkClasses(string suite, string benchmarkClass)
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var plan = BenchmarkCommandPlanner.Create(Options(suite: suite), configuration, checkout.RepositoryRoot);

        var invocation = Assert.Single(plan.Invocations);
        Assert.Equal("core", invocation.ProjectKey);
        Assert.Equal(benchmarkClass, invocation.BenchmarkClass);
        Assert.Contains("--filter", invocation.Arguments);
        Assert.Contains($"*{benchmarkClass}*", invocation.Arguments);
    }

    [Theory]
    [InlineData("approximation")]
    [InlineData("flush-term-cardinality")]
    public void RoutesNewlyRegisteredCoreBenchmarkSuites(string suite)
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var plan = BenchmarkCommandPlanner.Create(Options(suite: suite), configuration, checkout.RepositoryRoot);

        var invocation = Assert.Single(plan.Invocations);
        Assert.Equal("core", invocation.ProjectKey);
        Assert.Null(invocation.BenchmarkClass);
        Assert.Equal(suite, plan.Suite);
        Assert.Contains("--suite", invocation.Arguments);
        Assert.Contains(suite, invocation.Arguments);
    }

    [Fact]
    public void RoutesDedicatedMultiSegmentFuzzyBenchmarkClass()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var plan = BenchmarkCommandPlanner.Create(
            Options(suite: "fuzzy-multisegment"), configuration, checkout.RepositoryRoot);

        var invocation = Assert.Single(plan.Invocations);
        Assert.Equal("core", invocation.ProjectKey);
        Assert.Equal("FuzzyMultiSegmentBenchmarks", invocation.BenchmarkClass);
        Assert.Contains("--filter", invocation.Arguments);
        Assert.Contains("*FuzzyMultiSegmentBenchmarks*", invocation.Arguments);
    }

    [Fact]
    public void IntersectsProfileCaseEngineAreaGroupAndFilterSelectorsWithinTheCaseBudget()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = Options(suite: "all", area: "Search", arguments: ["--filter", "*CollapseAndFacetBenchmarks*"]) with
        {
            Profile = "leancorpus-search-v1",
            Case = "collapse-complete-leancorpus,collapse-complete-lucenenet",
            Engine = "leancorpus,lucenenet",
            WorkloadId = "collapse-top-score-count-v1",
            Group = "collapse-top-score-25-20k",
            MaxCases = 2
        };

        var plan = BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot);

        Assert.Equal(["collapse-complete-leancorpus", "collapse-complete-lucenenet"], plan.SelectedCaseIds);
        Assert.Equal(2, plan.SelectedCaseCount);
        Assert.Equal(2, plan.CaseBudget);
        Assert.Equal(2, plan.Invocations.Count);
        Assert.All(plan.Invocations, invocation => Assert.Contains("--filter", invocation.Arguments));
        Assert.Contains(plan.Invocations, invocation => invocation.Arguments.Contains("*CollapseAndFacetBenchmarks.LeanCorpus_SearchWithCollapse"));
        Assert.Contains(plan.Invocations, invocation => invocation.Arguments.Contains("*CollapseAndFacetBenchmarks.LuceneNet_SearchWithCollapse"));
    }

    [Fact]
    public void AppliesManifestDocumentCountToTypedCaseInvocations()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = Options(suite: "core") with
        {
            Case = "collapse-complete-leancorpus",
            MaxCases = 1
        };

        var plan = BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot);

        var invocation = Assert.Single(plan.Invocations);
        var documentCountArgument = Array.IndexOf(invocation.Arguments.ToArray(), "--doccount");
        Assert.True(documentCountArgument >= 0);
        Assert.Equal("20000", invocation.Arguments[documentCountArgument + 1]);
        Assert.Equal(20_000, plan.DocumentCount);
        Assert.Equal(["20000"], invocation.ParameterValues!["DocumentCount"]);
    }

    [Fact]
    public void BoundedCaseBudgetCountsParameterCombinations()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = Options(suite: "core") with
        {
            Case = "flush-term-cardinality",
            MaxCases = 8
        };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot));

        Assert.Contains("contains 9 benchmark cases", exception.Message, StringComparison.Ordinal);
        Assert.Contains("--max-cases budget of 8", exception.Message, StringComparison.Ordinal);

        var exactBudgetPlan = BenchmarkCommandPlanner.Create(options with { MaxCases = 9 }, configuration, checkout.RepositoryRoot);

        Assert.Equal(9, exactBudgetPlan.SelectedCaseCount);
        Assert.Equal(9, exactBudgetPlan.CaseBudget);
        var invocation = Assert.Single(exactBudgetPlan.Invocations);
        Assert.Contains("all-with-explicit", invocation.Arguments);
        var classSelector = Array.IndexOf(invocation.Arguments.ToArray(), "--benchmark-class");
        Assert.True(classSelector >= 0);
        Assert.Equal("FlushTermCardinalityBenchmarks", invocation.Arguments[classSelector + 1]);
        Assert.Contains("*FlushTermCardinalityBenchmarks.FlushUniqueTerms", invocation.Arguments);
    }

    [Fact]
    public void CaseFilterWithNoManifestMatchFailsBeforeRunning()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = Options(suite: "core", arguments: ["--filter", "*NoSuchCaseMethod*"]) with
        {
            Profile = "leancorpus-search-v1",
            MaxCases = 4
        };

        var exception = Assert.Throws<InvalidOperationException>(() => BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot));

        Assert.Contains("No benchmark cases remain", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BoundedCaseSelectionRejectsBudgetOverflow()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
        var options = Options(suite: "all") with
        {
            Case = "collapse-complete-leancorpus,collapse-complete-lucenenet",
            MaxCases = 1
        };

        var exception = Assert.Throws<InvalidOperationException>(() => BenchmarkCommandPlanner.Create(options, configuration, checkout.RepositoryRoot));

        Assert.Contains("above the --max-cases budget", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SelectsAllConfiguredBenchmarkProjectsForTheAllSelector()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var plan = BenchmarkCommandPlanner.Create(Options(suite: "all"), configuration, checkout.RepositoryRoot);

        Assert.Equal(["compression", "core", "text"], plan.Invocations.Select(static invocation => invocation.ProjectKey));
        Assert.All(plan.Invocations, invocation => Assert.True(File.Exists(invocation.ProjectPath)));
    }

    [Fact]
    public void IncludesOnlyConfiguredProjectsForAllWithExplicit()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var plan = BenchmarkCommandPlanner.Create(Options(suite: "all-with-explicit"), configuration, checkout.RepositoryRoot);

        Assert.Equal(["core"], plan.Invocations.Select(static invocation => invocation.ProjectKey));
        Assert.Contains("all-with-explicit", plan.Invocations[0].Arguments);
    }

    [Fact]
    public void UsesConfiguredControlledCorpusPolicyForStandaloneSuites()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();

        var standalone = BenchmarkCommandPlanner.Create(Options(suite: "codec-frame", controlled: true), configuration, checkout.RepositoryRoot);
        var corpus = BenchmarkCommandPlanner.Create(Options(suite: "query", controlled: true), configuration, checkout.RepositoryRoot);

        Assert.False(standalone.CorpusOnly);
        Assert.True(corpus.CorpusOnly);
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
    public async Task DryRunBuildsWithSingleNodeBeforeLaunchingWithoutBuild()
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
        Assert.Equal(3, progress.Count);
        Assert.Contains("aggregate 30 minutes (strategy)", progress[0], StringComparison.Ordinal);
        var buildCommand = progress[1];
        Assert.Contains("dotnet build", buildCommand, StringComparison.Ordinal);
        Assert.Contains("--disable-build-servers", buildCommand, StringComparison.Ordinal);
        Assert.Contains("-m:1", buildCommand, StringComparison.Ordinal);
        Assert.Contains("-p:UseSharedCompilation=false", buildCommand, StringComparison.Ordinal);

        var runCommand = progress[2];
        Assert.Contains("dotnet run --no-build", runCommand, StringComparison.Ordinal);
        Assert.DoesNotContain("-m:1", runCommand, StringComparison.Ordinal);
    }

    private static BenchmarkCommandOptions Options(
        string suite = "query",
        string strategy = "fast",
        string framework = "net10.0",
        string dataset = "synthetic",
        int documentCount = 0,
        string? area = null,
        IReadOnlyList<string>? arguments = null,
        bool dry = false,
        bool controlled = false)
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
            Controlled: controlled,
            CorpusOnly: false,
            GcDump: false,
            PrepareData: false,
            BookCount: 200,
            Dry: dry,
            List: false,
            arguments ?? []);
}
