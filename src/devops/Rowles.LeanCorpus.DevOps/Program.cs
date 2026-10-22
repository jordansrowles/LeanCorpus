using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Rowles.LeanCorpus.DevOps.Commands;

namespace Rowles.LeanCorpus.DevOps;

internal static class Program
{
    public static int Main(string[] args)
        => CommandLineApplication.Run(args, Console.Out, Console.Error);

    internal static RootCommand BuildRootCommand(ICommandHandler handler)
    {
        var root = new RootCommand("LeanCorpus managed DevOps.");
        root.Add(new VersionOption());
        var repository = new Option<string?>("--repo")
        {
            Description = "Select a LeanCorpus checkout when caller and executable locations disagree.",
            Recursive = true
        };
        root.Add(repository);

        var build = BuildBuildCommand(handler, repository);
        var clean = BuildCleanCommand(handler, repository);
        var server = BuildServerCommand(handler, repository);
        var report = BuildReportCommand(handler, repository);
        var coverage = BuildCoverageCommand(handler, repository);
        var pack = BuildPackCommand(handler, repository);
        var data = BuildDataCommand(handler, repository);
        var dataForge = BuildDataForgeCommand(handler, repository);
        var diagnostics = BuildDiagnosticsCommand(handler, repository);
        var benchmark = BuildBenchmarkCommand(handler, repository);
        root.Add(build);
        root.Add(clean);
        root.Add(server);
        root.Add(report);
        root.Add(coverage);
        root.Add(pack);
        root.Add(data);
        root.Add(dataForge);
        root.Add(diagnostics);
        root.Add(benchmark);

        return root;
    }

    private static Command BuildBuildCommand(ICommandHandler handler, Option<string?> repository)
    {
        var configuration = new Option<string>("--configuration", ["-Configuration"])
        {
            Description = "Build configuration (default: Release).",
            DefaultValueFactory = _ => "Release"
        };
        var project = new Option<string?>("--project", ["-Project"])
        {
            Description = "Build one project instead of the solution."
        };
        var framework = new Option<string>("--framework", ["-Framework"])
        {
            Description = "Target framework for a project build (default: net11.0).",
            DefaultValueFactory = _ => "net11.0"
        };
        var command = new Command("build", "Build the LeanCorpus solution or one project.");
        command.Add(configuration);
        command.Add(project);
        command.Add(framework);
        command.SetAction(result => handler.Build(new BuildCommandOptions(
            result.GetValue(configuration) ?? "Release",
            result.GetValue(project),
            result.GetValue(project) is null ? null : result.GetValue(framework) ?? "net11.0",
            result.GetValue(repository))));
        return command;
    }

    private static Command BuildCleanCommand(ICommandHandler handler, Option<string?> repository)
    {
        var target = new Argument<string>("target")
        {
            Description = "Owned artefact set (default: default).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "default"
        };
        var command = new Command("clean", "Remove owned LeanCorpus artefacts.");
        command.Add(target);
        command.SetAction(result => handler.Clean(new CleanCommandOptions(result.GetValue(target) ?? "default", result.GetValue(repository))));
        return command;
    }

