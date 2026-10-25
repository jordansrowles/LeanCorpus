using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using System.Text.Json.Nodes;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Commands;

public sealed class CommandLineContractTests
{
    [Fact]
    public void HelpListsOnlyTheManagedCommandSlices()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new RecordingCommandHandler();
        var root = Program.BuildRootCommand(handler);

        var exitCode = root.Parse(["--help"]).Invoke(new InvocationConfiguration { Output = output, Error = error });

        Assert.Equal(0, exitCode);
        Assert.Contains("build", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("clean", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("server", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pack", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("distribute", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dataforge", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmark", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ui", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("test", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("aot", output.ToString(), StringComparison.OrdinalIgnoreCase);
        var helpLines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(helpLines, line => line.TrimStart().StartsWith("test ", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(error.ToString());
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void HelpOmitsRetiredPowerShellCommandAliases()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new RecordingCommandHandler();

        var exitCode = Program.BuildRootCommand(handler).Parse(["--help"])
            .Invoke(new InvocationConfiguration { Output = output, Error = error });

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("benchmarks", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("setup", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void VersionDoesNotRequireRepositoryOrSdkDiscovery()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new RecordingCommandHandler();

        var exitCode = CommandLineApplication.Run(["--version"], output, error, handler);

        Assert.Equal(0, exitCode);
        Assert.False(string.IsNullOrWhiteSpace(output.ToString()));
        Assert.Empty(error.ToString());
        Assert.Empty(handler.Calls);
    }

    [Theory]
    [InlineData(new[] { "benchmarks", "docs" }, "docs build")]
    [InlineData(new[] { "setup" }, "setup.ps1")]
    public void RetiredPowerShellEntryPointsGiveManagedGuidance(string[] args, string expectedGuidance)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new RecordingCommandHandler();

        var exitCode = CommandLineApplication.Run(args, output, error, handler);

        Assert.Equal(2, exitCode);
        Assert.Contains(expectedGuidance, error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PowerShell path", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void DocsCommandDefaultsToBuildAndAcceptsLegacySkipAliases()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["docs", "prepare", "-SkipBenchmarks", "-SkipCoverage"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new DocsCommandOptions("prepare", true, true),
            Assert.IsType<DocsCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void DocsCommandDefaultsToBuildWhenActionIsOmitted()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["docs"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new DocsCommandOptions("build", false, false),
            Assert.IsType<DocsCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void DocsTestPreservesFrameworkAndNoRestoreOptions()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["docs", "test", "-Framework", "net10.0", "-NoRestore"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new DocsCommandOptions("test", false, false, "net10.0", true),
            Assert.IsType<DocsCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void ReportAcceptsLegacyAliasesAndPreservesThePathGlob()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["report", "files", "-Top", "3", "-Path", "docs/**", "-Json", "-Strict"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new ReportCommandOptions("files", 3, "docs/**", true, true), Assert.IsType<ReportCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void ReportDefaultsToAllWithoutForcingRepositoryDiscoveryDuringParsing()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["report"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new ReportCommandOptions("all", 10, null, false, false), Assert.IsType<ReportCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void BuildSolutionDefaultsDoNotForceAFramework()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["build"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new BuildCommandOptions("Release", null, null), Assert.IsType<BuildCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void BuildSolutionDoesNotForwardAProjectFrameworkOption()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["build", "-Framework", "net10.0"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new BuildCommandOptions("Release", null, null), Assert.IsType<BuildCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void BuildProjectAcceptsLegacySingleDashAliasesAndPreservesProjectToken()
    {
        var handler = new RecordingCommandHandler();
        const string project = "src/devops/project with spaces/Tool.csproj";

        var exitCode = Invoke(["build", "-Project", project, "-Configuration", "Debug", "-Framework", "net10.0"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new BuildCommandOptions("Debug", project, "net10.0"), Assert.IsType<BuildCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void BuildProjectDefaultsToNet11WhenFrameworkIsOmitted()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["build", "-Project", "src/devops/Tool.csproj"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new BuildCommandOptions("Release", "src/devops/Tool.csproj", "net11.0"), Assert.IsType<BuildCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void ExplicitRepositorySelectionFlowsToManagedCommandOptions()
    {
        var handler = new RecordingCommandHandler();
        const string repository = "/tmp/LeanCorpus checkout";

        Assert.Equal(0, Invoke(["--repo", repository, "build"], handler));

        Assert.Equal(repository, Assert.IsType<BuildCommandOptions>(Assert.Single(handler.Calls)).RepositoryPath);
    }

    [Fact]
    public void PackAcceptsLegacyFlagsAndDefaultsToRelease()
    {
        var handler = new RecordingCommandHandler();
        const string repository = "/tmp/LeanCorpus checkout";

        Assert.Equal(0, Invoke(["--repo", repository, "pack", "-CompressionOnly", "-NoBuild", "-Clean", "-ValidateCompression"], handler));

        Assert.Equal(new PackCommandOptions("Release", true, true, true, false, true, repository),
            Assert.IsType<PackCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void DataAcceptsLegacyDatasetFlagsAndExplicitRepository()
    {
        var handler = new RecordingCommandHandler();
        const string repository = "/tmp/LeanCorpus checkout";

        Assert.Equal(0, Invoke(["--repo", repository, "data", "gutenberg", "-BookCount", "321", "-OutputDir", "/tmp/books"], handler));

        Assert.Equal(new DataCommandOptions("gutenberg", 321, "/tmp/books", "https://api.nuget.org/v3/index.json", repository),
            Assert.IsType<DataCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void DataMigrationFixtureCommandAcceptsThePackageSourceAndOutputDirectory()
    {
        var handler = new RecordingCommandHandler();
        const string outputDirectory = "/tmp/migration fixtures";

        Assert.Equal(0, Invoke(["data", "migration-fixtures-311", "-PackageSource", "/tmp/nuget-feed", "-OutputDir", outputDirectory], handler));

        Assert.Equal(new DataCommandOptions("migration-fixtures-311", 200, outputDirectory, "/tmp/nuget-feed"),
            Assert.IsType<DataCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void BenchmarkLeavesDatasetUnspecifiedForConfiguredDefault()
    {
        var handler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["benchmark"], handler));

        Assert.Equal(string.Empty, Assert.IsType<BenchmarkCommandOptions>(Assert.Single(handler.Calls)).Dataset);
    }

    [Fact]
    public void BenchmarkParsesTypedBoundedCaseSelectors()
    {
        var handler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["benchmark", "--suite", "all", "--profile", "leancorpus-search-v1",
            "--case", "collapse-complete-leancorpus,collapse-complete-lucenenet", "--engine", "leancorpus,lucenenet",
            "--workload", "collapse-top-score-count-v1", "--max-cases", "2", "--dry"], handler));

        var options = Assert.IsType<BenchmarkCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal("leancorpus-search-v1", options.Profile);
        Assert.Equal("collapse-complete-leancorpus,collapse-complete-lucenenet", options.Case);
        Assert.Equal("leancorpus,lucenenet", options.Engine);
        Assert.Equal("collapse-top-score-count-v1", options.WorkloadId);
        Assert.Equal(2, options.MaxCases);
        Assert.True(options.Dry);
    }

    [Fact]
    public void BenchmarkWorkloadKeepsTypedScenarioAndBudgetOptions()
    {
        var handler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["--repo", "/tmp/LeanCorpus checkout", "benchmark", "workload", "ingest-search-refresh",
            "--engine", "both", "--framework", "net10.0", "--doc-count", "32",
            "--search-operations", "24", "--concurrency", "4", "--queue-capacity", "1",
            "--refresh-every", "8", "--duration-seconds", "30", "--memory-mib", "4096", "--dry"], handler));

        var command = Assert.IsType<BenchmarkCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal("/tmp/LeanCorpus checkout", command.RepositoryPath);
        var workload = Assert.IsType<BenchmarkWorkloadOptions>(command.Workload);
        Assert.Equal("ingest-search-refresh", workload.Scenario);
        Assert.Equal("both", workload.Engine);
        Assert.Equal("net10.0", workload.Framework);
        Assert.Equal(32, workload.DocumentCount);
        Assert.Equal(24, workload.SearchOperations);
        Assert.Equal(4, workload.Concurrency);
        Assert.Equal(1, workload.QueueCapacity);
        Assert.Equal(8, workload.RefreshEveryDocuments);
        Assert.Equal(30, workload.DurationSeconds);
        Assert.Equal(4096, workload.MemoryBudgetMiB);
        Assert.True(workload.Dry);
    }

    [Fact]
    public void DataForgePreservesExternalToolArgumentsAndRepositorySelection()
    {
        var handler = new RecordingCommandHandler();
        const string repository = "/tmp/LeanCorpus checkout";
        string[] arguments = ["generate", "-Profile", "leancorpus-search", "-Seed", "42", "-Output", "data dir"];

        Assert.Equal(0, Invoke(["--repo", repository, "dataforge", .. arguments], handler));

        var actual = Assert.IsType<DataForgeCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal(arguments, actual.Arguments);
        Assert.Equal(repository, actual.RepositoryPath);
    }

    [Fact]
    public void RemoteBenchmarkStartPreservesArgumentTokensAfterSeparator()
    {
        var handler = new RecordingCommandHandler();
        var arguments = new[] { "--suite", "query", "--strat", "intense", "--", "--filter", "*Name With Spaces*" };

        Assert.Equal(0, Invoke(["--repo", "/tmp/LeanCorpus checkout", "benchmark", "remote", "start",
            "--host", "jordan@bench", "--path", "/srv/LeanCorpus checkout", "--branch", "issue-97", "--", .. arguments], handler));

        var actual = Assert.IsType<BenchmarkRemoteCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal("start", actual.Action);
        Assert.Equal("jordan@bench", actual.Host);
        Assert.Equal("/srv/LeanCorpus checkout", actual.RemotePath);
        Assert.Equal("issue-97", actual.Branch);
        Assert.Equal(arguments, actual.BenchmarkArguments);
        Assert.Equal("/tmp/LeanCorpus checkout", actual.RepositoryPath);
    }

    [Fact]
    public void RemoteBenchmarkAcceptsLegacyAliasesAndTokenisesQuotedBenchmarkArguments()
    {
        var handler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["benchmark", "remote", "-Remote", "jordan@bench", "-RemotePath", "/srv/repo",
            "-Branch", "main", "-SessionName", "nightly", "-BenchmarkArgs", "--suite query --filter '*Term Query*'"], handler));

        var actual = Assert.IsType<BenchmarkRemoteCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal("start", actual.Action);
        Assert.Equal(["--suite", "query", "--filter", "*Term Query*"], actual.BenchmarkArguments);
        Assert.Equal("nightly", actual.SessionName);
    }

    [Fact]
    public void RemoteBenchmarkStatusRequiresAValidRunIdentityBeforeDispatch()
    {
        var handler = new RecordingCommandHandler();
        var error = new StringWriter();

        var exitCode = CommandLineApplication.Run(["benchmark", "remote", "status", "--run-id", "bad-id"], new StringWriter(), error, handler);

        Assert.Equal(2, exitCode);
        Assert.Contains("not a valid remote benchmark run id", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void RemoteLegacyBenchmarkArgumentParserPreservesBackslashPatterns()
    {
        var handler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["benchmark", "remote", "-BenchmarkArgs", "--filter '*Term\\.Query*'"], handler));

        var actual = Assert.IsType<BenchmarkRemoteCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal(["--filter", "*Term\\.Query*"], actual.BenchmarkArguments);
    }

    [Fact]
    public void DataForgeQualificationRoutesToTheManagedHandler()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = CommandLineApplication.Run(["dataforge", "qualify", "--dry", "--reference-path", "/tmp/wikipedia"], TextWriter.Null, TextWriter.Null, handler);

        Assert.Equal(0, exitCode);
        var actual = Assert.IsType<DataForgeCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal(["qualify", "--dry", "--reference-path", "/tmp/wikipedia"], actual.Arguments);
    }

    [Fact]
    public void BenchmarkAcceptsLegacyAliasesAndPreservesBenchmarkDotNetArguments()
    {
        var handler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["benchmark", "query", "-Strat", "fast", "-Framework", "net10.0", "-DocCount", "700", "-Area", "Search", "--", "--filter", "*Term*", "--job", "dry"], handler));

        var actual = Assert.IsType<BenchmarkCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal("query", actual.Suite);
        Assert.Equal("fast", actual.Strategy);
        Assert.Equal("net10.0", actual.Framework);
        Assert.Equal(700, actual.DocumentCount);
        Assert.Equal("Search", actual.Area);
        Assert.Equal(["--filter", "*Term*", "--job", "dry"], actual.Arguments);
    }

    [Fact]
    public void CleanDefaultsToDefaultTargetAndAcceptsTargetArgument()
    {
        var handler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["clean"], handler));
        Assert.Equal(new CleanCommandOptions("default"), Assert.IsType<CleanCommandOptions>(Assert.Single(handler.Calls)));

        handler.Calls.Clear();
        Assert.Equal(0, Invoke(["clean", "build"], handler));
        Assert.Equal(new CleanCommandOptions("build"), Assert.IsType<CleanCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void ServerDefaultsAndLegacyFlagsAreTypedAndPassthroughIsTokenPreserving()
    {
        var handler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["server", "start"], handler));
        AssertServerOptions("net11.0", "Debug", false, false, false, [], Assert.Single(handler.Calls));

        handler.Calls.Clear();
        Assert.Equal(0, Invoke(["server", "start", "-Framework", "net10.0", "-Configuration", "Release", "-NoBuild", "-NoRestore", "--", "--urls", "http://127.0.0.1:5081"], handler));
        AssertServerOptions("net10.0", "Release", true, true, false, ["--urls", "http://127.0.0.1:5081"], Assert.Single(handler.Calls));
    }

    [Fact]
    public void ServerRejectsApplicationArgumentsWithoutDelimiter()
    {
        var handler = new RecordingCommandHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CommandLineApplication.Run(["server", "start", "--unknown"], output, error, handler);

        Assert.Equal(2, exitCode);
        Assert.Contains("follow the -- delimiter", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void ExternalServerOptionRetainsTheLegacyListenerArguments()
    {
        var handler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["server", "start", "-External"], handler));

        AssertServerOptions("net11.0", "Debug", false, false, true,
            [], Assert.Single(handler.Calls));
    }

    [Fact]
    public void InvalidInputDoesNotInvokeCommandHandler()
    {
        var handler = new RecordingCommandHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CommandLineApplication.Run(["build", "--not-a-build-option"], output, error, handler);

        Assert.NotEqual(0, exitCode);
        Assert.Empty(handler.Calls);
        Assert.NotEmpty(error.ToString());
    }

    [Fact]
    public void TestCommandRoutesToTheManagedHandler()
    {
        var handler = new RecordingCommandHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CommandLineApplication.Run(["test", "-Suite", "core"], output, error, handler);

        Assert.Equal(0, exitCode);
        Assert.Single(handler.Calls);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void TestCommandPreservesLegacySelectionAndExecutionOptions()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(
        [
            "--repo", "/tmp/LeanCorpus checkout", "test", "core,text", "-Framework", "net11.0",
            "-Configuration", "Debug", "-RuntimeIdentifier", "linux-x64", "-Area", "Search",
            "-Category", "Unit", "-Technique", "PropertyBased", "-Filter", "FullyQualifiedName~Writer",
            "-Count", "2", "-Flaky", "-FailFast", "-Diagnostics", "-CI", "-NoRestore",
            "-CollectCoverage", "-Explicit", "-ExplicitOnly", "-FailWarnings", "-Profile", "stress",
            "-Verbosity", "detailed", "-HangTimeout", "off", "-Timeout", "4m", "--", "--seed", "17"
        ], handler);

        Assert.Equal(0, exitCode);
        var actual = Assert.IsType<TestCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal("/tmp/LeanCorpus checkout", actual.RepositoryPath);
        Assert.Equal(["core,text"], actual.RunOptions.Selection.Suites);
        Assert.Equal("net11.0", actual.RunOptions.Selection.Framework);
        Assert.True(actual.RunOptions.Selection.FrameworkExplicit);
        Assert.Equal("Debug", actual.RunOptions.Selection.Configuration);
        Assert.Equal("linux-x64", actual.RunOptions.Selection.RuntimeIdentifier);
        Assert.Equal("Search", actual.RunOptions.Selection.Area);
        Assert.Equal("Unit", actual.RunOptions.Selection.Category);
        Assert.Equal("PropertyBased", actual.RunOptions.Selection.Technique);
        Assert.Equal("FullyQualifiedName~Writer", actual.RunOptions.Selection.Filter);
        Assert.True(actual.RunOptions.Selection.ContinuousIntegration);
        Assert.True(actual.RunOptions.Selection.CollectCoverage);
        Assert.Equal(["--seed", "17"], actual.RunOptions.Selection.AdditionalArguments);
        Assert.Equal(2, actual.RunOptions.Count);
        Assert.True(actual.RunOptions.Flaky);
        Assert.True(actual.RunOptions.FailFast);
        Assert.True(actual.RunOptions.Diagnostics);
        Assert.True(actual.RunOptions.NoRestore);
        Assert.True(actual.RunOptions.Explicit);
        Assert.True(actual.RunOptions.ExplicitOnly);
        Assert.True(actual.RunOptions.FailWarnings);
        Assert.Equal("stress", actual.RunOptions.Profile);
        Assert.Equal("detailed", actual.RunOptions.Verbosity);
        Assert.Equal("off", actual.RunOptions.HangTimeout);
        Assert.Equal("4m", actual.RunOptions.ProcessTimeout);
    }

    [Theory]
    [InlineData("test", "--filter", "FullyQualifiedName~Writer", "filter")]
    [InlineData("test", "-Filter", "FullyQualifiedName~Writer", "filter")]
    [InlineData("test", "--hang-timeout", "off", "hangTimeout")]
    [InlineData("test", "-HangTimeout", "off", "hangTimeout")]
    [InlineData("test", "--rid", "linux-x64", "runtimeIdentifier")]
    [InlineData("test", "--runtime-identifier", "linux-x64", "runtimeIdentifier")]
    [InlineData("test", "-RuntimeIdentifier", "linux-x64", "runtimeIdentifier")]
    [InlineData("aot", "--filter", "FullyQualifiedName~Smoke", "filter")]
    [InlineData("aot", "-Filter", "FullyQualifiedName~Smoke", "filter")]
    [InlineData("aot", "--rid", "linux-x64", "runtimeIdentifier")]
    [InlineData("aot", "--runtime-identifier", "linux-x64", "runtimeIdentifier")]
    [InlineData("aot", "-RuntimeIdentifier", "linux-x64", "runtimeIdentifier")]
    public void TestAndAotCommandsAcceptCanonicalOptionsAndRegisteredAliases(
        string command,
        string option,
        string value,
        string selectedValue)
    {
        var handler = new RecordingCommandHandler();
        string[] arguments = command == "test"
            ? ["test", "core", option, value]
            : ["aot", option, value];

        var exitCode = Invoke(arguments, handler);

        Assert.Equal(0, exitCode);
        var actual = Assert.IsType<TestCommandOptions>(Assert.Single(handler.Calls));
        string selected = selectedValue switch
        {
            "filter" => actual.RunOptions.Selection.Filter,
            "hangTimeout" => actual.RunOptions.HangTimeout,
            "runtimeIdentifier" => actual.RunOptions.Selection.RuntimeIdentifier,
            _ => throw new InvalidOperationException($"Unexpected selected value '{selectedValue}'.")
        };
        Assert.Equal(value, selected);
    }

    [Theory]
    [InlineData("test", "--Filter", "FullyQualifiedName~Writer")]
    [InlineData("test", "--HangTimeout", "100s")]
    [InlineData("test", "--RuntimeIdentifier", "linux-x64")]
    [InlineData("test", "--not-a-test-option", "value")]
    [InlineData("aot", "--Filter", "FullyQualifiedName~Smoke")]
    [InlineData("aot", "--RuntimeIdentifier", "linux-x64")]
    [InlineData("aot", "--not-an-aot-option", "value")]
    public void MistypedOrUnknownManagedOptionsAreNotReportedOnlyAsMissingPassthroughBoundary(
        string command,
        string option,
        string value)
    {
        var handler = new RecordingCommandHandler();
        var output = new StringWriter();
        var error = new StringWriter();
        string[] commandArguments = command == "test"
            ? ["test", "core", option, value]
            : ["aot", option, value];

        var exitCode = CommandLineApplication.Run(commandArguments, output, error, handler);

        Assert.Equal(2, exitCode);
        Assert.Empty(handler.Calls);
        Assert.Contains("Unrecognised", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains(option, error.ToString(), StringComparison.Ordinal);
        Assert.Contains("after '--'", error.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("must follow the -- delimiter", error.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("test")]
    [InlineData("aot")]
    public void TestRunnerArgumentsRequireAndAcceptTheExplicitSeparator(string command)
    {
        var rejectedHandler = new RecordingCommandHandler();
        var output = new StringWriter();
        var error = new StringWriter();
        string[] missingSeparator = command == "test"
            ? ["test", "core", "--seed", "17"]
            : ["aot", "--seed", "17"];

        var rejectedExitCode = CommandLineApplication.Run(missingSeparator, output, error, rejectedHandler);

        Assert.Equal(2, rejectedExitCode);
        Assert.Empty(rejectedHandler.Calls);
        Assert.Contains("--seed", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("after '--'", error.ToString(), StringComparison.Ordinal);

        var acceptedHandler = new RecordingCommandHandler();
        string[] separated = command == "test"
            ? ["test", "core", "--", "--seed", "17"]
            : ["aot", "--", "--seed", "17"];

        Assert.Equal(0, Invoke(separated, acceptedHandler));
        var actual = Assert.IsType<TestCommandOptions>(Assert.Single(acceptedHandler.Calls));
        Assert.Equal(["--seed", "17"], actual.RunOptions.Selection.AdditionalArguments);
    }

    [Fact]
    public void TestListAndExplainAffectedAreSelectionOnlyModes()
    {
        var listHandler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["test", "--list"], listHandler));
        var list = Assert.IsType<TestCommandOptions>(Assert.Single(listHandler.Calls));
        Assert.True(list.List);
        Assert.False(list.ExplainAffected);

        var affectedHandler = new RecordingCommandHandler();

        Assert.Equal(0, Invoke(["test", "affected", "--explain-affected", "--range", "HEAD^..HEAD"], affectedHandler));
        var affected = Assert.IsType<TestCommandOptions>(Assert.Single(affectedHandler.Calls));
        Assert.False(affected.List);
        Assert.True(affected.ExplainAffected);
        Assert.Equal(["affected"], affected.RunOptions.Selection.Suites);
        Assert.Equal("HEAD^..HEAD", affected.RunOptions.Range);
    }

    [Fact]
    public void AotCommandUsesTheManagedNativeRunnerSelection()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(
        [
            "aot", "-Framework", "net11.0", "-RuntimeIdentifier", "linux-x64", "-Configuration", "Debug",
            "-NoRestore", "-CI", "-Count", "2", "-Flaky", "-Diagnostics", "-FailFast", "-Timeout", "30s",
            "-Filter", "FullyQualifiedName~Smoke", "--", "--seed", "42"
        ], handler);

        Assert.Equal(0, exitCode);
        var actual = Assert.IsType<TestCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal(["aot"], actual.RunOptions.Selection.Suites);
        Assert.Equal("net11.0", actual.RunOptions.Selection.Framework);
        Assert.True(actual.RunOptions.Selection.FrameworkExplicit);
        Assert.Equal("linux-x64", actual.RunOptions.Selection.RuntimeIdentifier);
        Assert.Equal("Debug", actual.RunOptions.Selection.Configuration);
        Assert.Equal("FullyQualifiedName~Smoke", actual.RunOptions.Selection.Filter);
        Assert.True(actual.RunOptions.Selection.ContinuousIntegration);
        Assert.Equal(["--seed", "42"], actual.RunOptions.Selection.AdditionalArguments);
        Assert.Equal(2, actual.RunOptions.Count);
        Assert.True(actual.RunOptions.Flaky);
        Assert.True(actual.RunOptions.Diagnostics);
        Assert.True(actual.RunOptions.FailFast);
        Assert.True(actual.RunOptions.NoRestore);
        Assert.Equal("30s", actual.RunOptions.ProcessTimeout);
        Assert.Equal("off", actual.RunOptions.HangTimeout);
        Assert.False(actual.RunOptions.Selection.CollectCoverage);
    }

    [Fact]
    public void BackupRestoreMatrixUsesItsDedicatedCommandRoute()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["test", "backup-restore-matrix", "-Framework", "net11.0", "-Count", "1", "-Iterations", "2"], handler);

        Assert.Equal(0, exitCode);
        var actual = Assert.IsType<BackupRestoreMatrixCommandOptions>(Assert.Single(handler.Calls));
        Assert.Null(actual.RepositoryPath);
        Assert.Equal("net11.0", actual.MatrixOptions.Framework);
        Assert.Equal("Release", actual.MatrixOptions.Configuration);
        Assert.Equal(1, actual.MatrixOptions.Count);
        Assert.Equal(2, actual.MatrixOptions.Iterations);
        Assert.False(actual.MatrixOptions.NoRestore);
        Assert.Contains("test backup-restore-matrix", actual.MatrixOptions.CommandLine, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsCommandRoutesToTheManagedHandler()
    {
        var handler = new RecordingCommandHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CommandLineApplication.Run(
            ["diagnostics", "trace", "--pid", "42", "--", "--providers", "Microsoft-DotNETRuntime"],
            output, error, handler);

        Assert.Equal(0, exitCode);
        var actual = Assert.IsType<DiagnosticsCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal(["trace", "--pid", "42", "--", "--providers", "Microsoft-DotNETRuntime"], actual.Arguments);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void RootHelpListsDiagnosticsAfterItsManagedRouteIsEnabled()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CommandLineApplication.Run(["--help"], output, error, new RecordingCommandHandler());

        Assert.Equal(0, exitCode);
        Assert.Contains("diagnostics", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void RootHelpDoesNotLoadConfiguration()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new RecordingCommandHandler();
        var root = Program.BuildRootCommand(handler);

        var exitCode = root.Parse(["--help"]).Invoke(new InvocationConfiguration { Output = output, Error = error });

        Assert.Equal(0, exitCode);
        Assert.Contains("config", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(error.ToString());
        Assert.Empty(handler.Calls);
    }

    [Fact]
    public void ConfigValidateLoadsOnlyTheResolvedConfiguration()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new ManagedCommandHandler(output, error);

        var exitCode = CommandLineApplication.Run(
            ["--repo", checkout.RepositoryRoot, "config", "validate"], output, error, handler);

        Assert.Equal(0, exitCode);
        Assert.Contains("Managed DevOps configuration is valid", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void ConfigValidateAcceptsAnExplicitDeepManifestCheck()
    {
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var handler = new RecordingCommandHandler();

        var exitCode = CommandLineApplication.Run(
            ["--repo", checkout.RepositoryRoot, "config", "validate", "--deep"], new StringWriter(), new StringWriter(), handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new ConfigValidateCommandOptions(checkout.RepositoryRoot, Deep: true),
            Assert.IsType<ConfigValidateCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void ConfigValidateReportsInvalidConfigurationAsValidationFailure()
    {
        using var checkout = TemporaryConfigCheckout.Create();
        var path = checkout.ConfigPath("benchmark-projects.json");
        var projects = JsonNode.Parse(File.ReadAllText(path))!;
        projects["data"]!["core"]!["Project"] = "../outside.csproj";
        File.WriteAllText(path, projects.ToJsonString());

        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new ManagedCommandHandler(output, error);
        var exitCode = CommandLineApplication.Run(
            ["--repo", checkout.Root, "config", "validate"], output, error, handler);

        Assert.Equal(2, exitCode);
        Assert.Contains("Managed DevOps configuration is invalid", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("benchmark-projects.json", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("$.data.core.Project", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void DiagnosticsProcessListIncludesTheCurrentProcess()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new ManagedCommandHandler(output, error);

        var exitCode = handler.Diagnostics(new DiagnosticsCommandOptions(["ps"]));

        Assert.Equal(0, exitCode);
        Assert.Contains(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void DiagnosticsRejectsInvalidPidBeforeCreatingRunArtifacts()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"DevOps invalid diagnostics {Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var output = new StringWriter();
        var error = new StringWriter();
        var handler = new ManagedCommandHandler(output, error);

        try
        {
            var exitCode = handler.Diagnostics(new DiagnosticsCommandOptions(["trace", "--pid", "0"], directory));

            Assert.Equal(2, exitCode);
            Assert.Contains("--pid must be a positive process ID", error.ToString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(directory, "artifacts")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void BenchmarkRoutesToTheManagedHandlerWithLegacyAliases()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["benchmark", "-Suite", "query", "-Strat", "fast", "-Dry"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal("query", Assert.IsType<BenchmarkCommandOptions>(Assert.Single(handler.Calls)).Suite);
        var actual = Assert.IsType<BenchmarkCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal("fast", actual.Strategy);
        Assert.True(actual.Dry);
    }

    [Fact]
    public void BenchmarkSuiteOptionPreservesChildOptionTokensAfterSeparator()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["benchmark", "--suite", "query", "--strat", "fast", "--dry", "--", "--filter", "*Term*"], handler);

        Assert.Equal(0, exitCode);
        var actual = Assert.IsType<BenchmarkCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal(["--filter", "*Term*"], actual.Arguments);
    }

    [Fact]
    public void CoverageCommandRoutesThroughManagedHandler()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["coverage", "-Suite", "core", "-Framework", "net10.0", "-Configuration", "Debug",
            "-Clean", "-IncludePerformance", "-GenerateReport"], handler);

        Assert.Equal(0, exitCode);
        var actual = Assert.IsType<CoverageCommandOptions>(Assert.Single(handler.Calls));
        Assert.Equal("net10.0", actual.Framework);
        Assert.Equal("Debug", actual.Configuration);
        Assert.Equal("core", actual.Suite);
        Assert.True(actual.Clean);
        Assert.True(actual.IncludePerformance);
        Assert.True(actual.GenerateReport);
    }

    [Fact]
    public void DistributeCommandPreservesRidConfigurationAndSelfContainedMode()
    {
        var handler = new RecordingCommandHandler();

        var exitCode = Invoke(["distribute", "--rid", "linux-x64", "--configuration", "Release", "--self-contained"], handler);

        Assert.Equal(0, exitCode);
        Assert.Equal(new DistributeCommandOptions("linux-x64", "Release", true),
            Assert.IsType<DistributeCommandOptions>(Assert.Single(handler.Calls)));
    }

    [Fact]
    public void UiCommandForwardsTheExplicitRepository()
    {
        var handler = new RecordingCommandHandler();
        const string repository = "/tmp/LeanCorpus checkout";

        Assert.Equal(0, Invoke(["--repo", repository, "ui"], handler));

        Assert.Equal(new UiCommandOptions(repository), Assert.IsType<UiCommandOptions>(Assert.Single(handler.Calls)));
    }

    private static int Invoke(string[] args, RecordingCommandHandler handler)
        => CommandLineApplication.Run(args, TextWriter.Null, TextWriter.Null, handler);

    private sealed class TemporaryConfigCheckout : IDisposable
    {
        private TemporaryConfigCheckout(string root) => Root = root;

        public string Root { get; }

        public string ConfigPath(string fileName) => Path.Combine(Root, ".config", "devops", fileName);

        public static TemporaryConfigCheckout Create()
        {
            var sourceRoot = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory).RepositoryRoot;
            var root = Path.Combine(Path.GetTempPath(), "DevOps config validation " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(Path.Combine(root, ".config", "devops"));
            File.Copy(Path.Combine(sourceRoot, "Rowles.LeanCorpus.slnx"), Path.Combine(root, "Rowles.LeanCorpus.slnx"));
            File.Copy(Path.Combine(sourceRoot, "global.json"), Path.Combine(root, "global.json"));

            var sourceConfiguration = Path.Combine(sourceRoot, ".config", "devops");
            foreach (var source in Directory.EnumerateFiles(sourceConfiguration, "*.json"))
                File.Copy(source, Path.Combine(root, ".config", "devops", Path.GetFileName(source)));

            CopyAuditReferences(sourceRoot, root);
            CreateProjectStubs(root, Path.Combine(root, ".config", "devops", "test-suites.json"));
            CreateProjectStubs(root, Path.Combine(root, ".config", "devops", "benchmark-projects.json"));
            return new TemporaryConfigCheckout(root);
        }

        private static void CopyAuditReferences(string sourceRepositoryRoot, string destinationRepositoryRoot)
        {
            var auditPath = Path.Combine(sourceRepositoryRoot, ".config", "devops", "benchmark-correctness-audit.json");
            using var audit = JsonDocument.Parse(File.ReadAllBytes(auditPath));
            foreach (var gate in audit.RootElement.GetProperty("gates").EnumerateArray())
            {
                foreach (var propertyName in new[] { "sourceFile", "fixturePath" })
                {
                    var relativePath = gate.GetProperty(propertyName).GetString()!;
                    var sourcePath = Path.Combine(sourceRepositoryRoot, relativePath);
                    var destinationPath = Path.Combine(destinationRepositoryRoot, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Copy(sourcePath, destinationPath, overwrite: true);
                }
            }
        }

        private static void CreateProjectStubs(string root, string configurationPath)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configurationPath));
            foreach (var project in document.RootElement.GetProperty("data").EnumerateObject())
            {
                var projectPath = project.Value.GetProperty("Project").GetString()!;
                var fullPath = Path.Combine(root, projectPath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllText(fullPath, "<Project />");
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }

    private static void AssertServerOptions(
        string framework,
        string configuration,
        bool noBuild,
        bool noRestore,
        bool external,
        string[] applicationArguments,
        object call)
    {
        var actual = Assert.IsType<ServerStartCommandOptions>(call);
        Assert.Equal(framework, actual.Framework);
        Assert.Equal(configuration, actual.Configuration);
        Assert.Equal(noBuild, actual.NoBuild);
        Assert.Equal(noRestore, actual.NoRestore);
        Assert.Equal(external, actual.External);
        Assert.Equal(applicationArguments, actual.ApplicationArguments);
    }

    private sealed class RecordingCommandHandler : ICommandHandler
    {
        public List<object> Calls { get; } = [];

        public int Build(BuildCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Clean(CleanCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Docs(DocsCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int StartServer(ServerStartCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Report(ReportCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Coverage(CoverageCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Pack(PackCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Distribute(DistributeCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Data(DataCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int DataForge(DataForgeCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Diagnostics(DiagnosticsCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Benchmark(BenchmarkCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int BenchmarkRemote(BenchmarkRemoteCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Ui(UiCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int ValidateConfiguration(ConfigValidateCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int Test(TestCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }

        public int BackupRestoreMatrix(BackupRestoreMatrixCommandOptions options)
        {
            Calls.Add(options);
            return 0;
        }
    }
}
