using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;
using Spectre.Console;

namespace Rowles.LeanCorpus.DevOps.Commands;

internal sealed record WorkbenchTestSelection(string Suite, string Framework, string Configuration, string Profile);

internal sealed record WorkbenchRun(string Category, string Directory, RunManifest Manifest);

internal interface IWorkbenchOperations
{
    int RunTests(WorkbenchTestSelection selection);

    int RunBenchmark(BenchmarkCommandOptions options);

    int RunDataForge(IReadOnlyList<string> arguments);

    IReadOnlyList<WorkbenchRun> ReadRecentRuns();
}

internal sealed class DevOpsWorkbench(
    IAnsiConsole console,
    DevOpsConfiguration configuration,
    IWorkbenchOperations operations,
    CancellationToken cancellationToken = default)
{
    private const int MaximumDataForgeArguments = 32;
    private const int MaximumRecentRuns = 40;
    private int _lastExitCode;
    private CancellationToken _promptCancellationToken;

    public int Run()
    {
        if (!console.Profile.Capabilities.Interactive)
        {
            console.Write(new Text("The DevOps workbench requires an interactive terminal. Run the documented DevOps commands directly when input or output is redirected."));
            console.WriteLine();
            return 2;
        }

        if (!console.Profile.Capabilities.Ansi)
        {
            console.Write(new Text("The DevOps workbench requires an ANSI-capable terminal for interactive selections. Run the documented DevOps commands directly or use an ANSI-capable terminal."));
            console.WriteLine();
            return 2;
        }

        using var interruptCancellation = new CancellationTokenSource();
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, interruptCancellation.Token);
        _promptCancellationToken = linkedCancellation.Token;
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            interruptCancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            while (true)
            {
                _promptCancellationToken.ThrowIfCancellationRequested();
                var area = Select("LeanCorpus DevOps workbench", ["Exit", "Tests", "Benchmarks", "DataForge", "Observe"]);
                switch (area)
                {
                    case "Exit":
                        return _lastExitCode;
                    case "Tests":
                        RunTests();
                        break;
                    case "Benchmarks":
                        RunBenchmark();
                        break;
                    case "DataForge":
                        RunDataForge();
                        break;
                    case "Observe":
                        ObserveRuns();
                        break;
                }

                if (_lastExitCode == 130)
                {
                    return 130;
                }
            }
        }
        catch (OperationCanceledException)
        {
            console.Write(new Text("The interactive operation was cancelled."));
            console.WriteLine();
            return 130;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            _promptCancellationToken = default;
        }
    }

    private void RunTests()
    {
        var suites = configuration.TestSuites
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => pair.Key)
            .Prepend("Back")
            .ToArray();
        var suite = Select("Select a test suite", suites);
        if (suite == "Back")
        {
            return;
        }

        var suiteConfiguration = configuration.TestSuites[suite];
        var frameworks = suiteConfiguration.Frameworks
            .Order(StringComparer.OrdinalIgnoreCase)
            .Prepend("Back")
            .ToArray();
        var framework = Select($"Framework for {suite}", frameworks);
        if (framework == "Back")
        {
            return;
        }

        var buildConfiguration = Select("Build configuration", ["Back", "Release", "Debug"]);
        if (buildConfiguration == "Back")
        {
            return;
        }

        var profile = Select("Test execution profile", ["Back", "unit", "integration", "stress"]);
        if (profile == "Back")
        {
            return;
        }

        console.Write(new Text($"Suite: {suite}\nFramework: {framework}\nConfiguration: {buildConfiguration}\nProfile: {profile}"));
        console.WriteLine();
        if (Select("Run this test selection?", ["Back", "Run tests"]) != "Run tests")
        {
            return;
        }

        _lastExitCode = operations.RunTests(new WorkbenchTestSelection(suite, framework, buildConfiguration, profile));
    }

    private void RunBenchmark()
    {
        var suites = configuration.BenchmarkSuites.Keys
            .Concat(["all", "core", "text"])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Prepend("Back")
            .ToArray();
        var suite = Select("Select a benchmark suite", suites);
        if (suite == "Back")
        {
            return;
        }

        var strategies = configuration.BenchmarkStrategies.Keys
            .Order(StringComparer.OrdinalIgnoreCase)
            .Prepend("Back")
            .ToArray();
        var strategy = Select("Benchmark strategy", strategies);
        if (strategy == "Back")
        {
            return;
        }

        var framework = Select("Target framework", ["Back", "net10.0", "net11.0"]);
        if (framework == "Back")
        {
            return;
        }

        var configuredAreas = configuration.BenchmarkGroups.Values
            .Select(static group => group.Area)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selectedArea = Select("Benchmark area", ["Back", "All areas", .. configuredAreas]);
        if (selectedArea == "Back")
        {
            return;
        }
        var area = selectedArea == "All areas" ? null : selectedArea;

        var configuredGroups = configuration.BenchmarkGroups
            .Where(pair => area is null || pair.Value.Area.Equals(area, StringComparison.OrdinalIgnoreCase))
            .Select(static pair => pair.Key)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var selectedGroup = Select("Benchmark group", ["Back", "All groups", .. configuredGroups]);
        if (selectedGroup == "Back")
        {
            return;
        }
        var group = selectedGroup == "All groups" ? null : selectedGroup;

        var dry = Select("Benchmark mode", ["Back", "Dry smoke", "Full selected strategy"]);
        if (dry == "Back")
        {
            return;
        }

        console.Write(new Text($"Suite: {suite}\nStrategy: {strategy}\nFramework: {framework}\nArea: {area ?? "all"}\nGroup: {group ?? "all"}\nMode: {dry}"));
        console.WriteLine();
        if (Select("Run this benchmark selection?", ["Back", "Run benchmark"]) != "Run benchmark")
        {
            return;
        }

        _lastExitCode = operations.RunBenchmark(new BenchmarkCommandOptions(
            suite,
            strategy,
            framework,
            DocumentCount: 0,
            Dataset: "synthetic",
            ReferencePath: null,
            Area: area,
            Group: group,
            SourceCommit: null,
            SourceRef: null,
            SourceManifest: null,
            Controlled: false,
            CorpusOnly: false,
            GcDump: false,
            PrepareData: false,
            BookCount: 200,
            Dry: dry == "Dry smoke",
            List: false,
            Arguments: []));
    }

    private void RunDataForge()
    {
        var action = Select("DataForge", ["Back", "Preview release qualification", "Run release qualification", "Run a DataForge command"]);
        if (action == "Back")
        {
            return;
        }

        IReadOnlyList<string> arguments;
        if (action == "Preview release qualification")
        {
            arguments = ["qualify", "--dry"];
        }
        else if (action == "Run release qualification")
        {
            arguments = ["qualify"];
        }
        else
        {
            arguments = ReadDataForgeArguments();
            if (arguments.Count == 0)
            {
                console.Write(new Text("No DataForge arguments were entered."));
                console.WriteLine();
                return;
            }
        }

        console.Write(new Text("DataForge arguments: " + string.Join(" ", arguments.Select(static argument => $"\"{argument}\""))));
        console.WriteLine();
        if (Select("Run this DataForge command?", ["Back", "Run command"]) != "Run command")
        {
            return;
        }

        _lastExitCode = operations.RunDataForge(arguments);
    }

    private IReadOnlyList<string> ReadDataForgeArguments()
    {
        var arguments = new List<string>();
        while (arguments.Count < MaximumDataForgeArguments)
        {
            var argument = console.PromptAsync(
                new TextPrompt<string>($"Argument {arguments.Count + 1} (blank to finish)").AllowEmpty(),
                _promptCancellationToken).GetAwaiter().GetResult();
            if (argument.Length == 0)
            {
                break;
            }
            arguments.Add(argument);
        }
        return arguments;
    }

    private void ObserveRuns()
    {
        var runs = operations.ReadRecentRuns()
            .OrderByDescending(static run => run.Manifest.StartedUtc)
            .Take(MaximumRecentRuns)
            .ToArray();
        if (runs.Length == 0)
        {
            console.Write(new Text("No structured DevOps runs were found."));
            console.WriteLine();
            return;
        }

        var labels = runs.Select(static run =>
            $"{run.Category} | {run.Manifest.State} | {run.Manifest.StartedUtc:yyyy-MM-dd HH:mm} UTC | {run.Manifest.RunId:D}")
            .Prepend("Back")
            .ToArray();
        var selected = Select("Select a run to inspect", labels);
        if (selected == "Back")
        {
            return;
        }

        var run = runs.Single(item => item.Manifest.RunId.ToString("D") == selected.Split(" | ", StringSplitOptions.None)[^1]);
        var manifest = run.Manifest;
        var lines = new List<string>
        {
            $"{run.Category} {manifest.State}: {manifest.RunId:D}",
            $"Command: {manifest.Command}",
            $"Started: {manifest.StartedUtc:O}",
            $"Completed: {manifest.CompletedUtc?.ToString("O") ?? "still running or unavailable"}",
            $"Commit: {manifest.SourceCommit ?? "unknown"} (dirty: {manifest.SourceDirty})",
            $"Platform: {manifest.Platform}",
            $"Child exit codes: {(manifest.ChildExitCodes.Count == 0 ? "none recorded" : string.Join(", ", manifest.ChildExitCodes))}",
            $"Run directory: {run.Directory}"
        };
        if (!string.IsNullOrWhiteSpace(manifest.Failure))
        {
            lines.Add($"Failure: {manifest.Failure}");
        }
        foreach (var (name, value) in manifest.Inputs.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            lines.Add($"Input {name}: {value}");
        }
        foreach (var artifact in manifest.Artifacts)
        {
            lines.Add($"Artefact: {artifact}");
        }

        console.Write(new Text(string.Join(Environment.NewLine, lines)));
        console.WriteLine();
    }

    private string Select(string title, IEnumerable<string> choices)
    {
        // There is no fixed full-screen layout, so Spectre can redraw prompts after a resize.
        // Re-read the width for each prompt and keep the visible choice list short on narrow terminals.
        var width = Math.Max(1, console.Profile.Width);
        var pageSize = Math.Clamp(width / 10, 3, 10);
        return console.PromptAsync(new SelectionPrompt<string>()
            .Title(title)
            .PageSize(pageSize)
            .AddChoices(choices)
            .UseConverter(Markup.Escape), _promptCancellationToken).GetAwaiter().GetResult();
    }
}