    private static Command BuildServerCommand(ICommandHandler handler, Option<string?> repository)
    {
        var framework = new Option<string>("--framework", ["-Framework"])
        {
            Description = "Target framework (default: net11.0).",
            DefaultValueFactory = _ => "net11.0",
            Recursive = true
        };
        var configuration = new Option<string>("--configuration", ["-Configuration"])
        {
            Description = "Build configuration (default: Debug).",
            DefaultValueFactory = _ => "Debug",
            Recursive = true
        };
        var noBuild = new Option<bool>("--no-build", ["-NoBuild"]) { Description = "Skip the build performed by dotnet run.", Recursive = true };
        var noRestore = new Option<bool>("--no-restore", ["-NoRestore"]) { Description = "Skip restore performed by dotnet run.", Recursive = true };
        var external = new Option<bool>("--external", ["-External"]) { Description = "Listen on all IPv4 interfaces at port 5080.", Recursive = true };

        ServerStartCommandOptions CreateOptions(ParseResult result, Argument<string[]>? applicationArguments)
        {
            var passthrough = applicationArguments is null ? [] : result.GetValue(applicationArguments) ?? [];
            return new ServerStartCommandOptions(
                result.GetValue(framework) ?? "net11.0",
                result.GetValue(configuration) ?? "Debug",
                result.GetValue(noBuild),
                result.GetValue(noRestore),
                result.GetValue(external),
                passthrough,
                result.GetValue(repository));
        }

        var server = new Command("server", "Community Server commands.");
        server.Add(framework);
        server.Add(configuration);
        server.Add(noBuild);
        server.Add(noRestore);
        server.Add(external);

        var startArguments = new Argument<string[]>("application-arguments")
        {
            Description = "Arguments passed to the application after --.",
            Arity = ArgumentArity.ZeroOrMore
        };
        var start = new Command("start", "Start the Community Server in the foreground.");
        start.Add(startArguments);
        start.SetAction(result =>
        {
            var passthrough = result.GetValue(startArguments) ?? [];
            if (passthrough.Length > 0 && !result.Tokens.Any(token => token.Type == TokenType.DoubleDash))
            {
                result.InvocationConfiguration.Error.WriteLine("Application arguments must follow the -- delimiter.");
                return 2;
            }

            return handler.StartServer(CreateOptions(result, startArguments));
        });
        server.Add(start);
        server.SetAction(result => handler.StartServer(CreateOptions(result, null)));
        return server;
    }

    private static Command BuildReportCommand(ICommandHandler handler, Option<string?> repository)
    {
        var group = new Argument<string>("group")
        {
            Description = "Report group: all, git, files or code (default: all).",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "all"
        };
        var top = new Option<int>("--top", ["-Top"])
        {
            Description = "Maximum rows in ranked report lists (default: 10).",
            DefaultValueFactory = _ => 10
        };
        var path = new Option<string?>("--path", ["-Path"])
        {
            Description = "Restrict file and code scans to a glob."
        };
        var strict = new Option<bool>("--strict", ["-Strict"])
        {
            Description = "Return failure when strict report checks find violations."
        };
        var json = new Option<bool>("--json", ["-Json"])
        {
            Description = "Write one versioned JSON result document to stdout."
        };
        var command = new Command("report", "Report on repository history, tracked files and code health.");
        command.Add(group);
        command.Add(top);
        command.Add(path);
        command.Add(strict);
        command.Add(json);
        command.SetAction(result => handler.Report(new ReportCommandOptions(
            result.GetValue(group) ?? "all",
            result.GetValue(top),
            result.GetValue(path),
            result.GetValue(strict),
            result.GetValue(json),
            result.GetValue(repository))));
        return command;
    }

    private static Command BuildCoverageCommand(ICommandHandler handler, Option<string?> repository)
    {
        var framework = new Option<string>("--framework", ["-Framework"])
        {
            Description = "Target framework (default: net10.0).",
            DefaultValueFactory = _ => "net10.0"
        };
        var configuration = new Option<string>("--configuration", ["-Configuration"])
        {
            Description = "Build configuration (default: Release).",
            DefaultValueFactory = _ => "Release"
        };
        var suite = new Option<string>("--suite", ["-Suite"])
        {
            Description = "Coverage-eligible test suite: core, sourcegen, or all (default: all).",
            DefaultValueFactory = _ => "all"
        };
        var clean = new Option<bool>("--clean", ["-Clean"])
        {
            Description = "Remove previous coverage artefacts before running."
        };
        var includePerformance = new Option<bool>("--include-performance", ["-IncludePerformance"])
        {
            Description = "Include tests marked Coverage=Skip."
        };
        var generateReport = new Option<bool>("--generate-report", ["-GenerateReport"])
        {
            Description = "Generate an HTML report with the repository-pinned ReportGenerator."
        };

        var command = new Command("coverage", "Run eligible test suites with code coverage collection.");
        command.Add(framework);
        command.Add(configuration);
        command.Add(suite);
        command.Add(clean);
        command.Add(includePerformance);
        command.Add(generateReport);
        command.SetAction(result => handler.Coverage(new CoverageCommandOptions(
            result.GetValue(framework) ?? "net10.0",
            result.GetValue(configuration) ?? "Release",
            result.GetValue(suite) ?? "all",
            result.GetValue(clean),
            result.GetValue(includePerformance),
            result.GetValue(generateReport),
            result.GetValue(repository))));
        return command;
    }

