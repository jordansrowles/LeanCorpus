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
        root.Add(build);
        root.Add(clean);
        root.Add(server);
        root.Add(report);

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
}

internal static class CommandLineApplication
{
    public static int Run(string[] args, TextWriter output, TextWriter error, ICommandHandler? handler = null)
    {
        if (args.Length > 0 && UnmigratedCommands.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            error.WriteLine($"The '{args[0]}' command remains on the PowerShell path during migration. Run `pwsh ./devops.ps1 {args[0]}` with the same arguments.");
            return 2;
        }

        var commandHandler = handler ?? new ManagedCommandHandler(output, error);
        var root = Program.BuildRootCommand(commandHandler);
        var parseResult = root.Parse(args);
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
        "aot", "benchmark", "benchmarks", "coverage", "data", "dataforge", "diagnostics", "docs", "pack", "setup", "test", "ui"
    ];
}

internal sealed class UnimplementedCommandHandler(TextWriter error) : ICommandHandler
{
    public int Build(BuildCommandOptions options) => Missing("build");

    public int Clean(CleanCommandOptions options) => Missing("clean");

    public int StartServer(ServerStartCommandOptions options) => Missing("server");

    public int Report(ReportCommandOptions options) => Missing("report");

    private int Missing(string command)
    {
        error.WriteLine($"The managed '{command}' service has not been implemented.");
        return 1;
    }
}
