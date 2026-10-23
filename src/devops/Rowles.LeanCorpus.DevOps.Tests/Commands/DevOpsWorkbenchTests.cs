using System.Collections.Immutable;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Commands;
using Spectre.Console.Testing;

namespace Rowles.LeanCorpus.DevOps.Tests.Commands;

public sealed class DevOpsWorkbenchTests
{
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
        PushSelection(console, down: 1); // Tests
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // core suite
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // net10.0
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // Release
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // unit
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // Run tests
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter); // Exit

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "test", "core", "net10.0", "Release", "unit" }, Assert.Single(operations.Calls));
    }

    [Fact]
    public void DataForgePreview_UsesTheManagedOperationWithTokenArguments()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        PushSelection(console, down: 3); // DataForge
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // Qualification preview
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // Run
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter); // Exit

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Equal(new[] { "dataforge", "qualify", "--dry" }, Assert.Single(operations.Calls));
    }

    [Fact]
    public void BenchmarkSelection_PreservesConfiguredBritishAreaAndGroupNames()
    {
        var console = new TestConsole().Interactive();
        var operations = new RecordingWorkbenchOperations();
        PushSelection(console, down: 2); // Benchmarks
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 2); // core suite
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // fast strategy
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // net10.0
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 2); // Serialisation area
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 2); // Serialisation group
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // Dry smoke
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // Run
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter); // Exit

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        var benchmark = Assert.Single(operations.Benchmarks);
        Assert.Equal("Serialisation", benchmark.Area);
        Assert.Equal("Serialisation", benchmark.Group);
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
        PushSelection(console, down: 4); // Observe
        console.Input.PushKey(ConsoleKey.Enter);
        PushSelection(console, down: 1); // Select the run
        console.Input.PushKey(ConsoleKey.Enter);
        console.Input.PushKey(ConsoleKey.Enter); // Exit

        int exitCode = CreateWorkbench(console, operations, TestContext.Current.CancellationToken).Run();

        Assert.Equal(0, exitCode);
        Assert.Contains("/repo/artifacts/test/runs/eb0cf3b0-4d32-43b5-b69a-7e6a8eefbcb3", console.Output);
        Assert.Contains("targets/core/results.trx", console.Output);
        Assert.Contains("tests.ndjson", console.Output);
    }

    private static DevOpsWorkbench CreateWorkbench(
        TestConsole console,
        RecordingWorkbenchOperations operations,
        CancellationToken cancellationToken)
        => new(console, new DevOpsConfiguration(
            ImmutableDictionary<string, TestSuiteConfiguration>.Empty.Add(
                "core", new TestSuiteConfiguration("core", "src/devops/Rowles.LeanCorpus.Tests.Core/Rowles.LeanCorpus.Tests.Core.csproj", "mtp", ["net10.0", "net11.0"], "net11.0", true, [], null, null, null)),
            ImmutableDictionary<string, CodeAreaConfiguration>.Empty,
            ImmutableDictionary<string, string>.Empty.Add("query", "query"),
            ImmutableDictionary<string, BenchmarkStrategyConfiguration>.Empty.Add(
                "fast", new BenchmarkStrategyConfiguration("Fast smoke", 100, ["--job", "Dry"])),
            ImmutableDictionary<string, ImmutableArray<string>>.Empty,
            ImmutableDictionary<string, BenchmarkGroupConfiguration>.Empty.Add(
                "Serialisation", new BenchmarkGroupConfiguration("Serialisation", ["src/core/Rowles.LeanCorpus/Serialisation/**"], "core", ["SchemaAndJsonBenchmarks"]))), operations, cancellationToken);

    private static void PushSelection(TestConsole console, int down)
    {
        for (int index = 0; index < down; index++)
            console.Input.PushKey(ConsoleKey.DownArrow);
    }

    private sealed class RecordingWorkbenchOperations : IWorkbenchOperations
    {
        public List<string[]> Calls { get; } = [];
        public List<BenchmarkCommandOptions> Benchmarks { get; } = [];
        public IReadOnlyList<WorkbenchRun> Runs { get; init; } = [];

        public int RunTests(WorkbenchTestSelection selection)
        {
            Calls.Add(["test", selection.Suite, selection.Framework, selection.Configuration, selection.Profile]);
            return 0;
        }

        public int RunBenchmark(BenchmarkCommandOptions options)
        {
            Benchmarks.Add(options);
            Calls.Add(["benchmark", options.Suite, options.Strategy, options.Framework, options.Dry.ToString()]);
            return 0;
        }

        public int RunDataForge(IReadOnlyList<string> arguments)
        {
            Calls.Add(["dataforge", .. arguments]);
            return 0;
        }

        public IReadOnlyList<WorkbenchRun> ReadRecentRuns() => Runs;
    }
}