    private static Command BuildPackCommand(ICommandHandler handler, Option<string?> repository)
    {
        var configuration = new Option<string>("--configuration", ["-Configuration"])
        {
            Description = "Package configuration (default: Release).",
            DefaultValueFactory = _ => "Release"
        };
        var compressionOnly = new Option<bool>("--compression-only", ["-CompressionOnly"])
        {
            Description = "Pack Core and the three optional compression packages."
        };
        var noBuild = new Option<bool>("--no-build", ["-NoBuild"])
        {
            Description = "Pack existing builds without rebuilding projects."
        };
        var clean = new Option<bool>("--clean", ["-Clean"])
        {
            Description = "Remove the owned package output directory before packing."
        };
        var validatePackages = new Option<bool>("--validate-packages", ["-ValidatePackages"])
        {
            Description = "Build and run isolated consumers for every shipping package."
        };
        var validateCompression = new Option<bool>("--validate-compression", ["-ValidateCompression"])
        {
            Description = "Build and run the isolated compression consumer matrix."
        };
        var command = new Command("pack", "Pack and validate LeanCorpus NuGet packages.");
        command.Add(configuration);
        command.Add(compressionOnly);
        command.Add(noBuild);
        command.Add(clean);
        command.Add(validatePackages);
        command.Add(validateCompression);
        command.SetAction(result => handler.Pack(new PackCommandOptions(
            result.GetValue(configuration) ?? "Release",
            result.GetValue(compressionOnly),
            result.GetValue(noBuild),
            result.GetValue(clean),
            result.GetValue(validatePackages),
            result.GetValue(validateCompression),
            result.GetValue(repository))));
        return command;
    }

    private static Command BuildDataCommand(ICommandHandler handler, Option<string?> repository)
    {
        var dataset = new Argument<string>("dataset")
        {
            Description = "Dataset to prepare: gutenberg or migration-fixtures-311.",
            Arity = ArgumentArity.ExactlyOne
        };
        var bookCount = new Option<int>("--book-count", ["-BookCount"])
        {
            Description = "Number of Project Gutenberg books to prepare (default: 200).",
            DefaultValueFactory = _ => 200
        };
        var outputDirectory = new Option<string?>("--output-dir", ["-OutputDir"])
        {
            Description = "Output directory (dataset-specific default when omitted)."
        };
        var packageSource = new Option<string>("--package-source", ["-PackageSource"])
        {
            Description = "NuGet source used by the pinned 3.1.1 fixture generator.",
            DefaultValueFactory = _ => "https://api.nuget.org/v3/index.json"
        };
        var command = new Command("data", "Prepare benchmark datasets and migration fixtures.");
        command.Add(dataset);
        command.Add(bookCount);
        command.Add(outputDirectory);
        command.Add(packageSource);
        command.SetAction(result => handler.Data(new DataCommandOptions(
            result.GetValue(dataset) ?? string.Empty,
            result.GetValue(bookCount),
            result.GetValue(outputDirectory),
            result.GetValue(packageSource) ?? "https://api.nuget.org/v3/index.json",
            result.GetValue(repository))));
        return command;
    }

    private static Command BuildDataForgeCommand(ICommandHandler handler, Option<string?> repository)
    {
        var arguments = new Argument<string[]>("arguments")
        {
            Description = "DataForge tool command and arguments.",
            Arity = ArgumentArity.ZeroOrMore
        };
        var command = new Command("dataforge", "Run the DataForge generation and reference tool.");
        command.Add(arguments);
        command.SetAction(result => handler.DataForge(new DataForgeCommandOptions(
            result.GetValue(arguments) ?? [], result.GetValue(repository))));
        return command;
    }

