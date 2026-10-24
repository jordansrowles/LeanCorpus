using System.Diagnostics;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Data;
using Rowles.LeanCorpus.DevOps.Diagnostics;
using Rowles.LeanCorpus.DevOps.Distribution;
using Rowles.LeanCorpus.DevOps.Documentation;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Packaging;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Reporting;
using Rowles.LeanCorpus.DevOps.Server;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;
using Spectre.Console;

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

    public int Pack(PackCommandOptions options)
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
            if (options.ValidatePackages && options.CompressionOnly)
            {
                return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure,
                    "Package validation requires the complete shipping package set and cannot be combined with compression-only packing."), output, error);
            }

            var checkout = RepositoryPaths.Resolve(options.RepositoryPath, Environment.CurrentDirectory, Environment.ProcessPath);
            if (RejectStaleManagedHost(checkout) is { } staleExitCode)
            {
                return staleExitCode;
            }

            var paths = new ArtifactPaths(checkout.RepositoryRoot);
            var processRunner = new ProcessRunner();
            var provenanceReader = new GitRepositoryProvenanceReader(processRunner);
            var packResult = new PackageBuildService(
                paths,
                new ArtifactRunStore(paths, runCategory: "package"),
                processRunner,
                new DotNetSdkResolver(processRunner),
                provenanceReader).PackAsync(new PackageBuildRequest
                {
                    Checkout = checkout,
                    Configuration = options.Configuration,
                    CompressionOnly = options.CompressionOnly,
                    NoBuild = options.NoBuild,
                    Clean = options.Clean
                }, cancellation.Token).GetAwaiter().GetResult();

            if (packResult.CommandResult.Outcome != OutcomeKind.Success || !options.ValidatePackages && !options.ValidateCompression)
            {
                var packageCommandResult = WithRunEvidence(packResult.CommandResult, packResult.RunDirectory, "Packages written");
                return _renderer.Render(packageCommandResult, output, error);
            }

            var consumerResult = new PackageConsumerValidationService(
                paths,
                new ArtifactRunStore(paths, runCategory: "test"),
                processRunner,
                provenanceReader,
                progress: message => output.WriteLine(message)).ValidateAsync(new PackageConsumerValidationRequest
                {
                    Checkout = checkout,
                    PackageDirectory = Path.Combine(paths.ArtifactRoot, "package", "release"),
                    Scope = options.ValidatePackages ? "all" : "compression"
                }, cancellation.Token).GetAwaiter().GetResult();

            var result = consumerResult.CommandResult with
            {
                Message = consumerResult.CommandResult.Outcome == OutcomeKind.Success
                    ? $"Package consumer validation passed. Package output: {Path.Combine(paths.ArtifactRoot, "package", "release")}."
                    : consumerResult.CommandResult.Message,
                Details = JoinDetails(packResult.RunDirectory, consumerResult.AcceptancePath,
                    consumerResult.CommandResult.Details)
            };
            return _renderer.Render(result, output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not run the managed package command.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int Distribute(DistributeCommandOptions options)
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
            var result = new ReleaseDistributionService(
                paths,
                new ArtifactRunStore(paths, runCategory: "distribution"),
                processRunner,
                new DotNetSdkResolver(processRunner),
                new GitRepositoryProvenanceReader(processRunner)).BuildAsync(new ReleaseDistributionRequest
                {
                    Checkout = checkout,
                    RuntimeIdentifier = options.RuntimeIdentifier,
                    Configuration = options.Configuration,
                    SelfContained = options.SelfContained
                }, cancellation.Token).GetAwaiter().GetResult();

            var commandResult = result.RunDirectory is null
                ? result.CommandResult
                : result.CommandResult with
                {
                    Message = result.CommandResult.Outcome == OutcomeKind.Success
                        ? $"Release archive created. Run evidence: {result.RunDirectory}"
                        : result.CommandResult.Message,
                    Details = string.Join(Environment.NewLine, new[] { result.CommandResult.Details, result.RunDirectory }
                        .Where(static value => !string.IsNullOrWhiteSpace(value)))
                };
            return _renderer.Render(commandResult, output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not build a DevOps release distribution.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int Coverage(CoverageCommandOptions options)
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
            var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
            var result = new CoverageService(
                paths,
                new ArtifactRunStore(paths, runCategory: "coverage"),
                new CleanService(paths, new ArtifactRunStore(paths)),
                new TestRunPlanner(configuration, new AffectedSelectionService(processRunner)),
                new TestExecutor(
                    paths,
                    new ArtifactRunStore(paths, runCategory: "test"),
                    processRunner,
                    new DotNetSdkResolver(processRunner),
                    new GitRepositoryProvenanceReader(processRunner),
                    progress: progress => output.WriteLine($"{progress.Kind}: {progress.Message ?? progress.TargetKey ?? string.Empty}")),
                processRunner).RunAsync(new CoverageRequest(
                    checkout,
                    options.Suite,
                    options.Framework,
                    options.Configuration,
                    options.Clean,
                    options.IncludePerformance,
                    options.GenerateReport,
                    "./devops coverage"), cancellation.Token).GetAwaiter().GetResult();

            var commandResult = result.CommandResult with
            {
                Message = result.CommandResult.Outcome == OutcomeKind.Success
                    ? $"Coverage run completed with {result.CoverageFileCount} Cobertura file(s). Evidence: {result.RunDirectory}"
                    : result.CommandResult.Message,
                Details = JoinDetails(result.RunDirectory, result.CommandResult.Details)
            };
            return _renderer.Render(commandResult, output, error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.Cancelled, "Coverage was cancelled."), output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Could not run managed coverage.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int Test(TestCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.RunOptions);

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
            var provenanceReader = new GitRepositoryProvenanceReader(processRunner);
            var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
            var planner = new TestRunPlanner(configuration, new AffectedSelectionService(processRunner));
            if (options.List)
            {
                WriteTestSuiteList(configuration, output);
                return 0;
            }

            if (options.ExplainAffected)
            {
                var selection = planner.ResolveSelectionAsync(checkout, options.RunOptions.Selection,
                    options.RunOptions.Commit, options.RunOptions.Range, cancellation.Token).GetAwaiter().GetResult();
                var affected = selection.AffectedSelection
                    ?? throw new InvalidOperationException("-ExplainAffected is valid only for the affected suite.");
                var source = affected.SuppliedCommit + affected.SuppliedRange;
                output.WriteLine($"Affected source: {affected.Mode}{(source.Length == 0 ? string.Empty : $" {source}")}");
                output.WriteLine($"Changed files: {affected.Paths.Count}");
                output.WriteLine($"Selected targets: {string.Join(", ", selection.Targets.Select(static target => target.Key))}");
                return 0;
            }

            var request = planner.PlanAsync(checkout, options.RunOptions, cancellation.Token).GetAwaiter().GetResult();
            var result = new TestExecutor(
                paths,
                new ArtifactRunStore(paths, runCategory: "test"),
                processRunner,
                new DotNetSdkResolver(processRunner),
                provenanceReader,
                progress: progress => output.WriteLine($"{progress.Kind}: {progress.Message ?? progress.TargetKey ?? string.Empty}"))
                .RunAsync(request, cancellation.Token).GetAwaiter().GetResult();
            var commandResult = result.RunDirectory.Length == 0
                ? result.CommandResult
                : result.CommandResult with
                {
                    Message = result.CommandResult.Outcome == OutcomeKind.Success
                        ? $"Test run completed. Evidence: {result.RunDirectory}"
                        : result.CommandResult.Message,
                    Details = JoinDetails(result.RunDirectory, result.CommandResult.Details)
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
                "Could not run the managed test command.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int BackupRestoreMatrix(BackupRestoreMatrixCommandOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.MatrixOptions);

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
            var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
            var provenanceReader = new GitRepositoryProvenanceReader(processRunner);
            var result = new BackupRestoreMatrixService(
                configuration,
                paths,
                processRunner,
                new DotNetSdkResolver(processRunner),
                provenanceReader).RunAsync(checkout, options.MatrixOptions, cancellation.Token).GetAwaiter().GetResult();
            var commandResult = result.RunDirectory.Length == 0
                ? result.CommandResult
                : WithRunEvidence(result.CommandResult, result.RunDirectory, "Backup/restore matrix completed");
            return _renderer.Render(commandResult, output, error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.Cancelled, "Backup/restore matrix was cancelled."), output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Could not run the managed backup/restore matrix.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int Docs(DocsCommandOptions options)
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
            var provenanceReader = new GitRepositoryProvenanceReader(processRunner);
            var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
            var service = new DocumentationCommandService(
                paths,
                new ArtifactRunStore(paths, runCategory: "docs"),
                processRunner,
                provenanceReader,
                new DocumentationSiteMetadataWriter(processRunner),
                new TestRunPlanner(configuration, new AffectedSelectionService(processRunner)),
                new TestExecutor(
                    paths,
                    new ArtifactRunStore(paths, runCategory: "test"),
                    processRunner,
                    new DotNetSdkResolver(processRunner),
                    provenanceReader,
                    progress: progress => output.WriteLine($"{progress.Kind}: {progress.Message ?? progress.TargetKey ?? string.Empty}")),
                configuration,
                progress: output.WriteLine);
            var commandLine = new List<string> { "./devops", "docs", options.Action };
            if (options.SkipBenchmarks) commandLine.Add("--skip-benchmarks");
            if (options.SkipCoverage) commandLine.Add("--skip-coverage");
            if (options.Action.Equals("test", StringComparison.OrdinalIgnoreCase))
            {
                commandLine.AddRange(["--framework", options.Framework]);
                if (options.NoRestore) commandLine.Add("--no-restore");
            }
            var result = service.RunAsync(new DocumentationCommandRequest
            {
                Checkout = checkout,
                Action = options.Action,
                SkipBenchmarks = options.SkipBenchmarks,
                SkipCoverage = options.SkipCoverage,
                Framework = options.Framework,
                NoRestore = options.NoRestore,
                CommandLine = string.Join(' ', commandLine)
            }, cancellation.Token).GetAwaiter().GetResult();
            return _renderer.Render(result.CommandResult, output, error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.Cancelled, "Managed docs command was cancelled."), output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Could not run managed documentation.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int Data(DataCommandOptions options)
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
            if (!options.Dataset.Equals("gutenberg", StringComparison.OrdinalIgnoreCase) &&
                !options.Dataset.Equals("migration-fixtures-311", StringComparison.OrdinalIgnoreCase))
            {
                return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure,
                    $"Unknown data set '{options.Dataset}'. Valid data sets: gutenberg, migration-fixtures-311."), output, error);
            }

            var checkout = RepositoryPaths.Resolve(options.RepositoryPath, Environment.CurrentDirectory, Environment.ProcessPath);
            if (RejectStaleManagedHost(checkout) is { } staleExitCode)
            {
                return staleExitCode;
            }

            var paths = new ArtifactPaths(checkout.RepositoryRoot);
            if (options.Dataset.Equals("migration-fixtures-311", StringComparison.OrdinalIgnoreCase))
            {
                var processRunner = new ProcessRunner();
                var fixtureResult = new MigrationFixtureGenerationService(
                    paths,
                    new ArtifactRunStore(paths, runCategory: "data"),
                    processRunner,
                    new GitRepositoryProvenanceReader(processRunner),
                    message => output.WriteLine(message)).GenerateAsync(new MigrationFixtureGenerationRequest(
                        checkout, options.PackageSource, options.OutputDirectory), cancellation.Token).GetAwaiter().GetResult();
                var fixtureCommandResult = fixtureResult.CommandResult with
                {
                    Message = fixtureResult.CommandResult.Outcome == OutcomeKind.Success
                        ? $"{fixtureResult.CommandResult.Message} Run evidence: {fixtureResult.RunDirectory}"
                        : fixtureResult.CommandResult.Message,
                    Details = JoinDetails(fixtureResult.RunDirectory, fixtureResult.CommandResult.Details)
                };
                return _renderer.Render(fixtureCommandResult, output, error);
            }

            if (options.BookCount < 1)
                return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, "Gutenberg -BookCount must be a positive integer."), output, error);

            using var httpClient = new HttpClient();
            var result = new GutenbergDownloadService(httpClient, message => output.WriteLine(message))
                .DownloadAsync(new GutenbergDownloadRequest
                {
                    RepositoryRoot = checkout.RepositoryRoot,
                    BookCount = options.BookCount,
                    OutputDirectory = options.OutputDirectory
                }, cancellation.Token).GetAwaiter().GetResult();
            return _renderer.Render(result.CommandResult, output, error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.Cancelled, "Gutenberg data preparation was cancelled."), output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not run the managed data command.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int DataForge(DataForgeCommandOptions options)
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
            if (options.Arguments.FirstOrDefault()?.Equals("qualify", StringComparison.OrdinalIgnoreCase) is true)
            {
                var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
                var parsed = DataForgeQualificationArgumentParser.Parse(options.Arguments.Skip(1).ToArray(), checkout.RepositoryRoot);
                if (!parsed.Success || parsed.Options is null)
                {
                    return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure,
                        parsed.Failure ?? "Invalid DataForge qualification arguments."), output, error);
                }

                var qualification = new DataForgeQualificationService(
                    paths,
                    new AtomicFileWriter(),
                    new DataForgeQualificationStepRunner(paths, processRunner, new DotNetSdkResolver(processRunner),
                        new GitRepositoryProvenanceReader(processRunner),
                        configuration.TestSuites,
                        progress: message => output.WriteLine(message),
                        errorOutput: message => error.WriteLine(message)),
                    configuration,
                    new GitRepositoryProvenanceReader(processRunner),
                    new DotNetSdkResolver(processRunner),
                    progress: message => output.WriteLine(message),
                    errorOutput: message => error.WriteLine(message));
                var qualificationResult = qualification.RunAsync(checkout, parsed.Options, cancellation.Token).GetAwaiter().GetResult();
                var qualificationCommandResult = qualificationResult.CommandResult with
                {
                    Details = JoinDetails(qualificationResult.QualificationDirectory, qualificationResult.CommandResult.Details)
                };
                return _renderer.Render(qualificationCommandResult, output, error);
            }

            var result = new DataForgeCommandService(
                paths,
                new ArtifactRunStore(paths, runCategory: "dataforge"),
                processRunner,
                new DotNetSdkResolver(processRunner),
                new GitRepositoryProvenanceReader(processRunner),
                outputProgress: message => output.WriteLine(message),
                errorOutput: message => error.WriteLine(message)).RunAsync(new DataForgeCommandRequest(checkout, options.Arguments),
                cancellation.Token).GetAwaiter().GetResult();
            var commandResult = result.CommandResult with
            {
                Message = result.CommandResult.Outcome == OutcomeKind.Success
                    ? $"{result.CommandResult.Message} Run evidence: {result.RunDirectory}"
                    : result.CommandResult.Message,
                Details = JoinDetails(result.RunDirectory, result.CommandResult.Details)
            };
            return _renderer.Render(commandResult, output, error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.Cancelled, "DataForge was cancelled."), output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not run the managed DataForge command.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int Diagnostics(DiagnosticsCommandOptions options)
    {
        var parsed = DiagnosticsArgumentParser.Parse(options.Arguments, Environment.CurrentDirectory);
        if (!parsed.Success || parsed.Request is null)
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, parsed.Failure ?? "Invalid diagnostics arguments."), output, error);
        if (parsed.Request.Operation == DiagnosticsOperation.Help)
        {
            ShowDiagnosticsHelp();
            return 0;
        }
        if (parsed.Request.Operation == DiagnosticsOperation.ProcessList)
            return ListProcesses();

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
            var paths = new ArtifactPaths(checkout.RepositoryRoot);
            var processRunner = new ProcessRunner();
            var service = new DiagnosticsCommandService(
                paths,
                new ArtifactRunStore(paths, runCategory: "diagnostics"),
                processRunner,
                new DotNetSdkResolver(processRunner),
                new GitRepositoryProvenanceReader(processRunner),
                progress: message => output.WriteLine(message),
                errorOutput: message => error.WriteLine(message));
            var result = service.RunAsync(new DiagnosticsCommandRequest(checkout, parsed.Request), cancellation.Token)
                .GetAwaiter().GetResult();
            var commandResult = result.RunDirectory is null
                ? result.CommandResult
                : result.CommandResult with
                {
                    Message = result.CommandResult.Outcome == OutcomeKind.Success
                        ? $"{result.CommandResult.Message} Run evidence: {result.RunDirectory}"
                        : result.CommandResult.Message,
                    Details = JoinDetails(result.RunDirectory, result.CommandResult.Details)
                };
            return _renderer.Render(commandResult, output, error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.Cancelled, "Diagnostics were cancelled."), output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not run managed diagnostics.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private void ShowDiagnosticsHelp()
    {
        output.WriteLine();
        output.WriteLine("  Diagnostics commands:");
        output.WriteLine();
        output.WriteLine("    ./devops diagnostics ps");
        output.WriteLine("    ./devops diagnostics counters --pid <pid> [-- <tool arguments>]");
        output.WriteLine("    ./devops diagnostics trace --pid <pid> [-- <tool arguments>]");
        output.WriteLine("    ./devops diagnostics gcdump --pid <pid> [-- <tool arguments>]");
        output.WriteLine("    ./devops diagnostics dump --pid <pid> [--type Mini|Heap|Full|Triage]");
        output.WriteLine("    ./devops diagnostics symbols <artifact> [-- <tool arguments>]");
        output.WriteLine("    ./devops diagnostics capture --pid <pid> [--duration 5s]");
        output.WriteLine();
        output.WriteLine("  Trace, GC dump, dump and capture output is written under artifacts/diagnostics/runs.");
        output.WriteLine("  Dumps may contain sensitive application memory.");
        output.WriteLine();
    }

    private int ListProcesses()
    {
        output.WriteLine("Id\tProcessName");
        foreach (var process in Process.GetProcesses().OrderBy(static process => process.Id))
        {
            using (process)
            {
                try
                {
                    output.WriteLine($"{process.Id}\t{process.ProcessName}");
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
            }
        }
        return 0;
    }

    public int Benchmark(BenchmarkCommandOptions options)
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
            var result = new BenchmarkCommandService(
                paths,
                new ArtifactRunStore(paths, runCategory: "benchmark"),
                processRunner,
                new DotNetSdkResolver(processRunner),
                new GitRepositoryProvenanceReader(processRunner),
                progress: message => output.WriteLine(message),
                errorOutput: message => error.WriteLine(message)).RunAsync(
                    new BenchmarkCommandRequest(checkout, options), cancellation.Token).GetAwaiter().GetResult();
            return _renderer.Render(WithRunEvidence(result.CommandResult, result.RunDirectory, "Benchmark run completed"), output, error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.Cancelled, "Benchmark run was cancelled."), output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not run the managed benchmark command.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int BenchmarkRemote(BenchmarkRemoteCommandOptions options)
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
                return staleExitCode;

            var paths = new ArtifactPaths(checkout.RepositoryRoot);
            var runner = new ProcessRunner();
            var service = new RemoteBenchmarkCommandService(
                paths,
                new ArtifactRunStore(paths, runCategory: "benchmark"),
                runner,
                new GitRepositoryProvenanceReader(runner),
                progress: message => output.WriteLine(message));
            var result = service.RunAsync(checkout, options, cancellation.Token).GetAwaiter().GetResult();
            return _renderer.Render(WithRunEvidence(result.CommandResult, result.RunDirectory, "Remote benchmark command completed"), output, error);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.Cancelled, "Remote benchmark command was cancelled; remote state may be unknown."), output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not run the managed remote benchmark command.", Details: exception.Message), output, error);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    public int Ui(UiCommandOptions options)
    {
        try
        {
            var checkout = RepositoryPaths.Resolve(options.RepositoryPath, Environment.CurrentDirectory, Environment.ProcessPath);
            if (RejectStaleManagedHost(checkout) is { } staleExitCode)
            {
                return staleExitCode;
            }

            var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
            return new DevOpsWorkbench(
                AnsiConsole.Console,
                configuration,
                new ManagedWorkbenchOperations(this, checkout, output, error)).Run();
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Could not start the managed DevOps workbench.", Details: exception.Message), output, error);
        }
    }

    public int ValidateConfiguration(ConfigValidateCommandOptions options)
    {
        try
        {
            var checkout = RepositoryPaths.Resolve(options.RepositoryPath, Environment.CurrentDirectory, Environment.ProcessPath);
            var configuration = new ConfigurationLoader(checkout.RepositoryRoot).Load();
            if (options.Deep)
            {
                BenchmarkManifestValidator.ValidateBuiltHostsAsync(configuration, checkout.RepositoryRoot, new ProcessRunner())
                    .GetAwaiter().GetResult();
            }
            var validationMessage = options.Deep
                ? "Managed DevOps configuration and benchmark host manifests are valid"
                : "Managed DevOps configuration is valid";
            return _renderer.Render(new CommandResult(OutcomeKind.Success,
                $"{validationMessage}: {configuration.BenchmarkProjects.Count} benchmark project(s), {configuration.BenchmarkSuites.Count} benchmark suite(s)."),
                output, error);
        }
        catch (InvalidOperationException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), output, error);
        }
        catch (InvalidDataException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure,
                "Managed DevOps configuration is invalid.", Details: exception.Message), output, error);
        }
        catch (FileNotFoundException exception)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.ValidationFailure,
                "Managed DevOps configuration is incomplete.", Details: exception.Message), output, error);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Could not validate managed DevOps configuration.", Details: exception.Message), output, error);
        }
    }

    private int? RejectStaleManagedHost(CheckoutIdentity checkout)
    {
        var failure = ManagedHostFingerprint.Validate(checkout.RepositoryRoot, Environment.ProcessPath);
        return failure is null
            ? null
            : _renderer.Render(new CommandResult(OutcomeKind.InfrastructureFailure, "The managed DevOps apphost is missing valid promotion evidence.", Details: failure), output, error);
    }

    private static void WriteTestSuiteList(DevOpsConfiguration configuration, TextWriter output)
    {
        var resolver = new TestTargetResolver(configuration.TestSuites);
        output.WriteLine();
        output.WriteLine("  Available test suites (-Suite):");
        output.WriteLine();
        foreach (var key in resolver.GetOrderedSuiteKeys(includeExplicitOnly: true))
        {
            var suite = configuration.TestSuites[key];
            var frameworks = string.Join(", ", suite.Frameworks.Order(StringComparer.Ordinal));
            var scope = suite.IncludeInAll == false ? "explicit only" : "all";
            output.WriteLine($"    {key,-20} {suite.Name} ({suite.Runner}; {frameworks}; {scope})");
        }
        output.WriteLine("    all                  All test suites");
        output.WriteLine("    affected             Test suites for selected source areas");
        output.WriteLine();
    }

    private static CommandResult WithRunEvidence(CommandResult result, string? runDirectory, string successPrefix)
        => runDirectory is null
            ? result
            : result with
            {
                Message = result.Outcome == OutcomeKind.Success ? $"{successPrefix}. Run evidence: {runDirectory}" : result.Message,
                Details = JoinDetails(runDirectory, result.Details)
            };

    private static string? JoinDetails(params string?[] values)
    {
        var present = values.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return present.Length == 0 ? null : string.Join(Environment.NewLine, present);
    }
}