internal sealed class ManagedWorkbenchOperations(
    ICommandHandler commandHandler,
    CheckoutIdentity checkout,
    TextWriter output,
    TextWriter error) : IWorkbenchOperations
{
    private readonly CommandResultRenderer _renderer = new();

    public int RunTests(WorkbenchTestSelection selection)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancelHandler;
        try
        {
            var paths = new ArtifactPaths(checkout.RepositoryRoot);
            var processRunner = new ProcessRunner();
            var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
            var planner = new TestRunPlanner(configuration, new AffectedSelectionService(processRunner));
            var request = planner.PlanAsync(checkout, new TestRunOptions
            {
                Selection = new TestTargetRequest
                {
                    Suites = [selection.Suite],
                    Framework = selection.Framework,
                    FrameworkExplicit = true,
                    Configuration = selection.Configuration
                },
                Profile = selection.Profile,
                CommandLine = $"./devops ui test {selection.Suite} --framework {selection.Framework}"
            }, cancellation.Token).GetAwaiter().GetResult();
            var result = new TestExecutor(
                paths,
                new ArtifactRunStore(paths, runCategory: "test"),
                processRunner,
                new DotNetSdkResolver(processRunner),
                new GitRepositoryProvenanceReader(processRunner),
                progress: progress => output.WriteLine($"{progress.Kind}: {progress.Message ?? progress.TargetKey ?? string.Empty}"))
                .RunAsync(request, cancellation.Token).GetAwaiter().GetResult();
            var commandResult = result.CommandResult with
            {
                Message = result.CommandResult.Outcome == OutcomeKind.Success
                    ? $"Test run completed. Evidence: {result.RunDirectory}"
                    : result.CommandResult.Message,
                Details = result.CommandResult.Details is null
                    ? result.RunDirectory
                    : result.CommandResult.Details + Environment.NewLine + result.RunDirectory
            };
            return _renderer.Render(commandResult, output, error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.Cancelled, "Test run was cancelled."), output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Could not run the managed test selection.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int RunBenchmark(BenchmarkCommandOptions options)
        => commandHandler.Benchmark(options with { RepositoryPath = checkout.RepositoryRoot });

    public int RunDataForge(IReadOnlyList<string> arguments)
        => commandHandler.DataForge(new DataForgeCommandOptions(arguments, checkout.RepositoryRoot));

    public IReadOnlyList<WorkbenchRun> ReadRecentRuns()
    {
        var paths = new ArtifactPaths(checkout.RepositoryRoot);
        var candidates = new List<(string Category, Guid RunId, DateTime ModifiedUtc)>();
        foreach (var category in ArtifactPaths.RunCategories)
        {
            var root = paths.GetRunRoot(category);
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (!Guid.TryParse(Path.GetFileName(directory), out var runId))
                {
                    continue;
                }

                var runPath = Path.Combine(directory, "run.json");
                var manifestPath = File.Exists(runPath) ? runPath : Path.Combine(directory, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    candidates.Add((category, runId, File.GetLastWriteTimeUtc(manifestPath)));
                }
            }
        }

        var runs = new List<WorkbenchRun>();
        foreach (var candidate in candidates
                     .OrderByDescending(static item => item.ModifiedUtc)
                     .Take(40))
        {
            var store = new ArtifactRunStore(paths, runCategory: candidate.Category);
            var manifest = store.ReadAsync(candidate.RunId).GetAwaiter().GetResult();
            if (manifest is not null)
            {
                runs.Add(new WorkbenchRun(candidate.Category,
                    paths.GetRunDirectory(candidate.Category, candidate.RunId), manifest));
            }
        }

        return runs.OrderByDescending(static run => run.Manifest.StartedUtc).ToArray();
    }
}
