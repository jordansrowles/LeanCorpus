using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Commands;

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
        Assert.Contains("data", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dataforge", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("benchmark", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("docs", output.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ui", output.ToString(), StringComparison.OrdinalIgnoreCase);
        var helpLines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.DoesNotContain(helpLines, line => line.TrimStart().StartsWith("test ", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(error.ToString());
        Assert.Empty(handler.Calls);
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
    public void UnmigratedLegacyCommandExplainsThePowerShellTransitionPath()
    {
        var handler = new RecordingCommandHandler();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = CommandLineApplication.Run(["test", "-Suite", "core"], output, error, handler);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("pwsh ./devops.ps1 test", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(handler.Calls);
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
    public void LegacyCommandFixtureIsValidAndCoversMigratedSlices()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Contracts", "legacy-command-contracts.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();

        Assert.Equal(14, cases.Length);
        Assert.Equal(cases.Length, cases.Select(item => item.GetProperty("id").GetString()).Distinct().Count());
        var managedCommands = new[] { "build", "clean", "coverage", "report", "server" };
        Assert.All(cases, item => Assert.Contains(item.GetProperty("command").GetString(), managedCommands));
        Assert.Equal(new[] { "report-code-strict-json", "report-files-path-glob", "report-git-json" },
            cases.Where(item => item.GetProperty("command").GetString() == "report")
                .Select(item => item.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "coverage-core-default" },
            cases.Where(item => item.GetProperty("command").GetString() == "coverage")
                .Select(item => item.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
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
    public void UiCommandForwardsTheExplicitRepository()
    {
        var handler = new RecordingCommandHandler();
        const string repository = "/tmp/LeanCorpus checkout";

        Assert.Equal(0, Invoke(["--repo", repository, "ui"], handler));

        Assert.Equal(new UiCommandOptions(repository), Assert.IsType<UiCommandOptions>(Assert.Single(handler.Calls)));
    }

    private static int Invoke(string[] args, RecordingCommandHandler handler)
        => CommandLineApplication.Run(args, TextWriter.Null, TextWriter.Null, handler);

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
    }
}