    private static Command BuildDiagnosticsCommand(ICommandHandler handler, Option<string?> repository)
    {
        var arguments = new Argument<string[]>("arguments")
        {
            Description = "Diagnostics subcommand and tool arguments.",
            Arity = ArgumentArity.ZeroOrMore
        };
        var command = new Command("diagnostics", "Attach standard .NET diagnostics to a process.");
        command.Add(repository);
        command.Add(arguments);
        command.SetAction(result => handler.Diagnostics(new DiagnosticsCommandOptions(
            result.GetValue(arguments) ?? [], result.GetValue(repository))));
        return command;
    }

    private static Command BuildBenchmarkCommand(ICommandHandler handler, Option<string?> repository)
    {
        var suite = new Option<string>("--suite", ["-Suite"])
        {
            Description = "Benchmark suite (default: all).",
            DefaultValueFactory = _ => "all"
        };
        var strategy = new Option<string>("--strat", ["-Strat"])
        {
            Description = "Benchmark strategy (default: default).",
            DefaultValueFactory = _ => "default"
        };
        var framework = new Option<string>("--framework", ["-Framework"])
        {
            Description = "Benchmark target framework (default: net11.0).",
            DefaultValueFactory = _ => "net11.0"
        };
        var documentCount = new Option<int>("--doc-count", ["-DocCount"])
        {
            Description = "Override the strategy document count."
        };
        var dataset = new Option<string>("--dataset", ["-Dataset"])
        {
            Description = "Benchmark dataset: synthetic or wikipedia (default: synthetic).",
            DefaultValueFactory = _ => "synthetic"
        };
        var referencePath = new Option<string?>("--reference-path", ["-ReferencePath"])
        {
            Description = "Frozen Wikipedia reference directory."
        };
        var area = new Option<string?>("--area", ["-Area"]);
        var group = new Option<string?>("--group", ["-Group"]);
        var sourceCommit = new Option<string?>("--source-commit", ["-SourceCommit"]);
        var sourceRef = new Option<string?>("--source-ref", ["-SourceRef"]);
        var sourceManifest = new Option<string?>("--source-manifest", ["-SourceManifest"]);
        var controlled = new Option<bool>("--controlled", ["-Controlled"]);
        var corpusOnly = new Option<bool>("--corpus-only", ["-CorpusOnly"]);
        var gcDump = new Option<bool>("--gc-dump", ["-GcDump"]);
        var prepareData = new Option<bool>("--prepare-data", ["-PrepareData"]);
        var bookCount = new Option<int>("--book-count", ["-BookCount"])
        {
            Description = "Gutenberg book count for data preparation (default: 200).",
            DefaultValueFactory = _ => 200
        };
        var dry = new Option<bool>("--dry", ["-Dry"]);
        var list = new Option<bool>("--list", ["-List"]);
        var arguments = new Argument<string[]>("arguments")
        {
            Description = "BenchmarkDotNet arguments following --.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var command = new Command("benchmark", "Run selected BenchmarkDotNet suites.");
        command.Add(suite);
        command.Add(strategy);
        command.Add(framework);
        command.Add(documentCount);
        command.Add(dataset);
        command.Add(referencePath);
        command.Add(area);
        command.Add(group);
        command.Add(sourceCommit);
        command.Add(sourceRef);
        command.Add(sourceManifest);
        command.Add(controlled);
        command.Add(corpusOnly);
        command.Add(gcDump);
        command.Add(prepareData);
        command.Add(bookCount);
        command.Add(dry);
        command.Add(list);
        command.Add(arguments);
        command.SetAction(result => handler.Benchmark(new BenchmarkCommandOptions(
            result.GetValue(suite) ?? "all",
            result.GetValue(strategy) ?? "default",
            result.GetValue(framework) ?? "net11.0",
            result.GetValue(documentCount),
            result.GetValue(dataset) ?? "synthetic",
            result.GetValue(referencePath),
            result.GetValue(area),
            result.GetValue(group),
            result.GetValue(sourceCommit),
            result.GetValue(sourceRef),
            result.GetValue(sourceManifest),
            result.GetValue(controlled),
            result.GetValue(corpusOnly),
            result.GetValue(gcDump),
            result.GetValue(prepareData),
            result.GetValue(bookCount),
            result.GetValue(dry),
            result.GetValue(list),
            result.GetValue(arguments) ?? [],
            result.GetValue(repository))));
        return command;
    }
}

internal static class CommandLineApplication
{
    public static int Run(string[] args, TextWriter output, TextWriter error, ICommandHandler? handler = null)
    {
        var commandHandler = handler ?? new ManagedCommandHandler(output, error);
        if (TryGetBenchmarkRemoteOptions(args, out var remoteParse))
        {
            if (remoteParse.Help)
            {
                output.WriteLine(RemoteBenchmarkHelp);
                return 0;
            }
            if (remoteParse.Error is not null)
            {
                error.WriteLine(remoteParse.Error);
                return 2;
            }
            return commandHandler.BenchmarkRemote(remoteParse.Options!);
        }

        if (TryGetDataForgeOptions(args, out var dataForgeOptions))
        {
            return commandHandler.DataForge(dataForgeOptions);
        }

        if (TryGetDiagnosticsOptions(args, out var diagnosticsOptions))
        {
            return commandHandler.Diagnostics(diagnosticsOptions);
        }

        if (args.Length > 0 && UnmigratedCommands.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            error.WriteLine($"The '{args[0]}' command remains on the PowerShell path during migration. Run `pwsh ./devops.ps1 {args[0]}` with the same arguments.");
            return 2;
        }

        var root = Program.BuildRootCommand(commandHandler);
        var parseResult = root.Parse(NormalizeBenchmarkSuiteArgument(args));
        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                error.WriteLine(parseError.Message);
            }

