using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Reporting;
using Rowles.LeanCorpus.DevOps.Server;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Commands;

internal sealed class ManagedCommandHandler(TextWriter output, TextWriter error) : ICommandHandler
{
    private readonly CommandResultRenderer _renderer = new();

    public int Build(BuildCommandOptions options)
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
            var checkout = RepositoryPaths.Resolve(options.RepositoryPath, Environment.CurrentDirectory, Environment.ProcessPath);
            if (RejectStaleManagedHost(checkout) is { } staleExitCode)
            {
                return staleExitCode;
            }

            var paths = new ArtifactPaths(checkout.RepositoryRoot);
            var processRunner = new ProcessRunner();
            var service = new BuildService(
                paths,
                new ArtifactRunStore(paths),
                processRunner,
                new DotNetSdkResolver(processRunner),
                new GitRepositoryProvenanceReader(processRunner),
                projectInspector: new DotNetProjectInspector(processRunner));
            var result = service.BuildAsync(new BuildRequest
            {
                Checkout = checkout,
                ProjectPath = options.Project,
                Configuration = options.Configuration,
                Framework = options.Framework
            }, cancellation.Token).GetAwaiter().GetResult();
            var commandResult = result.RunDirectory is null
                ? result.CommandResult
                : result.CommandResult with
                {
                    Message = result.CommandResult.Outcome == OutcomeKind.Success
                        ? $"Build succeeded. Run evidence: {result.RunDirectory}"
                        : result.CommandResult.Message,
                    Details = result.CommandResult.Details is null
                        ? result.RunDirectory
                        : result.CommandResult.Details + Environment.NewLine + result.RunDirectory
                };
            return _renderer.Render(commandResult, output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not run the managed build command.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int Clean(CleanCommandOptions options)
    {
        try
        {
            var checkout = RepositoryPaths.Resolve(options.RepositoryPath, Environment.CurrentDirectory, Environment.ProcessPath);
            if (RejectStaleManagedHost(checkout) is { } staleExitCode)
            {
                return staleExitCode;
            }

            var paths = new ArtifactPaths(checkout.RepositoryRoot);
            var result = new CleanService(paths, new ArtifactRunStore(paths))
                .CleanAsync(new CleanRequest { Checkout = checkout, Target = options.Target })
                .GetAwaiter().GetResult();
            var commandResult = result.RunDirectory is null
                ? result.CommandResult
                : result.CommandResult with
                {
                    Message = result.CommandResult.Outcome == OutcomeKind.Success
                        ? $"{result.CommandResult.Message} Run evidence: {result.RunDirectory}"
                        : result.CommandResult.Message,
                    Details = result.CommandResult.Details is null
                        ? result.RunDirectory
                        : result.CommandResult.Details + Environment.NewLine + result.RunDirectory
                };
            return _renderer.Render(commandResult, output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not run the managed clean command.", Details: exception.Message), output, error);
        }
    }

    public int StartServer(ServerStartCommandOptions options)
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
            if (options.External)
            {
                error.WriteLine("Warning: external listening is unencrypted. Use only on a trusted network and protect the host with the deployment environment.");
            }

            var checkout = RepositoryPaths.Resolve(options.RepositoryPath, Environment.CurrentDirectory, Environment.ProcessPath);
            if (RejectStaleManagedHost(checkout) is { } staleExitCode)
            {
                return staleExitCode;
            }

            var paths = new ArtifactPaths(checkout.RepositoryRoot);
            var processRunner = new ProcessRunner();
            var service = new ServerService(
                paths,
                new ArtifactRunStore(paths),
                processRunner,
                new DotNetSdkResolver(processRunner),
                new GitRepositoryProvenanceReader(processRunner));
            var result = service.StartAsync(new ServerStartRequest
            {
                Checkout = checkout,
                Framework = options.Framework,
                Configuration = options.Configuration,
                NoBuild = options.NoBuild,
                NoRestore = options.NoRestore,
                External = options.External,
                ApplicationArguments = options.ApplicationArguments
            }, cancellation.Token).GetAwaiter().GetResult();
            var commandResult = result.RunDirectory is null
                ? result.CommandResult
                : result.CommandResult with
                {
                    Message = result.CommandResult.Outcome == OutcomeKind.Success
                        ? $"{result.CommandResult.Message} Run evidence: {result.RunDirectory}"
                        : result.CommandResult.Message,
                    Details = result.CommandResult.Details is null
                        ? result.RunDirectory
                        : result.CommandResult.Details + Environment.NewLine + result.RunDirectory
                };
            return _renderer.Render(commandResult, output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not start the managed server command.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int Report(ReportCommandOptions options)
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
            var checkout = RepositoryPaths.Resolve(options.RepositoryPath, Environment.CurrentDirectory, Environment.ProcessPath);
            if (RejectStaleManagedHost(checkout) is { } staleExitCode)
            {
                return staleExitCode;
            }

            var result = new ReportService(new ProcessRunner(), message => error.WriteLine(message)).GenerateAsync(new ReportRequest
            {
                Checkout = checkout,
                Group = options.Group,
                Top = options.Top,
                Path = options.Path,
                Strict = options.Strict
            }, cancellation.Token).GetAwaiter().GetResult();
            if (options.Json)
            {
                output.WriteLine(result.Document?.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) ?? "{}");
                return result.CommandResult.ExitCode;
            }
            if (result.Document is not null)
            {
                ReportTextRenderer.Render(result.Document, output);
                return result.CommandResult.ExitCode;
            }
            return _renderer.Render(result.CommandResult, output, error);
        }
        catch (InvalidOperationException exception)
        {
            if (options.Json)
            {
                output.WriteLine(new System.Text.Json.Nodes.JsonObject
                {
                    ["contractVersion"] = 1,
                    ["command"] = "report",
                    ["state"] = "failed",
                    ["outcome"] = OutcomeKind.ValidationFailure.ToString(),
                    ["errors"] = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonValue.Create(exception.Message))
                }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                return new CommandResult(OutcomeKind.ValidationFailure, exception.Message).ExitCode;
            }
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            var commandResult = new CommandResult(OutcomeKind.InfrastructureFailure, "Could not run the managed report command.", Details: exception.Message);
            if (!options.Json)
            {
                return _renderer.Render(commandResult, output, error);
            }
            output.WriteLine(new System.Text.Json.Nodes.JsonObject
            {
                ["contractVersion"] = 1,
                ["command"] = "report",
                ["state"] = "failed",
                ["outcome"] = commandResult.Outcome.ToString(),
                ["errors"] = new System.Text.Json.Nodes.JsonArray(System.Text.Json.Nodes.JsonValue.Create(commandResult.Message), System.Text.Json.Nodes.JsonValue.Create(exception.Message))
            }.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return commandResult.ExitCode;
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private int? RejectStaleManagedHost(CheckoutIdentity checkout)
    {
        var failure = ManagedHostFingerprint.Validate(checkout.RepositoryRoot, Environment.ProcessPath);
        return failure is null
            ? null
            : _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "The managed DevOps apphost is missing valid promotion evidence.", Details: failure), output, error);
    }
}
