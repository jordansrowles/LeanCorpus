using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;
using Rowles.DataForge.Workloads;
using Spectre.Console;

namespace Rowles.LeanCorpus.DevOps.Commands;

internal sealed record WorkbenchRun(string Category, string Directory, RunManifest Manifest);

internal interface IWorkbenchOperations
{
    int RunCommand(IReadOnlyList<string> arguments, CancellationToken cancellationToken);

    string ReadRepositoryMetadata(CancellationToken cancellationToken);

    IReadOnlyList<WorkbenchRun> ReadRecentRuns();
}

internal sealed class DevOpsWorkbench(
    IAnsiConsole console,
    Func<DevOpsConfiguration> configurationFactory,
    string repositoryRoot,
    IWorkbenchOperations operations,
    CancellationToken cancellationToken = default)
{
    private const int MaximumDataForgeArguments = 32;
    private const int MaximumRecentRuns = 40;
    private int _lastExitCode;
    private CancellationToken _promptCancellationToken;
    private DevOpsConfiguration? _configuration;
    private string? _repositoryMetadata;

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
                WriteHeader();
                var area = Select("LeanCorpus DevOps workbench", [
                    "Exit", "Build and package", "Tests and quality", "Benchmarks", "DataForge",
                    "Fixtures and corpora", "Documentation", "Server and diagnostics", "Configuration",
                    "Repository status", "Recent runs"]);
                switch (area)
                {
                    case "Exit":
                        return _lastExitCode;
                    case "Build and package":
                        BuildAndPackage();
                        break;
                    case "Tests and quality":
                        TestsAndQuality();
                        break;
                    case "Benchmarks":
                        RunBenchmark();
                        break;
                    case "DataForge":
                        RunDataForge();
                        break;
                    case "Fixtures and corpora":
                        FixturesAndCorpora();
                        break;
                    case "Documentation":
                        Documentation();
                        break;
                    case "Server and diagnostics":
                        ServerAndDiagnostics();
                        break;
                    case "Configuration":
                        Configuration();
                        break;
                    case "Repository status":
                        try
                        {
                            _repositoryMetadata = operations.ReadRepositoryMetadata(_promptCancellationToken);
                        }
                        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
                        {
                            _repositoryMetadata = $"Repository metadata unavailable: {exception.Message}";
                        }
                        console.Write(new Text(_repositoryMetadata));
                        console.WriteLine();
                        break;
                    case "Recent runs":
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

    private DevOpsConfiguration ConfigurationData => _configuration ??= configurationFactory();

    private void WriteHeader()
    {
        string devOpsVersion = typeof(DevOpsWorkbench).Assembly.GetName().Version?.ToString(3) ?? "unknown";
        string coreVersion = ReadCorePackageVersion();
        string heading = $"LeanCorpus DevOps v{Markup.Escape(devOpsVersion)} | LeanCorpus {Markup.Escape(coreVersion)} | Checkout: {Markup.Escape(repositoryRoot)}";
        console.Write(new Rule(heading).LeftJustified());
        console.WriteLine();
        if (_repositoryMetadata is not null)
        {
            console.Write(new Text(_repositoryMetadata));
            console.WriteLine();
        }
        else
        {
            console.Write(new Text("Repository branch, revision, dirty state and requested/selected SDK are not checked until Repository status is selected."));
            console.WriteLine();
        }
    }

    private string ReadCorePackageVersion()
    {
        try
        {
            var project = System.Xml.Linq.XDocument.Load(Path.Combine(repositoryRoot,
                "src", "core", "Rowles.LeanCorpus", "Rowles.LeanCorpus.csproj"));
            return project.Root?.Elements("PropertyGroup").SelectMany(static group => group.Elements("Version"))
                .Select(static version => version.Value)
                .FirstOrDefault(static version => !string.IsNullOrWhiteSpace(version)) ?? "unknown";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return "unknown";
        }
    }

    private void RunTests()
    {
        var suites = ConfigurationData.TestSuites
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(static pair => pair.Key)
            .Prepend("Back")
            .ToArray();
        var suite = Select("Select a test suite", suites);
        if (suite == "Back")
        {
            return;
        }

        var suiteConfiguration = ConfigurationData.TestSuites[suite];
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

        _lastExitCode = operations.RunCommand(
            ["test", suite, "--framework", framework, "--configuration", buildConfiguration, "--profile", profile],
            _promptCancellationToken);
    }

    private void RunBenchmark()
    {
        string action = Select("Benchmarks", [
            "Back", "Run a benchmark suite", "Run a sustained workload", "Remote benchmark",
            "Qualification summary", "Explore benchmark catalogue"]);
        switch (action)
        {
            case "Run a benchmark suite":
                RunBenchmarkSuite();
                break;
            case "Run a sustained workload":
                RunBenchmarkWorkload();
                break;
            case "Remote benchmark":
                RunRemoteBenchmark();
                break;
            case "Qualification summary":
                RunBenchmarkQualificationSummary();
                break;
            case "Explore benchmark catalogue":
                ExecuteWithConfirmation(["benchmark", "--list"], "List registered benchmark suites and groups");
                break;
        }
    }

    private void RunBenchmarkSuite()
    {
        var suites = ConfigurationData.BenchmarkSuites.Keys
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

        var strategies = ConfigurationData.BenchmarkStrategies.Keys
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

        var configuredAreas = ConfigurationData.BenchmarkGroups.Values
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

        var configuredGroups = ConfigurationData.BenchmarkGroups
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

        var arguments = new List<string> { "benchmark", "--suite", suite, "--strat", strategy, "--framework", framework };
        if (area is not null)
            arguments.AddRange(["--area", area]);
        if (group is not null)
            arguments.AddRange(["--group", group]);
        if (dry == "Dry smoke")
            arguments.Add("--dry");
        _lastExitCode = operations.RunCommand(arguments, _promptCancellationToken);
    }

    private void RunBenchmarkWorkload()
    {
        string scenario = Select("Sustained workload scenario", [
            "Back", "ingest-throughput", "search-readonly", "ingest-search-refresh"]);
        if (scenario == "Back") return;
        string engine = Select("Workload engine", ["Back", "both", "core", "text"]);
        if (engine == "Back") return;
        string framework = Select("Target framework", ["Back", "net10.0", "net11.0"]);
        if (framework == "Back") return;
        string dataset = ReadValue("Benchmark dataset ID", "leancorpus-search");
        string mode = Select("Workload mode", ["Back", "Dry smoke", "Bounded workload"]);
        if (mode == "Back") return;

        var arguments = new List<string>
        {
            "benchmark", "workload", scenario,
            "--engine", engine,
            "--framework", framework,
            "--dataset", dataset
        };
        if (mode == "Dry smoke") arguments.Add("--dry");
        ExecuteWithConfirmation(arguments, $"Sustained workload: {scenario}, {engine}, {framework}, {mode}");
    }

    private void RunRemoteBenchmark()
    {
        string action = Select("Remote benchmark", ["Back", "Start", "Check status", "Cancel"]);
        if (action == "Back") return;

        var arguments = new List<string> { "benchmark", "remote", action switch
        {
            "Start" => "start",
            "Check status" => "status",
            "Cancel" => "cancel",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        }};
        if (action == "Start")
        {
            AddOptionalPair(arguments, "--host", ReadOptionalValue("Remote host (user@host)"));
            AddOptionalPair(arguments, "--path", ReadOptionalValue("Remote checkout path"));
            AddOptionalPair(arguments, "--branch", ReadOptionalValue("Remote branch"));
            AddOptionalPair(arguments, "--session", ReadOptionalValue("Remote session name"));
            string[] benchmarkArguments = ReadTokenArguments("Benchmark argument");
            if (benchmarkArguments.Length > 0) arguments.AddRange(["--", .. benchmarkArguments]);
        }
        else
        {
            string runId = ReadValue("Remote benchmark run ID", "");
            if (runId.Length == 0) return;
            arguments.AddRange(["--run-id", runId]);
        }
        ExecuteWithConfirmation(arguments, $"Remote benchmark: {action}");
    }

    private void RunBenchmarkQualificationSummary()
    {
        string mode = Select("Qualification summary", ["Back", "Plan a summary", "Summarise collected runs"]);
        if (mode == "Back") return;
        string reports = ReadValue("Collected run reports path", "collected-run-reports.json");
        string output = ReadValue("Summary output path", "qualification-summary.json");
        var arguments = new List<string>
        {
            "benchmark", "qualification-summary", "--reports", reports, "--output", output
        };
        if (mode == "Plan a summary")
        {
            arguments.Add("--plan-only");
        }
        else
        {
            string expectedRunCount = ReadValue("Expected report count", "");
            if (expectedRunCount.Length == 0) return;
            arguments.AddRange(["--expected-run-count", expectedRunCount]);
        }
        ExecuteWithConfirmation(arguments, $"Benchmark qualification summary: {mode}");
    }

    private void BuildAndPackage()
    {
        switch (Select("Build and package", ["Back", "Build a project", "Clean artefacts", "Pack packages", "Distribute archive"]))
        {
            case "Build a project":
                BuildProject();
                break;
            case "Pack packages":
                PackPackages();
                break;
            case "Clean artefacts":
                CleanArtefacts();
                break;
            case "Distribute archive":
                DistributeArchive();
                break;
        }
    }

    private void CleanArtefacts()
    {
        string target = Select("Owned artefacts to clean", [
            "Back", "default", "test", "build", "coverage", "docs", "diagnostics", "benchmark", "package", "cache", "all"]);
        if (target == "Back") return;
        ExecuteWithConfirmation(["clean", target], $"Clean owned artefacts: {target}");
    }

    private void BuildProject()
    {
        string project = Select("Project to build", [
            "Back", "Core library", "DevOps tool", "Community Server Core", "Repository solution"]);
        if (project == "Back")
            return;

        string configuration = Select("Build configuration", ["Back", "Release", "Debug"]);
        if (configuration == "Back")
            return;

        var arguments = new List<string> { "build", "--configuration", configuration };
        if (project != "Repository solution")
        {
            string projectPath = project switch
            {
                "Core library" => "src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj",
                "DevOps tool" => "src/devops/tooling/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj",
                "Community Server Core" => "src/server/Rowles.LeanCorpus.Server.Core/Rowles.LeanCorpus.Server.Core.csproj",
                _ => throw new ArgumentOutOfRangeException(nameof(project))
            };
            string framework = Select("Target framework", ["Back", "net10.0", "net11.0"]);
            if (framework == "Back")
                return;
            arguments.AddRange(["--project", projectPath, "--framework", framework]);
        }

        ExecuteWithConfirmation(arguments, $"Build: {project} ({configuration})");
    }

    private void PackPackages()
    {
        string configuration = Select("Package configuration", ["Back", "Release", "Debug"]);
        if (configuration == "Back")
            return;
        string validation = Select("Package validation", ["Back", "Standard pack", "Validate packages", "Clean and validate packages"]);
        if (validation == "Back")
            return;

        var arguments = new List<string> { "pack", "--configuration", configuration };
        if (validation is "Validate packages" or "Clean and validate packages")
            arguments.Add("--validate-packages");
        if (validation == "Clean and validate packages")
            arguments.Add("--clean");
        ExecuteWithConfirmation(arguments, $"Package: {configuration}, {validation}");
    }

    private void DistributeArchive()
    {
        string rid = Select("Release target", ["Back", "linux-x64", "win-x64", "osx-arm64", "linux-arm64"]);
        if (rid == "Back")
            return;
        string runtime = Select("Runtime payload", ["Back", "Framework-dependent", "Self-contained"]);
        if (runtime == "Back")
            return;

        var arguments = new List<string> { "distribute", "--rid", rid };
        if (runtime == "Self-contained")
            arguments.Add("--self-contained");
        ExecuteWithConfirmation(arguments, $"Distribution archive: {rid}, {runtime}");
    }

    private void TestsAndQuality()
    {
        switch (Select("Tests and quality", ["Back", "Run tests", "Coverage", "AOT smoke", "Repository report"]))
        {
            case "Run tests":
                RunTests();
                break;
            case "Coverage":
                RunCoverage();
                break;
            case "AOT smoke":
                RunAotSmoke();
                break;
            case "Repository report":
                RunReport();
                break;
        }
    }

    private void RunReport()
    {
        string group = Select("Report group", ["Back", "all", "git", "files", "code"]);
        if (group == "Back") return;
        string top = ReadValue("Maximum ranked rows", "10");
        string path = ReadOptionalValue("File or code scan glob");
        string strict = Select("Strict report checks", ["Back", "Normal", "Strict"]);
        if (strict == "Back") return;
        string format = Select("Report format", ["Back", "Terminal", "JSON"]);
        if (format == "Back") return;

        var arguments = new List<string> { "report", group, "--top", top };
        AddOptionalPair(arguments, "--path", path);
        if (strict == "Strict") arguments.Add("--strict");
        if (format == "JSON") arguments.Add("--json");
        ExecuteWithConfirmation(arguments, $"Repository report: {group}");
    }

    private void RunCoverage()
    {
        string suite = Select("Coverage suite", ["Back", "core", "sourcegen", "all"]);
        if (suite == "Back")
            return;
        string framework = Select("Target framework", ["Back", "net10.0", "net11.0"]);
        if (framework == "Back")
            return;
        string configuration = Select("Build configuration", ["Back", "Release", "Debug"]);
        if (configuration == "Back")
            return;

        var arguments = new List<string> { "coverage", "--suite", suite, "--framework", framework, "--configuration", configuration };
        string reportChoice = Select("Generate an HTML report?", ["Back", "No HTML", "Generate HTML"]);
        if (reportChoice == "Back")
            return;
        if (reportChoice == "Generate HTML")
            arguments.Add("--generate-report");
        ExecuteWithConfirmation(arguments, $"Coverage: {suite}, {framework}, {configuration}");
    }

    private void RunAotSmoke()
    {
        string framework = Select("AOT framework", ["Back", "net10.0", "net11.0"]);
        if (framework == "Back")
            return;
        string rid = Select("AOT runtime identifier", ["Back", "linux-x64", "win-x64", "osx-arm64"]);
        if (rid == "Back")
            return;
        string configuration = Select("Build configuration", ["Back", "Release", "Debug"]);
        if (configuration == "Back")
            return;
        var arguments = new List<string> { "aot", "--framework", framework, "--rid", rid, "--configuration", configuration };
        ExecuteWithConfirmation(arguments, $"AOT smoke: {framework}, {rid}, {configuration}");
    }

    private void RunDataForge()
    {
        IReadOnlyList<string> arguments;
        string action = Select("DataForge", [
            "Back", "Preview release qualification", "Run release qualification", "Generate a dataset",
            "Reference dataset", "List profiles", "Inspect a generated dataset", "Verify a generated dataset",
            "Reproduce a generated dataset", "Enter token arguments"]);
        switch (action)
        {
            case "Preview release qualification":
                arguments = ["qualify", "--dry"];
                break;
            case "Run release qualification":
                arguments = ["qualify"];
                break;
            case "Generate a dataset":
                arguments = BuildDataForgeGenerationArguments();
                break;
            case "Reference dataset":
                arguments = BuildReferenceArguments();
                break;
            case "List profiles":
                arguments = ["profiles"];
                break;
            case "Inspect a generated dataset":
            case "Verify a generated dataset":
            case "Reproduce a generated dataset":
            {
                string path = ReadValue("Dataset directory or manifest path", "");
                if (path.Length == 0) return;
                string operation = action switch
                {
                    "Inspect a generated dataset" => "inspect",
                    "Verify a generated dataset" => "verify",
                    "Reproduce a generated dataset" => "reproduce",
                    _ => throw new ArgumentOutOfRangeException(nameof(action))
                };
                arguments = [operation, path];
                break;
            }
            case "Enter token arguments":
                arguments = ReadDataForgeArguments();
                break;
            default:
                return;
        }

        if (arguments.Count == 0)
            return;
        ExecuteWithConfirmation(["dataforge", .. arguments], "DataForge: " + string.Join(" ", arguments));
    }

    private IReadOnlyList<string> BuildDataForgeGenerationArguments()
    {
        string profile = Select("Synthetic profile", [
            "Back", "leancorpus-search", "leancorpus-vector", "leancorpus-hybrid",
            "leancorpus-stress", "rowles-text-multilingual", "leancorpus-e2e"]);
        if (profile == "Back")
            return [];
        string? mode = null;
        string? language = null;
        var arguments = new List<string> { "generate", "-Profile", profile };
        if (profile == "leancorpus-stress")
        {
            mode = Select("Stress workload mode", ["Back", .. DataForgeProfileOptions.StressModes]);
            if (mode == "Back")
                return [];
            arguments.AddRange(["-Mode", mode]);
        }
        else if (profile == "rowles-text-multilingual")
        {
            language = Select("Multilingual language", ["Back", .. DataForgeProfileOptions.MultilingualLanguages]);
            if (language == "Back")
                return [];
            arguments.AddRange(["-Language", language]);
        }

        AddOptionalPair(arguments, "-Version", ReadOptionalValue("Profile version override (blank for profile default)"));
        AddOptionalPair(arguments, "-Seed", ReadOptionalValue("Seed override (blank for profile default)"));
        AddOptionalPair(arguments, "-Count", ReadOptionalValue("Record count override (blank for profile default)"));

        if (profile == "leancorpus-vector" || mode == DataForgeProfileOptions.StressVectorMode)
        {
            AddOptionalPair(arguments, "-Dimension", ReadOptionalValue("Vector dimension override"));
            AddOptionalPair(arguments, "-VectorDistribution", ReadOptionalValue("Vector distribution override"));
            AddOptionalPair(arguments, "-ClusterCount", ReadOptionalValue("Vector cluster count override"));
            AddOptionalPair(arguments, "-QueryCount", ReadOptionalValue("Vector query count override"));
        }
        else if (profile == "leancorpus-hybrid" || mode == DataForgeProfileOptions.StressHybridMode)
        {
            AddOptionalPair(arguments, "-Dimension", ReadOptionalValue("Hybrid vector dimension override"));
            if (mode == DataForgeProfileOptions.StressHybridMode)
            {
                AddOptionalPair(arguments, "-CategoryCardinality", ReadOptionalValue("Category cardinality override"));
                AddOptionalPair(arguments, "-NarrowFilterBasisPoints", ReadOptionalValue("Narrow filter share override"));
            }
        }
        else if (mode == DataForgeProfileOptions.StressSearchMode)
        {
            AddOptionalPair(arguments, "-VeryLongShareBasisPoints", ReadOptionalValue("Very long record share override"));
            AddOptionalPair(arguments, "-LongShareBasisPoints", ReadOptionalValue("Long record share override"));
            AddOptionalPair(arguments, "-CategoryCardinality", ReadOptionalValue("Category cardinality override"));
            AddOptionalPair(arguments, "-RegionCardinality", ReadOptionalValue("Region cardinality override"));
            AddOptionalPair(arguments, "-RareAnchorBasisPoints", ReadOptionalValue("Rare anchor share override"));
        }

        if (mode is not null)
        {
            string largeWorkload = Select("Allow stress workloads above the safety limits?",
                ["Back", "No", "Yes, allow large"]);
            if (largeWorkload == "Back")
                return [];
            if (largeWorkload == "Yes, allow large")
                arguments.Add("-AllowLarge");
        }

        AddOptionalPair(arguments, "-Output", ReadOptionalValue("Output directory"));
        return arguments;
    }

    private IReadOnlyList<string> BuildReferenceArguments()
    {
        string action = Select("Wikipedia reference dataset", ["Back", "download", "build", "inspect", "verify"]);
        if (action == "Back")
            return [];
        var arguments = new List<string> { "reference", action };
        switch (action)
        {
            case "download":
                AddOptionalPair(arguments, "-Cache", ReadOptionalValue("Cache directory"));
                break;
            case "build":
                AddOptionalPair(arguments, "-Cache", ReadOptionalValue("Cache directory"));
                AddOptionalPair(arguments, "-Output", ReadOptionalValue("Output directory"));
                break;
            case "inspect":
            case "verify":
                AddOptionalPair(arguments, "-ReferencePath", ReadOptionalValue("Reference dataset path"));
                break;
        }
        return arguments;
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

    private void FixturesAndCorpora()
    {
        switch (Select("Fixtures and corpora", ["Back", "List historical fixtures", "Generate historical fixtures", "Gutenberg corpus"]))
        {
            case "List historical fixtures":
            {
                var arguments = new List<string> { "fixtures", "list" };
                AddOptionalPair(arguments, "--version", ReadOptionalValue("Limit to version"));
                ExecuteWithConfirmation(arguments, "List historical fixtures");
                break;
            }
            case "Generate historical fixtures":
            {
                string version = ReadValue("Package version", "3.1.1");
                string scenario = Select("Fixture scenario", ["Back", "migration", "sparse-hnsw"]);
                if (scenario == "Back")
                    return;
                var arguments = new List<string> { "fixtures", "generate", "--version", version, "--scenario", scenario };
                AddOptionalPair(arguments, "--package-source", ReadOptionalValue("NuGet package source"));
                AddOptionalPair(arguments, "--output-dir", ReadOptionalValue("Output directory"));
                ExecuteWithConfirmation(arguments, $"Generate fixtures: {version}, {scenario}");
                break;
            }
            case "Gutenberg corpus":
            {
                string count = ReadValue("Book count", "200");
                var arguments = new List<string> { "corpus", "gutenberg", "--book-count", count };
                AddOptionalPair(arguments, "--output-dir", ReadOptionalValue("Output directory"));
                ExecuteWithConfirmation(arguments, $"Gutenberg corpus: {count} books");
                break;
            }
        }
    }

    private void Documentation()
    {
        string action = Select("Documentation", ["Back", "Build site", "Prepare content", "Generate metadata", "Serve site", "Test docs site"]);
        if (action == "Back")
            return;
        var arguments = new List<string> { "docs", action switch
        {
            "Build site" => "build",
            "Prepare content" => "prepare",
            "Generate metadata" => "metadata",
            "Serve site" => "serve",
            "Test docs site" => "test",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        }};
        if (action is "Build site" or "Prepare content")
        {
            string benchmarkInputs = Select("Prepare benchmark inputs?", ["Back", "Skip", "Prepare"]);
            if (benchmarkInputs == "Back")
                return;
            string coverageInputs = Select("Prepare coverage inputs?", ["Back", "Skip", "Prepare"]);
            if (coverageInputs == "Back")
                return;
            if (benchmarkInputs == "Skip") arguments.Add("--skip-benchmarks");
            if (coverageInputs == "Skip") arguments.Add("--skip-coverage");
        }
        if (action == "Test docs site")
        {
            string framework = Select("Target framework", ["Back", "net10.0", "net11.0"]);
            if (framework == "Back")
                return;
            arguments.AddRange(["--framework", framework]);
        }
        ExecuteWithConfirmation(arguments, $"Documentation: {action}");
    }

    private void ServerAndDiagnostics()
    {
        string action = Select("Server and diagnostics", ["Back", "Start Community Server", "Process list", "Trace a process", "Dump a process"]);
        switch (action)
        {
            case "Start Community Server":
            {
                string framework = Select("Server framework", ["Back", "net10.0", "net11.0"]);
                if (framework == "Back") return;
                string configuration = Select("Build configuration", ["Back", "Debug", "Release"]);
                if (configuration == "Back") return;
                var arguments = new List<string> { "server", "start", "--framework", framework, "--configuration", configuration };
                string build = Select("Build server before starting?", ["Back", "Build", "Use existing build"]);
                if (build == "Back") return;
                string restore = Select("Restore packages?", ["Back", "Restore", "No restore"]);
                if (restore == "Back") return;
                if (build == "Use existing build") arguments.Add("--no-build");
                if (restore == "No restore") arguments.Add("--no-restore");
                string external = Select("Listen on all IPv4 interfaces?", ["Back", "Loopback only", "External"]);
                if (external == "Back") return;
                if (external == "External") arguments.Add("--external");
                string[] applicationArguments = ReadTokenArguments("Server application argument");
                if (applicationArguments.Length > 0) arguments.AddRange(["--", .. applicationArguments]);
                ExecuteWithConfirmation(arguments, $"Start Community Server: {framework}, {configuration}");
                break;
            }
            case "Process list":
                ExecuteWithConfirmation(["diagnostics", "ps"], "List processes");
                break;
            case "Trace a process":
            case "Dump a process":
            {
                string pid = ReadValue("Process ID", "");
                if (pid.Length == 0) return;
                var arguments = new List<string> { "diagnostics", action == "Trace a process" ? "trace" : "dump", "--pid", pid };
                if (action == "Dump a process")
                {
                    string dumpType = Select("Dump type", ["Back", "Mini", "Heap", "Full", "Triage"]);
                    if (dumpType == "Back") return;
                    arguments.AddRange(["--type", dumpType]);
                }
                ExecuteWithConfirmation(arguments, $"Diagnostics for process {pid}");
                break;
            }
        }
    }

    private void Configuration()
    {
        string action = Select("Configuration", ["Back", "Summary", "Validate", "Deep validation", "Solution inventory"]);
        if (action == "Back") return;
        var arguments = action switch
        {
            "Summary" => new List<string> { "config" },
            "Validate" => new List<string> { "config", "validate" },
            "Deep validation" => new List<string> { "config", "validate", "--deep" },
            "Solution inventory" => new List<string> { "config", "validate", "--solution-inventory" },
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        ExecuteWithConfirmation(arguments, $"Configuration: {action}");
    }

    private string ReadValue(string title, string defaultValue)
        => console.PromptAsync(new TextPrompt<string>($"{title} (default: {defaultValue})")
            .DefaultValue(defaultValue), _promptCancellationToken).GetAwaiter().GetResult();

    private string ReadOptionalValue(string title)
        => console.PromptAsync(new TextPrompt<string>($"{title} (blank to omit)").AllowEmpty(),
            _promptCancellationToken).GetAwaiter().GetResult();

    private string[] ReadTokenArguments(string title)
    {
        var arguments = new List<string>();
        while (arguments.Count < MaximumDataForgeArguments)
        {
            string value = console.PromptAsync(new TextPrompt<string>($"{title} {arguments.Count + 1} (blank to finish)").AllowEmpty(),
                _promptCancellationToken).GetAwaiter().GetResult();
            if (value.Length == 0)
                break;
            arguments.Add(value);
        }
        return [.. arguments];
    }

    private static void AddOptionalPair(List<string> arguments, string name, string value)
    {
        if (value.Length > 0)
            arguments.AddRange([name, value]);
    }

    private void ExecuteWithConfirmation(IReadOnlyList<string> arguments, string summary)
    {
        console.Write(new Text(summary));
        console.WriteLine();
        if (Select("Run this command?", ["Back", "Run command"]) != "Run command")
            return;
        _lastExitCode = operations.RunCommand(arguments, _promptCancellationToken);
    }

    private void ObserveRuns()
    {
        var allRuns = operations.ReadRecentRuns()
            .OrderByDescending(static run => run.Manifest.StartedUtc)
            .Take(MaximumRecentRuns)
            .ToArray();
        if (allRuns.Length == 0)
        {
            console.Write(new Text("No structured DevOps runs were found."));
            console.WriteLine();
            return;
        }

        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Category")
            .AddColumn("Run ID")
            .AddColumn("Status")
            .AddColumn("Elapsed");
        foreach (var workbenchRun in allRuns)
        {
            table.AddRow(
                Markup.Escape(workbenchRun.Category),
                Markup.Escape(workbenchRun.Manifest.RunId.ToString("N")[..8]),
                Markup.Escape(workbenchRun.Manifest.State.ToString()),
                Markup.Escape(FormatElapsed(workbenchRun.Manifest)));
        }

        console.Write(new Panel(table)
            .Header("[bold]Recent DevOps runs[/]")
            .Border(BoxBorder.Rounded));
        console.WriteLine();

        var categories = allRuns.Select(static run => run.Category)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Prepend("All categories")
            .ToArray();
        string category = Select("Filter recent runs by category", categories);
        var runs = allRuns.Where(run => category == "All categories" ||
                run.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var labels = runs.Select(static run =>
            $"{run.Category} | {run.Manifest.State} | {run.Manifest.StartedUtc:yyyy-MM-dd HH:mm} UTC | {FormatElapsed(run.Manifest)} | {run.Manifest.RunId:D}")
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
            $"Elapsed: {FormatElapsed(manifest)}",
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

    private static string FormatElapsed(RunManifest manifest)
    {
        if (manifest.CompletedUtc is not { } completed || completed < manifest.StartedUtc)
        {
            return "in progress or unavailable";
        }

        TimeSpan elapsed = completed - manifest.StartedUtc;
        return $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
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
    Func<CancellationToken, ICommandHandler> commandHandlerFactory,
    CheckoutIdentity checkout,
    TextWriter output,
    TextWriter error) : IWorkbenchOperations
{
    public int RunCommand(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var commandHandler = commandHandlerFactory(cancellationToken);
        return CommandLineApplication.Run(
            ["--repo", checkout.RepositoryRoot, .. arguments], output, error, commandHandler, cancellationToken);
    }

    public string ReadRepositoryMetadata(CancellationToken cancellationToken)
    {
        var temporary = Directory.CreateTempSubdirectory("leancorpus-workbench-status-");
        try
        {
            var processRunner = new ProcessRunner();
            var provenance = new GitRepositoryProvenanceReader(processRunner)
                .ReadAsync(checkout, temporary.FullName, cancellationToken).GetAwaiter().GetResult();
            var sdk = new DotNetSdkResolver(processRunner)
                .ResolveAsync(checkout, temporary.FullName, cancellationToken).GetAwaiter().GetResult();
            string configurationStatus;
            try
            {
                _ = new ConfigurationLoader(checkout.RepositoryRoot).Load();
                configurationStatus = "valid";
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
            {
                configurationStatus = $"unavailable ({exception.Message})";
            }

            string branch = provenance.Outcome == OutcomeKind.Success ? provenance.Branch ?? "unknown" : $"unavailable ({provenance.Failure})";
            string commit = provenance.Outcome == OutcomeKind.Success ? provenance.Commit ?? "unknown" : "unknown";
            if (commit.Length > 8)
                commit = commit[..8];
            string dirty = provenance.Outcome == OutcomeKind.Success ? provenance.Dirty ? "yes" : "no" : "unknown";
            string requestedSdk = sdk.RequestedVersion ?? "unknown";
            string selectedSdk = sdk.Outcome == OutcomeKind.Success ? sdk.SelectedVersion ?? "unknown" : $"unavailable ({sdk.Failure})";
            string sdkRollForward = sdk.RollForward ?? "default";
            return $"Branch: {branch}\nCommit: {commit}\nDirty: {dirty}\nSDK requested: {requestedSdk}\nSDK selected: {selectedSdk}\nSDK roll-forward: {sdkRollForward}\nConfiguration: {configurationStatus}";
        }
        finally
        {
            try { Directory.Delete(temporary.FullName, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    public IReadOnlyList<WorkbenchRun> ReadRecentRuns()
    {
        var paths = new ArtifactPaths(checkout.RepositoryRoot);
        var candidates = new List<(string Category, Guid RunId, DateTime ModifiedUtc)>();
        foreach (var categoryDescriptor in ArtifactPaths.RunCategories)
        {
            var category = categoryDescriptor.Id;
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