            return 2;
        }

        var result = parseResult.Invoke(new InvocationConfiguration
        {
            Output = output,
            Error = error,
            EnableDefaultExceptionHandler = false
        });

        return result;
    }

    private static readonly string[] UnmigratedCommands =
    [
        "aot", "benchmarks", "docs", "setup", "test", "ui"
    ];

    private const string RemoteBenchmarkHelp = """
        Usage:
          devops benchmark remote start [--host <user@host>] [--path <absolute-posix-path>] [--branch <branch>] [--session <name>] -- [benchmark arguments]
          devops benchmark remote status --run-id <guid>
          devops benchmark remote cancel --run-id <guid>

        Start example:
          devops benchmark remote start --host jordan@debian --path /home/jordan/code/leancorpus --branch main -- --suite query --strat intense

        Status and cancellation use the remote identity stored in the local benchmark run artefacts.
        Cancellation requests Ctrl+C in tmux and remains unconfirmed until a later status response is terminal.
        """;

    private sealed record RemoteBenchmarkParse(
        BenchmarkRemoteCommandOptions? Options,
        string? Error,
        bool Help);

    private static bool TryGetBenchmarkRemoteOptions(string[] args, out RemoteBenchmarkParse parse)
    {
        var commandIndex = args.Length >= 3 && args[0].Equals("--repo", StringComparison.OrdinalIgnoreCase)
            ? 2
            : 0;
        if (commandIndex + 1 >= args.Length || !args[commandIndex].Equals("benchmark", StringComparison.OrdinalIgnoreCase) ||
            !args[commandIndex + 1].Equals("remote", StringComparison.OrdinalIgnoreCase))
        {
            parse = null!;
            return false;
        }

        var index = commandIndex + 2;
        string action = "start";
        if (index < args.Length && !args[index].StartsWith("-", StringComparison.Ordinal))
        {
            action = args[index++].ToLowerInvariant();
        }
        if (action is "help" or "--help")
        {
            parse = new RemoteBenchmarkParse(null, null, true);
            return true;
        }
        if (action is not ("start" or "status" or "cancel"))
        {
            parse = new RemoteBenchmarkParse(null, $"Unknown remote benchmark action '{action}'. Valid actions are start, status, cancel and help.", false);
            return true;
        }

        string? host = null;
        string? remotePath = null;
        string? branch = null;
        string? sessionName = null;
        string? repositoryPath = commandIndex == 2 ? args[1] : null;
        Guid? runId = null;
        var benchmarkArguments = new List<string>();
        string? failure = null;
        while (index < args.Length && failure is null)
        {
            var argument = args[index++];
            if (argument == "--")
            {
                benchmarkArguments.AddRange(args.Skip(index));
                break;
            }
            if (argument is "--help" or "-h")
            {
                parse = new RemoteBenchmarkParse(null, null, true);
                return true;
            }

            string? Value()
            {
                if (index >= args.Length || args[index] == "--")
                {
                    failure = $"Option '{argument}' requires a value.";
                    return null;
                }
                return args[index++];
            }

            switch (argument.ToLowerInvariant())
            {
                case "--host":
                case "-remote":
                    host = Value();
                    break;
                case "--path":
                case "-remotepath":
                    remotePath = Value();
                    break;
                case "--branch":
                case "-branch":
                    branch = Value();
                    break;
                case "--session":
                case "-sessionname":
                    sessionName = Value();
                    break;
                case "--run-id":
                    var runIdText = Value();
                    if (runIdText is not null)
                    {
                        if (!Guid.TryParse(runIdText, out var parsedRunId) || parsedRunId == Guid.Empty)
                            failure = $"'{runIdText}' is not a valid remote benchmark run id.";
                        else
                            runId = parsedRunId;
                    }
                    break;
                case "--repo":
                case "-repo":
                    repositoryPath = Value();
                    break;
                case "--benchmark-args":
                case "-benchmarkargs":
                    var legacyArguments = Value();
                    if (legacyArguments is not null)
                    {
                        try
                        {
                            benchmarkArguments.AddRange(SplitLegacyCommandLine(legacyArguments));
                        }
                        catch (FormatException exception)
                        {
                            failure = exception.Message;
                        }
                    }
                    break;
                default:
                    failure = $"Unknown remote benchmark option or argument '{argument}'. Use 'benchmark remote --help' for usage.";
                    break;
            }
        }

        if (failure is null && action == "start" && runId is not null)
            failure = "--run-id is valid only for remote status and cancel actions.";
        if (failure is null && action != "start" && benchmarkArguments.Count > 0)
            failure = $"Benchmark arguments are valid only for 'benchmark remote start'.";

        parse = new RemoteBenchmarkParse(failure is null
            ? new BenchmarkRemoteCommandOptions(action, host, remotePath, branch, sessionName, runId, benchmarkArguments, repositoryPath)
            : null, failure, false);
        return true;
    }

    private static IReadOnlyList<string> SplitLegacyCommandLine(string value)
    {
        var arguments = new List<string>();
        var token = new System.Text.StringBuilder();
        var quote = '\0';
        var escaped = false;
        var hasToken = false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (escaped)
            {
                token.Append(character);
                escaped = false;
                hasToken = true;
                continue;
            }
            if (character == '\\' && quote != '\'' && index + 1 < value.Length &&
                (char.IsWhiteSpace(value[index + 1]) || value[index + 1] is '\\' or '\'' or '"'))
            {
                escaped = true;
                hasToken = true;
                continue;
            }
            if (quote != '\0')
            {
                if (character == quote)
                    quote = '\0';
                else
                    token.Append(character);
                hasToken = true;
                continue;
            }
            if (character is '\'' or '"')
            {
                quote = character;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(character))
            {
                if (hasToken)
                {
                    arguments.Add(token.ToString());
                    token.Clear();
                    hasToken = false;
                }
            }
            else
            {
                token.Append(character);
                hasToken = true;
            }
        }
        if (escaped)
            token.Append('\\');
        if (quote != '\0')
            throw new FormatException("--benchmark-args contains an unterminated quote.");
        if (hasToken)
            arguments.Add(token.ToString());
        return arguments;
    }

    private static string[] NormalizeBenchmarkSuiteArgument(string[] args)
    {
        var commandIndex = args.Length >= 3 && args[0].Equals("--repo", StringComparison.OrdinalIgnoreCase)
            ? 2
            : 0;
        var suiteIndex = commandIndex + 1;
        if (commandIndex >= args.Length || !args[commandIndex].Equals("benchmark", StringComparison.OrdinalIgnoreCase) ||
            suiteIndex >= args.Length || args[suiteIndex].StartsWith("-", StringComparison.Ordinal) || args[suiteIndex] == "--")
        {
            return args;
        }

        return [.. args.Take(suiteIndex), "--suite", args[suiteIndex], .. args.Skip(suiteIndex + 1)];
    }

    private static bool TryGetDataForgeOptions(string[] args, out DataForgeCommandOptions options)
    {
        string? repositoryPath = null;
        var commandIndex = -1;
        if (args.Length >= 3 && args[0].Equals("--repo", StringComparison.OrdinalIgnoreCase) &&
            args[2].Equals("dataforge", StringComparison.OrdinalIgnoreCase))
        {
            repositoryPath = args[1];
            commandIndex = 2;
        }
        else if (args.Length > 0 && args[0].Equals("dataforge", StringComparison.OrdinalIgnoreCase))
        {
            commandIndex = 0;
        }

        if (commandIndex < 0)
        {
            options = null!;
            return false;
        }

        options = new DataForgeCommandOptions(args.Skip(commandIndex + 1).ToArray(), repositoryPath);
        return true;
    }

    private static bool TryGetDiagnosticsOptions(string[] args, out DiagnosticsCommandOptions options)
    {
        string? repositoryPath = null;
        var commandIndex = -1;
        if (args.Length >= 3 && args[0].Equals("--repo", StringComparison.OrdinalIgnoreCase) &&
            args[2].Equals("diagnostics", StringComparison.OrdinalIgnoreCase))
        {
            repositoryPath = args[1];
            commandIndex = 2;
        }
        else if (args.Length > 0 && args[0].Equals("diagnostics", StringComparison.OrdinalIgnoreCase))
        {
            commandIndex = 0;
        }

        if (commandIndex < 0)
        {
            options = null!;
            return false;
        }

        var arguments = new List<string>();
        var passthrough = false;
        for (var index = commandIndex + 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--")
            {
                passthrough = true;
                arguments.Add(argument);
                continue;
            }

            if (!passthrough && repositoryPath is null &&
                argument.Equals("--repo", StringComparison.OrdinalIgnoreCase) ||
                !passthrough && repositoryPath is null && argument.Equals("-Repo", StringComparison.OrdinalIgnoreCase))
            {
                if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]))
                {
                    options = null!;
                    return false;
                }
                repositoryPath = args[++index];
                continue;
            }

            arguments.Add(argument);
        }

        options = new DiagnosticsCommandOptions(arguments, repositoryPath);
        return true;
    }
}

internal sealed class UnimplementedCommandHandler(TextWriter error) : ICommandHandler
{
    public int Build(BuildCommandOptions options) => Missing("build");

    public int Clean(CleanCommandOptions options) => Missing("clean");

    public int StartServer(ServerStartCommandOptions options) => Missing("server");

    public int Report(ReportCommandOptions options) => Missing("report");

    public int Coverage(CoverageCommandOptions options) => Missing("coverage");

    public int Pack(PackCommandOptions options) => Missing("pack");

    public int Data(DataCommandOptions options) => Missing("data");

    public int DataForge(DataForgeCommandOptions options) => Missing("dataforge");

    public int Diagnostics(DiagnosticsCommandOptions options) => Missing("diagnostics");

    public int Benchmark(BenchmarkCommandOptions options) => Missing("benchmark");

    public int BenchmarkRemote(BenchmarkRemoteCommandOptions options) => Missing("benchmark remote");

    private int Missing(string command)
    {
        error.WriteLine($"The managed '{command}' service has not been implemented.");
        return 1;
    }
}
