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
    public void DataForgeQualificationRemainsOnTheLegacyRouteUntilItsManagedWorkflowExists()
    {
        var handler = new RecordingCommandHandler();
        var error = new StringWriter();

        var exitCode = CommandLineApplication.Run(["dataforge", "qualify", "-Dry"], TextWriter.Null, error, handler);

        Assert.Equal(2, exitCode);
        Assert.Contains("pwsh ./devops.ps1 dataforge qualify", error.ToString(), StringComparison.Ordinal);
        Assert.Empty(handler.Calls);
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
    public void LegacyCommandFixtureIsValidAndCoversMigratedSlices()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Contracts", "legacy-command-contracts.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var cases = document.RootElement.GetProperty("cases").EnumerateArray().ToArray();

        Assert.Equal(13, cases.Length);
        Assert.Equal(cases.Length, cases.Select(item => item.GetProperty("id").GetString()).Distinct().Count());
        var managedCommands = new[] { "build", "clean", "report", "server" };
        Assert.All(cases, item => Assert.Contains(item.GetProperty("command").GetString(), managedCommands));
        Assert.Equal(new[] { "report-code-strict-json", "report-files-path-glob", "report-git-json" },
            cases.Where(item => item.GetProperty("command").GetString() == "report")
                .Select(item => item.GetProperty("id").GetString()).Order(StringComparer.Ordinal));
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
    }
}
