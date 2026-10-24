using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Build;

internal sealed class BuildService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    string dotnetExecutable = "dotnet",
    IDotNetProjectInspector? projectInspector = null)
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public async Task<BuildResult> BuildAsync(BuildRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = Validate(request);
        if (validation is not null)
        {
            return new BuildResult(new CommandResult(OutcomeKind.ValidationFailure, validation), null, null, [], null);
        }

        ExecutionLease executionLease;
        try
        {
            executionLease = await ExecutionLease.AcquireSharedAsync(artifactPaths.RepositoryRoot, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BuildResult(new CommandResult(OutcomeKind.Cancelled, "Build was cancelled while waiting for the DevOps execution lease."), null, null, [], null);
        }
        catch (TimeoutException exception)
        {
            return new BuildResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not acquire the shared build lease.", Details: exception.Message), null, null, [], null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new BuildResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not acquire the shared build lease.", Details: exception.Message), null, null, [], null);
        }

        await using var lease = executionLease;

        RunManifest manifest;
        try
        {
            manifest = await runStore.StartAsync(
                "build",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["target"] = request.ProjectPath is null ? "solution" : request.ProjectPath,
                    ["configuration"] = request.Configuration,
                    ["framework"] = request.ProjectPath is null ? string.Empty : request.Framework ?? "net11.0",
                    ["runtimeIdentifier"] = request.RuntimeIdentifier ?? string.Empty,
                    ["noRestore"] = request.NoRestore.ToString().ToLowerInvariant()
                },
                sourceCommit: null,
                sourceDirty: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new BuildResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not create build run evidence.", Details: exception.Message), null, null, [], null);
        }

        var runDirectory = artifactPaths.GetBuildRunDirectory(manifest.RunId);
        try
        {
            var git = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (git.Outcome != OutcomeKind.Success)
            {
                return await FinishEarlyAsync(manifest, runDirectory, git.Outcome, "Could not record source provenance.", git.Failure, cancellationToken).ConfigureAwait(false);
            }

            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty, cancellationToken: cancellationToken).ConfigureAwait(false);
            var sdk = await sdkResolver.ResolveAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (sdk.Outcome != OutcomeKind.Success)
            {
                return await FinishEarlyAsync(manifest, runDirectory, sdk.Outcome, "Could not resolve the SDK selected by global.json.", sdk.Failure, cancellationToken).ConfigureAwait(false);
            }

            manifest = await runStore.UpdateRunningAsync(
                manifest,
                git.Commit,
                git.Branch,
                git.Dirty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dotnet-sdk-requested"] = sdk.RequestedVersion ?? string.Empty,
                    ["dotnet-sdk-selected"] = sdk.SelectedVersion ?? string.Empty,
                    ["dotnet-sdk-roll-forward"] = sdk.RollForward ?? "default"
                },
                cancellationToken).ConfigureAwait(false);

            if (projectInspector is not null)
            {
                if (request.ProjectPath is null)
                {
                    var solution = await projectInspector.InspectSolutionAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
                    if (solution.Outcome != OutcomeKind.Success)
                    {
                        return await FinishEarlyAsync(manifest, runDirectory, solution.Outcome, "Could not inspect the LeanCorpus solution.", solution.Failure, cancellationToken).ConfigureAwait(false);
                    }

                    manifest = await runStore.UpdateRunningAsync(
                        manifest,
                        git.Commit,
                        git.Branch,
                        git.Dirty,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["solution-project-count"] = solution.Projects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["solution-format"] = "slnx",
                            ["solution-graph-probe"] = solution.MatchesDotNetCli ? "solution-persistence-matched-dotnet-sln" : "dotnet-sln-list-fallback"
                        },
                        cancellationToken,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["solution-graph-probe-detail"] = solution.Failure ?? "SolutionPersistence project paths matched dotnet sln list."
                        }).ConfigureAwait(false);
                }
                else
                {
                    var inspection = await projectInspector.InspectProjectAsync(
                        request.Checkout,
                        request.ProjectPath,
                        request.Configuration,
                        request.Framework ?? "net11.0",
                        sdk.SdkDirectory ?? string.Empty,
                        runDirectory,
                        cancellationToken).ConfigureAwait(false);
                    if (inspection.Outcome != OutcomeKind.Success)
                    {
                        return await FinishEarlyAsync(manifest, runDirectory, inspection.Outcome, "Could not inspect the selected build project.", inspection.Failure, cancellationToken).ConfigureAwait(false);
                    }

                    manifest = await runStore.UpdateRunningAsync(
                        manifest,
                        git.Commit,
                        git.Branch,
                        git.Dirty,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["msbuild-probe"] = inspection.UsedMsBuildApi && inspection.ApiMatchesCli ? "locator-api-matched-dotnet-cli" : "dotnet-cli-fallback",
                            ["target-framework"] = inspection.TargetFramework ?? request.Framework ?? "net11.0",
                            ["target-path"] = inspection.TargetPath ?? string.Empty,
                            ["msbuild-version"] = inspection.MsBuildVersion ?? "sdk-cli"
                        },
                        cancellationToken,
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["msbuild-probe-detail"] = inspection.Failure ?? "SDK-selected MSBuild API matched dotnet msbuild -getProperty."
                        }).ConfigureAwait(false);
                }
            }

            var arguments = BuildArguments(request, runDirectory);
            manifest = await runStore.UpdateRunningAsync(
                manifest,
                git.Commit,
                git.Branch,
                git.Dirty,
                cancellationToken: cancellationToken,
                additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["effectiveArguments"] = System.Text.Json.JsonSerializer.Serialize(arguments)
                }).ConfigureAwait(false);
            var child = await processRunner.RunAsync(new ProcessRequest
            {
                ExecutablePath = dotnetExecutable,
                Arguments = arguments,
                WorkingDirectory = request.Checkout.RepositoryRoot,
                StandardOutputLogPath = Path.Combine(runDirectory, "process.stdout.log"),
                StandardErrorLogPath = Path.Combine(runDirectory, "process.stderr.log"),
                ProgressInterval = TimeSpan.FromSeconds(5)
            }, cancellationToken).ConfigureAwait(false);

            var terminalState = child.Outcome switch
            {
                OutcomeKind.Success => RunState.Succeeded,
                OutcomeKind.Cancelled => RunState.Cancelled,
                OutcomeKind.Timeout => RunState.TimedOut,
                _ => RunState.Failed
            };
            var artifacts = new List<string> { "process.stdout.log", "process.stderr.log", "sdk-version.stdout.log", "sdk-version.stderr.log", "sdk-list.stdout.log", "sdk-list.stderr.log", "git-commit.stdout.log", "git-commit.stderr.log", "git-branch.stdout.log", "git-branch.stderr.log", "git-status.stdout.log", "git-status.stderr.log" };
            if (projectInspector is not null && request.ProjectPath is null)
            {
                artifacts.Add("solution-list.stdout.log");
                artifacts.Add("solution-list.stderr.log");
            }
            if (request.ProjectPath is not null && projectInspector is not null)
            {
                artifacts.Add("msbuild-cli.stdout.log");
                artifacts.Add("msbuild-cli.stderr.log");
            }
            if (request.LoggingPolicy.BinaryLog)
            {
                artifacts.Add("build.binlog");
            }

            if (request.LoggingPolicy.TextLog)
            {
                artifacts.Add("build.log");
            }

            var failure = child.Failure;
            manifest = await runStore.FinishAsync(
                manifest,
                terminalState,
                child.RawExitCode,
                artifacts,
                failure,
                CancellationToken.None).ConfigureAwait(false);
            var message = child.Outcome switch
            {
                OutcomeKind.Success => "Build succeeded.",
                OutcomeKind.CommandFailure => $"dotnet build failed with exit code {child.RawExitCode}.",
                OutcomeKind.Cancelled => "Build was cancelled.",
                OutcomeKind.Timeout => "Build timed out.",
                OutcomeKind.ValidationFailure => "Build request failed validation.",
                _ => "Build infrastructure failed."
            };
            var commandResult = new CommandResult(child.Outcome, message, child.RawExitCode, failure);
            return new BuildResult(commandResult, manifest.RunId, runDirectory, arguments, manifest);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.Cancelled, "Build was cancelled.", exception.Message, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return await FinishEarlyAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure, "Build infrastructure failed.", exception.Message, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private string? Validate(BuildRequest request)
    {
        if (request.Checkout is null || !Directory.Exists(request.Checkout.RepositoryRoot))
        {
            return "A valid LeanCorpus checkout is required.";
        }

        if (!PathsEqual(artifactPaths.RepositoryRoot, request.Checkout.RepositoryRoot))
        {
            return "The build service artefact root does not match the selected LeanCorpus checkout.";
        }

        if (string.IsNullOrWhiteSpace(request.Configuration) || request.Configuration.Contains('\n') || request.Configuration.Contains('\r'))
        {
            return "Build configuration must be a non-empty single-line value.";
        }

        if (request.ProjectPath is null)
        {
            if (request.Framework is not null)
            {
                return "A target framework can be selected only for a project build.";
            }

            if (!File.Exists(RepositoryPaths.GetSolutionPath(request.Checkout.RepositoryRoot)))
            {
                return "The LeanCorpus solution file is missing.";
            }
        }
        else
        {
            string projectFullPath;
            try
            {
                projectFullPath = Path.GetFullPath(Path.Combine(request.Checkout.RepositoryRoot, request.ProjectPath));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return $"Project path '{request.ProjectPath}' is invalid: {exception.Message}";
            }

            if (!IsWithin(request.Checkout.RepositoryRoot, projectFullPath) || !File.Exists(projectFullPath) || !projectFullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return $"Project path '{request.ProjectPath}' must identify an existing project inside the selected checkout.";
            }

            var framework = request.Framework ?? "net11.0";
            if (!Regex.IsMatch(framework, "^net[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant))
            {
                return $"Target framework '{framework}' is not a supported framework moniker.";
            }
        }

        if (request.RuntimeIdentifier is { } rid && !Regex.IsMatch(rid, "^[A-Za-z0-9]+(?:-[A-Za-z0-9]+)*$", RegexOptions.CultureInvariant))
        {
            return $"Runtime identifier '{rid}' is invalid.";
        }

        return null;
    }

    private static IReadOnlyList<string> BuildArguments(BuildRequest request, string runDirectory)
    {
        var targetPath = request.ProjectPath is null
            ? RepositoryPaths.GetSolutionPath(request.Checkout.RepositoryRoot)
            : Path.GetFullPath(Path.Combine(request.Checkout.RepositoryRoot, request.ProjectPath));
        var arguments = new List<string> { "build", targetPath, "-c", request.Configuration };
        if (request.ProjectPath is not null)
        {
            arguments.Add("-f");
            arguments.Add(request.Framework ?? "net11.0");
        }

        if (request.RuntimeIdentifier is not null)
        {
            arguments.Add("-r");
            arguments.Add(request.RuntimeIdentifier);
        }

        if (request.NoRestore)
        {
            arguments.Add("--no-restore");
        }

        arguments.AddRange(["--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false", "--tl:off"]);
        if (request.LoggingPolicy.BinaryLog)
        {
            arguments.Add($"-bl:{Path.Combine(runDirectory, "build.binlog")}");
        }

        if (request.LoggingPolicy.TextLog)
        {
            arguments.Add("-fl");
            arguments.Add($"-flp:logfile={Path.Combine(runDirectory, "build.log")};verbosity=normal");
        }

        return arguments.ToImmutableArray();
    }

    private async Task<BuildResult> FinishEarlyAsync(
        RunManifest manifest,
        string runDirectory,
        OutcomeKind outcome,
        string message,
        string? details,
        CancellationToken cancellationToken)
    {
        var state = outcome switch
        {
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };

        try
        {
            var final = await runStore.FinishAsync(manifest, state, failure: details, cancellationToken: cancellationToken).ConfigureAwait(false);
            return new BuildResult(new CommandResult(outcome, message, Details: details), final.RunId, runDirectory, [], final);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new BuildResult(
                new CommandResult(OutcomeKind.InfrastructureFailure, "Could not finalise build run evidence.", Details: $"{details} {exception.Message}".Trim()),
                manifest.RunId,
                runDirectory,
                [],
                null);
        }
    }

    private static bool PathsEqual(string left, string right)
        => string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), PathComparison);

    private static bool IsWithin(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }
}
