using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.DataForge.Tool;
using Rowles.DataForge.Workloads;
using Spectre.Console.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Commands;

public sealed class DevOpsWorkbenchTests
{
    [Fact]
    public void Run_HomeScreenDoesNotLoadConfigurationOrOptionalRepositoryMetadata()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        int configurationLoads = 0;
        Choose(console, "Exit", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken,
            () => configurationLoads++).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(0, configurationLoads);
        Assert.Equal(0, operations.RepositoryMetadataReads);
        Assert.Contains("DevOps", console.Output, StringComparison.Ordinal);
        Assert.Contains("LeanCorpus", console.Output, StringComparison.Ordinal);
        Assert.Contains("Checkout: /repo", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_RequiresAnInteractiveConsole()
    {
        var console = new TestConsole();
        var operations = new RecordingWorkbenchOperations();

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(2, exitCode);
        Assert.Contains("interactive terminal", console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(operations.Calls);
    }

    [Fact]
    public void Run_ProvidesCommandFallbackWhenInteractiveConsoleDoesNotSupportAnsi()
    {
        var console = new TestConsole().Interactive().SupportsAnsi(false);
        var operations = new RecordingWorkbenchOperations();

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(2, exitCode);
        Assert.Contains("ANSI", console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("documented DevOps commands", console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(operations.Calls);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1051", Justification = "This test intentionally passes a pre-cancelled token to exercise prompt cancellation.")]
    public void Run_ReturnsCancellationOutcomeWhenPromptCancellationIsRequested()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        int exitCode = CreateWorkbench(console, operations, cancellation.Token).Run();

        Assert.Equal(130, exitCode);
        Assert.Contains("cancelled", console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(operations.Calls);
    }

    [Fact]
    public void TestSelection_PassesConfiguredChoicesToManagedOperation()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Tests and quality", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");
        Choose(console, "Run tests", "Back", "Run tests", "Coverage", "AOT smoke", "Repository report");
        Choose(console, "core", "Back", "core");
        Choose(console, "net10.0", "Back", "net10.0", "net11.0");
        Choose(console, "Release", "Back", "Release", "Debug");
        Choose(console, "unit", "Back", "unit", "integration", "stress");
        Choose(console, "Run tests", "Back", "Run tests");
        Choose(console, "Exit", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "test", "core", "--framework", "net10.0", "--configuration", "Release", "--profile", "unit" },
            Assert.Single(operations.Calls));
    }

    [Fact]
    public void BuildSelectionUsesManagedCliTokenArguments()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Build and package", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");
        Choose(console, "Build a project", "Back", "Build a project", "Clean artefacts", "Pack packages", "Distribute archive");
        Choose(console, "Core library", "Back", "Core library", "DevOps tool", "Community Server Core", "Repository solution");
        Choose(console, "Release", "Back", "Release", "Debug");
        Choose(console, "net10.0", "Back", "net10.0", "net11.0");
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "build", "--configuration", "Release", "--project",
            "src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj", "--framework", "net10.0" },
            Assert.Single(operations.Calls));
    }

    [Fact]
    public void DataForgePreview_UsesTheManagedOperationWithTokenArguments()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "DataForge", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");
        Choose(console, "Preview release qualification", "Back", "Preview release qualification", "Run release qualification",
            "Generate a dataset", "Reference dataset", "List profiles", "Inspect a generated dataset", "Verify a generated dataset",
            "Reproduce a generated dataset", "Enter token arguments");
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "dataforge", "qualify", "--dry" }, Assert.Single(operations.Calls));
    }

    [Fact]
    public void DataForgeGenerationWorkbench_EmitsProfileSpecificTokensAcceptedByTheRealParser()
    {
        string root = Path.Combine(Path.GetTempPath(), "workbench-dataforge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scenarios = new[]
        {
            (Name: "stress-search", Profile: "leancorpus-stress", Mode: "search", Language: (string?)null,
                OptionValues: new[] { "100", "200", "8", "4", "3" }, AllowLarge: "No",
                Expected: new[] { "-VeryLongShareBasisPoints", "100", "-LongShareBasisPoints", "200",
                    "-CategoryCardinality", "8", "-RegionCardinality", "4", "-RareAnchorBasisPoints", "3" }),
            (Name: "stress-vector", Profile: "leancorpus-stress", Mode: "vector", Language: (string?)null,
                OptionValues: new[] { "16", "Uniform", "4", "1" }, AllowLarge: "No",
                Expected: new[] { "-Dimension", "16", "-VectorDistribution", "Uniform", "-ClusterCount", "4", "-QueryCount", "1" }),
            (Name: "stress-hybrid", Profile: "leancorpus-stress", Mode: "hybrid", Language: (string?)null,
                OptionValues: new[] { "16", "8", "5" }, AllowLarge: "No",
                Expected: new[] { "-Dimension", "16", "-CategoryCardinality", "8", "-NarrowFilterBasisPoints", "5" }),
            (Name: "stress-search-large-consent", Profile: "leancorpus-stress", Mode: "search", Language: (string?)null,
                OptionValues: new[] { "", "", "", "", "" }, AllowLarge: "Yes, allow large",
                Expected: Array.Empty<string>()),
            (Name: "multilingual", Profile: "rowles-text-multilingual", Mode: (string?)null, Language: "ko",
                OptionValues: Array.Empty<string>(), AllowLarge: (string?)null,
                Expected: Array.Empty<string>()),
            (Name: "vector", Profile: "leancorpus-vector", Mode: (string?)null, Language: (string?)null,
                OptionValues: new[] { "16", "Uniform", "4", "1" }, AllowLarge: (string?)null,
                Expected: new[] { "-Dimension", "16", "-VectorDistribution", "Uniform", "-ClusterCount", "4", "-QueryCount", "1" }),
            (Name: "hybrid", Profile: "leancorpus-hybrid", Mode: (string?)null, Language: (string?)null,
                OptionValues: new[] { "16" }, AllowLarge: (string?)null,
                Expected: new[] { "-Dimension", "16" }),
            (Name: "search", Profile: "leancorpus-search", Mode: (string?)null, Language: (string?)null,
                OptionValues: Array.Empty<string>(), AllowLarge: (string?)null,
                Expected: Array.Empty<string>()),
            (Name: "profile-defaults", Profile: "leancorpus-e2e", Mode: (string?)null, Language: (string?)null,
                OptionValues: Array.Empty<string>(), AllowLarge: (string?)null,
                Expected: Array.Empty<string>())
        };

        try
        {
            foreach (var scenario in scenarios)
            {
                string outputPath = Path.Combine(root, scenario.Name);
                string[] promptValues = scenario.Name == "profile-defaults"
                    ? ["", "", "", outputPath]
                    : ["1", "42", "8", .. scenario.OptionValues, outputPath];
                string[] tokens = CaptureDataForgeGenerationArguments(
                    scenario.Profile, scenario.Mode, scenario.Language, scenario.AllowLarge, promptValues);

                var expected = new List<string> { "dataforge", "generate", "-Profile", scenario.Profile };
                if (scenario.Mode is not null)
                    expected.AddRange(["-Mode", scenario.Mode]);
                if (scenario.Language is not null)
                    expected.AddRange(["-Language", scenario.Language]);
                if (scenario.Name != "profile-defaults")
                    expected.AddRange(["-Version", "1", "-Seed", "42", "-Count", "8"]);
                expected.AddRange(scenario.Expected);
                if (scenario.AllowLarge == "Yes, allow large")
                    expected.Add("-AllowLarge");
                expected.AddRange(["-Output", outputPath]);
                Assert.Equal(expected, tokens);

                using var output = new StringWriter();
                using var error = new StringWriter();
                int exitCode = DataForgeCommandLine.Run(tokens.Skip(1).ToArray(), output, error, root);
                Assert.True(exitCode == 0, $"DataForge rejected {scenario.Name}: {error}{output}");
                Assert.True(Directory.Exists(outputPath), $"DataForge did not materialise {scenario.Name}.");
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void DataForgeGenerationWorkbench_BackFromProfileOrModeDoesNotRunACommand()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "DataForge", MainMenu);
        Choose(console, "Generate a dataset", DataForgeActions);
        Choose(console, "Back", DataForgeProfiles);
        Choose(console, "Exit", MainMenu);

        Assert.Equal(0, CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run());
        Assert.Empty(operations.Calls);
    }

    [Fact]
    public void DataForgeGenerationWorkbench_BackFromStressModeDoesNotRunACommand()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "DataForge", MainMenu);
        Choose(console, "Generate a dataset", DataForgeActions);
        Choose(console, "leancorpus-stress", DataForgeProfiles);
        Choose(console, "Back", StressModes);
        Choose(console, "Exit", MainMenu);

        Assert.Equal(0, CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run());
        Assert.Empty(operations.Calls);
    }

    [Fact]
    public void ManagedWorkbenchOperations_DoesNotCreateAHandlerWhenCancellationPrecedesTheCommand()
    {
        int handlerCreations = 0;
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var operations = new ManagedWorkbenchOperations(
            _ =>
            {
                handlerCreations++;
                return new RecordingCommandHandler();
            },
            checkout,
            TextWriter.Null,
            TextWriter.Null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => operations.RunCommand(["dataforge", "profiles"], cancellation.Token));
        Assert.Equal(0, handlerCreations);
    }

    [Fact]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1051", Justification = "The bounded task waits verify operation-token cancellation and cleanup.")]
    public async Task DevOpsWorkbench_CancellationStopsActiveDataForgeChildAndRetainsEvidence()
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(), "workbench-cancellation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        var checkout = RepositoryPaths.Resolve(null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);
        var processRunner = new GatedDataForgeProcessRunner(GetProcessTestChildPath(), temporaryRoot);
        using var commandOutput = new StringWriter();
        using var commandError = new StringWriter();
        var operations = new ManagedWorkbenchOperations(
            cancellationToken => new ManagedCommandHandler(commandOutput, commandError, cancellationToken, () => processRunner),
            checkout,
            commandOutput,
            commandError);
        using var cancellation = new CancellationTokenSource();
        var console = new TestConsole().Interactive();
        string outputPath = Path.Combine(temporaryRoot, "dataset");
        PrepareDataForgeGenerationWorkbench(console, outputPath);
        var workbench = new DevOpsWorkbench(
            console,
            () => new ConfigurationLoader(checkout.RepositoryRoot).Load(),
            checkout.RepositoryRoot,
            operations,
            cancellation.Token);
        Task<int> workbenchRun = Task.Run(workbench.Run);

        try
        {
            string readyPath = processRunner.GetReadyPath(1);
            Assert.True(SpinWait.SpinUntil(() => File.Exists(readyPath), TimeSpan.FromSeconds(20)),
                "The DataForge child did not reach its active wait state.");
            int childProcessId = int.Parse(await File.ReadAllTextAsync(readyPath, TestContext.Current.CancellationToken), System.Globalization.CultureInfo.InvariantCulture);

            await cancellation.CancelAsync();

            Assert.Equal(130, await workbenchRun.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.Contains("cancelled", commandError.ToString(), StringComparison.OrdinalIgnoreCase);
            string cancelledRunDirectory = Assert.Single(processRunner.RunDirectories);
            Assert.Contains(cancelledRunDirectory, commandError.ToString(), StringComparison.Ordinal);
            using (var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(cancelledRunDirectory, "run.json"), TestContext.Current.CancellationToken)))
            {
                Assert.Equal("cancelled", manifest.RootElement.GetProperty("state").GetString());
                Assert.Empty(manifest.RootElement.GetProperty("childExitCodes").EnumerateArray());
            }
            Assert.True(SpinWait.SpinUntil(() => !IsProcessRunning(childProcessId), TimeSpan.FromSeconds(5)),
                $"The cancelled DataForge child process {childProcessId} remained alive.");

            processRunner.ReleaseSubsequentChildren = true;
            using var completedCancellation = new CancellationTokenSource();
            int completedExitCode = operations.RunCommand(
                ["dataforge", "generate", "-Profile", "leancorpus-search", "-Version", "1", "-Seed", "42", "-Count", "8", "-Output", outputPath],
                completedCancellation.Token);
            Assert.Equal(0, completedExitCode);
            string completedRunDirectory = processRunner.RunDirectories.Last();
            await completedCancellation.CancelAsync();
            using (var manifest = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(completedRunDirectory, "run.json"), TestContext.Current.CancellationToken)))
            {
                Assert.Equal("succeeded", manifest.RootElement.GetProperty("state").GetString());
                Assert.Equal(0, manifest.RootElement.GetProperty("childExitCodes")[0].GetInt32());
            }
            Assert.Equal(2, processRunner.RunDirectories.Count);
        }
        finally
        {
            if (!workbenchRun.IsCompleted)
                await cancellation.CancelAsync();
            try { await workbenchRun.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) { }
            if (Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static string[] CaptureDataForgeGenerationArguments(
        string profile,
        string? mode,
        string? language,
        string? allowLarge,
        IReadOnlyList<string> promptValues)
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "DataForge", MainMenu);
        Choose(console, "Generate a dataset", DataForgeActions);
        Choose(console, profile, DataForgeProfiles);
        if (mode is not null)
            Choose(console, mode, StressModes);
        if (language is not null)
            Choose(console, language, ["Back", .. RowlesTextMultilingualProfile.Languages]);
        int valuesBeforeConsent = allowLarge is null ? promptValues.Count : promptValues.Count - 1;
        for (int index = 0; index < valuesBeforeConsent; index++)
            console.Input.PushTextWithEnter(promptValues[index]);
        if (allowLarge is not null)
            Choose(console, allowLarge, LargeWorkloadChoices);
        for (int index = valuesBeforeConsent; index < promptValues.Count; index++)
            console.Input.PushTextWithEnter(promptValues[index]);
        Choose(console, "Run command", ["Back", "Run command"]);
        Choose(console, "Exit", MainMenu);

        Assert.Equal(0, CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run());
        return Assert.Single(operations.Calls);
    }

    [Fact]
    public void BenchmarkSelection_PreservesConfiguredBritishAreaAndGroupNames()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Benchmarks", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");
        Choose(console, "Run a benchmark suite", "Back", "Run a benchmark suite", "Run a sustained workload", "Remote benchmark",
            "Qualification summary", "Explore benchmark catalogue");
        Choose(console, "core", "Back", "all", "core", "query", "text");
        Choose(console, "fast", "Back", "fast");
        Choose(console, "net10.0", "Back", "net10.0", "net11.0");
        Choose(console, "Serialisation", "Back", "All areas", "Serialisation");
        Choose(console, "Serialisation", "Back", "All groups", "Serialisation");
        Choose(console, "Dry smoke", "Back", "Dry smoke", "Full selected strategy");
        Choose(console, "Run benchmark", "Back", "Run benchmark");
        Choose(console, "Exit", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        var benchmark = Assert.Single(operations.Calls);
        Assert.Equal(new[] { "benchmark", "--suite", "core", "--strat", "fast", "--framework", "net10.0",
            "--area", "Serialisation", "--group", "Serialisation", "--dry" }, benchmark);
    }

    [Fact]
    public void WorkloadSelectionPassesScenarioAndOptionsToManagedCommand()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Benchmarks", MainMenu);
        Choose(console, "Run a sustained workload", "Back", "Run a benchmark suite", "Run a sustained workload", "Remote benchmark",
            "Qualification summary", "Explore benchmark catalogue");
        Choose(console, "search-readonly", "Back", "ingest-throughput", "search-readonly", "ingest-search-refresh");
        Choose(console, "both", "Back", "both", "core", "text");
        Choose(console, "net10.0", "Back", "net10.0", "net11.0");
        console.Input.PushKey(ConsoleKey.Enter); // Use the dataset default
        Choose(console, "Dry smoke", "Back", "Dry smoke", "Bounded workload");
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "benchmark", "workload", "search-readonly", "--engine", "both", "--framework", "net10.0",
            "--dataset", "leancorpus-search", "--dry" }, Assert.Single(operations.Calls));
    }

    [Fact]
    public void RemoteBenchmarkSelectionPassesTokenArgumentsWithoutShellParsing()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Benchmarks", MainMenu);
        Choose(console, "Remote benchmark", "Back", "Run a benchmark suite", "Run a sustained workload", "Remote benchmark",
            "Qualification summary", "Explore benchmark catalogue");
        Choose(console, "Start", "Back", "Start", "Check status", "Cancel");
        console.Input.PushTextWithEnter("tester@example");
        console.Input.PushTextWithEnter("/srv/lean corpus");
        console.Input.PushTextWithEnter("issue-97");
        console.Input.PushTextWithEnter("issue-97-session");
        console.Input.PushTextWithEnter("--suite");
        console.Input.PushTextWithEnter("query");
        console.Input.PushTextWithEnter("");
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "benchmark", "remote", "start", "--host", "tester@example", "--path", "/srv/lean corpus",
            "--branch", "issue-97", "--session", "issue-97-session", "--", "--suite", "query" }, Assert.Single(operations.Calls));
    }

    [Fact]
    public void QualificationSummarySelectionCanPrepareAPlan()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Benchmarks", MainMenu);
        Choose(console, "Qualification summary", "Back", "Run a benchmark suite", "Run a sustained workload", "Remote benchmark",
            "Qualification summary", "Explore benchmark catalogue");
        Choose(console, "Plan a summary", "Back", "Plan a summary", "Summarise collected runs");
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter);
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "benchmark", "qualification-summary", "--reports", "collected-run-reports.json",
            "--output", "qualification-summary.json", "--plan-only" }, Assert.Single(operations.Calls));
    }

    [Fact]
    public void BenchmarkCatalogueSelectionUsesManagedListRoute()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Benchmarks", MainMenu);
        Choose(console, "Explore benchmark catalogue", "Back", "Run a benchmark suite", "Run a sustained workload", "Remote benchmark",
            "Qualification summary", "Explore benchmark catalogue");
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "benchmark", "--list" }, Assert.Single(operations.Calls));
    }

    [Theory]
    [InlineData("Inspect a generated dataset", "inspect")]
    [InlineData("Verify a generated dataset", "verify")]
    [InlineData("Reproduce a generated dataset", "reproduce")]
    public void DataForgeDatasetActionsPassDatasetPathAsOneToken(string menuChoice, string operation)
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "DataForge", MainMenu);
        Choose(console, menuChoice, "Back", "Preview release qualification", "Run release qualification", "Generate a dataset",
            "Reference dataset", "List profiles", "Inspect a generated dataset", "Verify a generated dataset",
            "Reproduce a generated dataset", "Enter token arguments");
        console.Input.PushTextWithEnter("/datasets/my corpus/manifest.json");
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "dataforge", operation, "/datasets/my corpus/manifest.json" }, Assert.Single(operations.Calls));
    }

    [Fact]
    public void CleanSelectionUsesAnOwnedTargetAndConfirmation()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Build and package", MainMenu);
        Choose(console, "Clean artefacts", "Back", "Build a project", "Clean artefacts", "Pack packages", "Distribute archive");
        Choose(console, "test", "Back", "default", "test", "build", "coverage", "docs", "diagnostics", "benchmark", "package", "cache", "all");
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "clean", "test" }, Assert.Single(operations.Calls));
    }

    [Fact]
    public void ReportSelectionPassesGroupAndReportOptions()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Tests and quality", MainMenu);
        Choose(console, "Repository report", "Back", "Run tests", "Coverage", "AOT smoke", "Repository report");
        Choose(console, "code", "Back", "all", "git", "files", "code");
        console.Input.PushKey(ConsoleKey.Enter); // Default top count
        console.Input.PushTextWithEnter("src/core/**/*.cs");
        Choose(console, "Strict", "Back", "Normal", "Strict");
        Choose(console, "JSON", "Back", "Terminal", "JSON");
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "report", "code", "--top", "10", "--path", "src/core/**/*.cs", "--strict", "--json" },
            Assert.Single(operations.Calls));
    }

    [Fact]
    public void Run_ReturnsManagedCommandFailureAfterReturningToHome()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations { RunExitCode = 7 };
        Choose(console, "Server and diagnostics", MainMenu);
        Choose(console, "Process list", "Back", "Start Community Server", "Process list", "Trace a process", "Dump a process");
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(7, exitCode);
        Assert.Equal(new[] { "diagnostics", "ps" }, Assert.Single(operations.Calls));
    }

    [Fact]
    public void Run_PropagatesCancellationFromManagedCommand()
    {
        var console = new TestConsole().Interactive();
        using var cancellation = new CancellationTokenSource();
        var operations = new RecordingWorkbenchOperations { CancelDuringRun = cancellation };
        Choose(console, "Server and diagnostics", MainMenu);
        Choose(console, "Process list", "Back", "Start Community Server", "Process list", "Trace a process", "Dump a process");
        Choose(console, "Run command", "Back", "Run command");

        int exitCode = CreateWorkbench(console, operations, cancellation.Token).Run();

        Assert.Equal(130, exitCode);
        Assert.Contains("cancelled", console.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Single(operations.Calls);
    }

    [Fact]
    public void RepositoryStatusReportsUnavailableMetadataWithoutBreakingTheWorkbench()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations { MetadataFailure = "git metadata unavailable" };
        Choose(console, "Repository status", MainMenu);
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Contains("Repository metadata unavailable: git metadata unavailable", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void RunSupportsNarrowInteractiveTerminals()
    {
        var console = new TestConsole().Interactive().Width(20);
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Empty(operations.Calls);
    }

    [Fact]
    public void Observe_ShowsRunDirectoryAndFullArtifactReferences()
    {
        var console = new TestConsole().Interactive();
        var manifest = new RunManifest
        {
            SchemaVersion = RunManifest.CurrentSchemaVersion,
            RunId = Guid.Parse("eb0cf3b0-4d32-43b5-b69a-7e6a8eefbcb3"),
            Command = "./devops test --suite core",
            State = RunState.Failed,
            StartedUtc = DateTimeOffset.Parse("2026-10-22T12:30:00Z"),
            CompletedUtc = DateTimeOffset.Parse("2026-10-22T12:31:00Z"),
            SourceCommit = "3718a8c5704ff020b11ebcc68ba2dff658916f4c",
            SourceDirty = false,
            Platform = "Debian GNU/Linux 13; X64",
            Inputs = ImmutableDictionary<string, string>.Empty,
            ChildExitCodes = [1],
            Artifacts = ["targets/core/results.trx", "tests.ndjson"],
            Failure = "One test failed."
        };
        var operations = new RecordingWorkbenchOperations
        {
            Runs = [new WorkbenchRun("test", "/repo/artifacts/test/runs/eb0cf3b0-4d32-43b5-b69a-7e6a8eefbcb3", manifest)]
        };
        Choose(console, "Recent runs", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");
        Choose(console, "All categories", "All categories", "test");
        Choose(console, "test | Failed | 2026-10-22 12:30 UTC | 00:01:00 | eb0cf3b0-4d32-43b5-b69a-7e6a8eefbcb3", "Back",
            "test | Failed | 2026-10-22 12:30 UTC | 00:01:00 | eb0cf3b0-4d32-43b5-b69a-7e6a8eefbcb3");
        Choose(console, "Exit", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Contains("/repo/artifacts/test/runs/eb0cf3b0-4d32-43b5-b69a-7e6a8eefbcb3", console.Output);
        Assert.Contains("targets/core/results.trx", console.Output);
        Assert.Contains("tests.ndjson", console.Output);
        Assert.Contains("Elapsed: 00:01:00", console.Output);
        Assert.Contains("Recent DevOps runs", console.Output);
        Assert.Contains("Category", console.Output);
        Assert.Contains("Run ID", console.Output);
        Assert.Contains("Status", console.Output);
        Assert.Contains("eb0cf3b0", console.Output);
    }

    [Fact]
    public void ObserveCanNavigateRecentRunsOnANarrowTerminal()
    {
        var console = new TestConsole().Interactive().Width(32);
        var manifest = new RunManifest
        {
            SchemaVersion = RunManifest.CurrentSchemaVersion,
            RunId = Guid.Parse("eb0cf3b0-4d32-43b5-b69a-7e6a8eefbcb3"),
            Command = "./devops test --suite core",
            State = RunState.Failed,
            StartedUtc = DateTimeOffset.Parse("2026-10-22T12:30:00Z"),
            CompletedUtc = DateTimeOffset.Parse("2026-10-22T12:31:00Z"),
            SourceDirty = false,
            Platform = "Debian GNU/Linux 13; X64",
            Inputs = ImmutableDictionary<string, string>.Empty,
            ChildExitCodes = [1],
            Artifacts = ["targets/core/results.trx"]
        };
        var operations = new RecordingWorkbenchOperations
        {
            Runs = [new WorkbenchRun("test", "/repo/artifacts/test/runs/eb0cf3b0-4d32-43b5-b69a-7e6a8eefbcb3", manifest)]
        };
        Choose(console, "Recent runs", MainMenu);
        Choose(console, "All categories", ["All categories", "test"]);
        Choose(console, "test | Failed | 2026-10-22 12:30 UTC | 00:01:00 | eb0cf3b0-4d32-43b5-b69a-7e6a8eefbcb3",
            ["Back", "test | Failed | 2026-10-22 12:30 UTC | 00:01:00 | eb0cf3b0-4d32-43b5-b69a-7e6a8eefbcb3"]);
        Choose(console, "Exit", MainMenu);

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void RepositoryStatusLoadsOptionalMetadataOnlyAfterSelection()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Repository status", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");
        Choose(console, "Exit", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(1, operations.RepositoryMetadataReads);
        Assert.Contains("Branch: test", console.Output, StringComparison.Ordinal);
        Assert.Contains("Commit: abc12345", console.Output, StringComparison.Ordinal);
        Assert.Contains("Dirty: no", console.Output, StringComparison.Ordinal);
        Assert.Contains("SDK requested: 11.0.100-preview", console.Output, StringComparison.Ordinal);
        Assert.Contains("SDK selected: 11.0.100-preview", console.Output, StringComparison.Ordinal);
        Assert.Contains("SDK roll-forward: latestPatch", console.Output, StringComparison.Ordinal);
        Assert.Contains("Configuration: valid", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsTraceSelectionUsesManagedCommandTokens()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        Choose(console, "Server and diagnostics", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");
        Choose(console, "Trace a process", "Back", "Start Community Server", "Process list", "Trace a process", "Dump a process");
        console.Input.PushTextWithEnter("1234");
        Choose(console, "Run command", "Back", "Run command");
        Choose(console, "Exit", "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
            "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs");

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "diagnostics", "trace", "--pid", "1234" }, Assert.Single(operations.Calls));
    }

    private static DevOpsWorkbench CreateWorkbench(
        TestConsole console,
        IWorkbenchOperations operations,
        CancellationToken cancellationToken,
        Action? onConfigurationLoad = null)
    {
        var configuration = new DevOpsConfiguration(
            ImmutableDictionary<string, TestSuiteConfiguration>.Empty.Add(
                "core", new TestSuiteConfiguration("core", "src/devops/tests/Rowles.LeanCorpus.Tests.Core/Rowles.LeanCorpus.Tests.Core.csproj", "mtp", ["net10.0", "net11.0"], "net11.0", true, [], null, null, null)),
            ImmutableDictionary<string, CodeAreaConfiguration>.Empty,
            ImmutableDictionary<string, BenchmarkProjectConfiguration>.Empty,
            ImmutableDictionary<string, BenchmarkSuiteConfiguration>.Empty.Add(
                "query", new BenchmarkSuiteConfiguration("query", "Query", "core", [], [], ["net10.0", "net11.0"], [], false, false, false, false, false, null, null)),
            ImmutableDictionary<string, BenchmarkStrategyConfiguration>.Empty.Add(
                "fast", new BenchmarkStrategyConfiguration("Fast smoke", 100, ["--job", "Dry"])),
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, BenchmarkGroupConfiguration>.Empty.Add(
                "Serialisation", new BenchmarkGroupConfiguration("Serialisation", ["src/core/Rowles.LeanCorpus/Serialisation/**"], "core", ["SchemaAndJsonBenchmarks"])));
        return new DevOpsWorkbench(console, () =>
        {
            onConfigurationLoad?.Invoke();
            return configuration;
        }, "/repo", operations, cancellationToken);
    }

    private static void PrepareDataForgeGenerationWorkbench(TestConsole console, string outputPath)
    {
        Choose(console, "DataForge", MainMenu);
        Choose(console, "Generate a dataset", DataForgeActions);
        Choose(console, "leancorpus-search", DataForgeProfiles);
        console.Input.PushTextWithEnter("1");
        console.Input.PushTextWithEnter("42");
        console.Input.PushTextWithEnter("8");
        console.Input.PushTextWithEnter(outputPath);
        Choose(console, "Run command", ["Back", "Run command"]);
    }

    private static string GetProcessTestChildPath()
    {
        string executableName = OperatingSystem.IsWindows() ? "devops-process-test-child.exe" : "devops-process-test-child";
        string configuration = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "Rowles.LeanCorpus.DevOps.ProcessTestChild", configuration, executableName));
    }

    private static bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static readonly string[] MainMenu =
    [
        "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge", "Fixtures and corpora",
        "Documentation", "Server and diagnostics", "Configuration", "Repository status", "Recent runs"
    ];

    private static readonly string[] DataForgeActions =
    [
        "Back", "Preview release qualification", "Run release qualification", "Generate a dataset", "Reference dataset",
        "List profiles", "Inspect a generated dataset", "Verify a generated dataset", "Reproduce a generated dataset",
        "Enter token arguments"
    ];

    private static readonly string[] DataForgeProfiles =
    [
        "Back", "leancorpus-search", "leancorpus-vector", "leancorpus-hybrid",
        "leancorpus-stress", "rowles-text-multilingual", "leancorpus-e2e"
    ];

    private static readonly string[] StressModes = ["Back", "search", "vector", "hybrid"];
    private static readonly string[] LargeWorkloadChoices = ["Back", "No", "Yes, allow large"];

    private static void Choose(TestConsole console, string selected, params string[] choices)
    {
        int selectedIndex = Array.IndexOf(choices, selected);
        Assert.True(selectedIndex >= 0, $"Choice '{selected}' was not present in the supplied choices.");
        for (int index = 0; index < selectedIndex; index++)
            console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
    }

    private sealed class RecordingWorkbenchOperations : IWorkbenchOperations
    {
        public List<string[]> Calls { get; } = [];
        public IReadOnlyList<WorkbenchRun> Runs { get; init; } = [];
        public int RepositoryMetadataReads { get; private set; }
        public int RunExitCode { get; init; }
        public CancellationTokenSource? CancelDuringRun { get; init; }
        public string? MetadataFailure { get; init; }

        public int RunCommand(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add([.. arguments]);
            CancelDuringRun?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return RunExitCode;
        }

        public string ReadRepositoryMetadata(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RepositoryMetadataReads++;
            if (MetadataFailure is not null)
                throw new IOException(MetadataFailure);
            return "Branch: test\nCommit: abc12345\nDirty: no\nSDK requested: 11.0.100-preview.7\nSDK selected: 11.0.100-preview.7\nSDK roll-forward: latestPatch\nConfiguration: valid";
        }

        public IReadOnlyList<WorkbenchRun> ReadRecentRuns() => Runs;
    }

    private sealed class RecordingCommandHandler : ICommandHandler
    {
        public int Build(BuildCommandOptions options) => 0;
        public int Clean(CleanCommandOptions options) => 0;
        public int Docs(DocsCommandOptions options) => 0;
        public int StartServer(ServerStartCommandOptions options) => 0;
        public int Report(ReportCommandOptions options) => 0;
        public int Coverage(CoverageCommandOptions options) => 0;
        public int Pack(PackCommandOptions options) => 0;
        public int Distribute(DistributeCommandOptions options) => 0;
        public int Data(DataCommandOptions options) => 0;
        public int DataFixtures(HistoricalFixturesCommandOptions options) => 0;
        public int DataForge(DataForgeCommandOptions options) => 0;
        public int Diagnostics(DiagnosticsCommandOptions options) => 0;
        public int Benchmark(BenchmarkCommandOptions options) => 0;
        public int BenchmarkRemote(BenchmarkRemoteCommandOptions options) => 0;
        public int BenchmarkQualificationSummary(BenchmarkQualificationSummaryCommandOptions options) => 0;
        public int Test(TestCommandOptions options) => 0;
        public int Ui(UiCommandOptions options) => 0;
        public int ShowConfiguration(ConfigSummaryCommandOptions options) => 0;
        public int ValidateConfiguration(ConfigValidateCommandOptions options) => 0;
        public int BackupRestoreMatrix(BackupRestoreMatrixCommandOptions options) => 0;
    }

    private sealed class GatedDataForgeProcessRunner(string childPath, string temporaryRoot) : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();
        private readonly List<string> _runDirectories = [];

        public IReadOnlyList<string> RunDirectories
        {
            get
            {
                lock (_runDirectories)
                    return [.. _runDirectories];
            }
        }

        public bool ReleaseSubsequentChildren { get; set; }

        public string GetReadyPath(int sequence) => Path.Combine(temporaryRoot, $"child-{sequence}.pid");

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Arguments.Count >= 3 && request.Arguments[0] == "run" &&
                request.Arguments.Any(static argument => argument.EndsWith("Rowles.DataForge.Tool.csproj", StringComparison.Ordinal)))
            {
                int sequence;
                lock (_runDirectories)
                {
                    sequence = _runDirectories.Count + 1;
                    _runDirectories.Add(Path.GetDirectoryName(request.StandardOutputLogPath)!);
                }

                string readyPath = GetReadyPath(sequence);
                string releasePath = Path.Combine(temporaryRoot, $"child-{sequence}.release");
                if (sequence > 1 && ReleaseSubsequentChildren)
                    await File.WriteAllTextAsync(releasePath, "release", cancellationToken);
                return await _inner.RunAsync(request with
                {
                    ExecutablePath = childPath,
                    Arguments = ["wait-for-release", readyPath, releasePath]
                }, cancellationToken);
            }

            return await _inner.RunAsync(request, cancellationToken);
        }
    }
}
