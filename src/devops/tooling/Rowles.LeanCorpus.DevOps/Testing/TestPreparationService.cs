using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record TestPreparationRequest
{
    public required CheckoutIdentity Checkout { get; init; }
    public IReadOnlyList<TestTarget> Targets { get; init; } = [];
    public bool Ci { get; init; }
    public bool NoRestore { get; init; }
}

internal sealed record PreparedTestTarget(TestTarget Target, string ProjectPath, string ExecutablePath);

internal sealed record TestPreparationTiming(
    string Stage,
    string Operation,
    string WorkItem,
    IReadOnlyList<string> TargetKeys,
    double DurationMs);

internal sealed record TestPreparationResult(
    OutcomeKind Outcome,
    IReadOnlyList<PreparedTestTarget> Targets,
    IReadOnlyList<ProcessResult> Processes,
    string? Failure)
{
    public IReadOnlyList<TestPreparationTiming> Timings { get; init; } = [];
}

internal sealed class TestPreparationService(IProcessRunner processRunner)
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public async Task<TestPreparationResult> PrepareAsync(
        TestPreparationRequest request,
        string runDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        if (request.Targets.Count == 0)
        {
            return Failure(OutcomeKind.ValidationFailure, "No test targets were selected.");
        }

        var root = Path.GetFullPath(request.Checkout.RepositoryRoot);
        var outputDirectory = Path.GetFullPath(runDirectory);
        Directory.CreateDirectory(outputDirectory);
        var prepared = new List<PreparedTestTarget>();
        var processes = new List<ProcessResult>();
        var restored = new HashSet<string>(PathComparer);
        var built = new HashSet<string>(PathComparer);
        var published = new HashSet<string>(PathComparer);
        var mtpExecutables = new Dictionary<string, string>(PathComparer);
        var aotExecutables = new Dictionary<string, string>(PathComparer);
        var timings = new List<TestPreparationTiming>();
        var sequence = 0;

        try
        {
            foreach (var target in request.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var projectPath = Path.GetFullPath(Path.Combine(root, target.Project.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsWithin(root, projectPath) || !projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || !File.Exists(projectPath))
                {
                    return Failure(OutcomeKind.ValidationFailure,
                        $"Test project '{target.Project}' must resolve to an existing project inside the selected checkout.", prepared, processes,
                        timings: timings);
                }

                if (target.RunnerKind == "Mtp")
                {
                    var projectKey = $"{projectPath}|{target.Framework}|{target.Configuration}";
                    if (mtpExecutables.TryGetValue(projectKey, out var cachedExecutable))
                    {
                        prepared.Add(new PreparedTestTarget(target, projectPath, cachedExecutable));
                        continue;
                    }

                    if (!request.Ci && !request.NoRestore && restored.Add(projectKey))
                    {
                        var restore = await RunDotNetAsync(root, outputDirectory, ++sequence,
                            ["restore", projectPath, "--nologo", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false"],
                            TimeSpan.FromMinutes(15), "restore", target, timings, cancellationToken).ConfigureAwait(false);
                        processes.Add(restore);
                        if (restore.Outcome != OutcomeKind.Success)
                        {
                            return Failure(restore.Outcome, $"Restore failed for test project '{target.Project}'.", prepared, processes, restore.Failure, timings);
                        }
                    }

                    if (!request.Ci && built.Add(projectKey))
                    {
                        var build = await RunDotNetAsync(root, outputDirectory, ++sequence,
                            ["build", projectPath, "--configuration", target.Configuration, "--framework", target.Framework,
                                "--no-restore", "--nologo", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false"],
                            TimeSpan.FromMinutes(20), "build", target, timings, cancellationToken).ConfigureAwait(false);
                        processes.Add(build);
                        if (build.Outcome != OutcomeKind.Success)
                        {
                            return Failure(build.Outcome, $"Build failed for test project '{target.Project}' ({target.Framework}).", prepared, processes,
                                build.Failure, timings);
                        }
                    }

                    var targetPathResult = await RunDotNetAsync(root, outputDirectory, ++sequence,
                        ["msbuild", projectPath, "--nologo", "--disable-build-servers", "-m:1", "-getProperty:TargetPath",
                            $"-property:Configuration={target.Configuration}", $"-property:TargetFramework={target.Framework}",
                            "-property:UseSharedCompilation=false"],
                        TimeSpan.FromMinutes(2), "resolve MTP target path", target, timings, cancellationToken).ConfigureAwait(false);
                    processes.Add(targetPathResult);
                    if (targetPathResult.Outcome != OutcomeKind.Success)
                    {
                        return Failure(targetPathResult.Outcome, $"Could not resolve the MTP target path for '{target.Project}'.", prepared, processes,
                            targetPathResult.Failure, timings);
                    }

                    var targetPaths = File.ReadLines(targetPathResult.StandardOutputLogPath)
                        .Select(static line => line.Trim())
                        .Where(static line => line.Length > 0 && Path.IsPathRooted(line) &&
                            Path.GetExtension(line).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                        .Distinct(PathComparer)
                        .ToArray();
                    if (targetPaths.Length != 1)
                    {
                        return Failure(OutcomeKind.InfrastructureFailure,
                            $"dotnet msbuild did not return one TargetPath for '{target.Project}' ({target.Framework}, {target.Configuration}).",
                            prepared, processes, timings: timings);
                    }

                    var targetPath = Path.GetFullPath(targetPaths[0]);
                    if (!IsWithin(root, targetPath))
                    {
                        return Failure(OutcomeKind.ValidationFailure,
                            $"Resolved test TargetPath '{targetPath}' is outside the selected checkout.", prepared, processes, timings: timings);
                    }
                    var projectName = Path.GetFileNameWithoutExtension(projectPath);
                    var executableDirectory = Path.GetDirectoryName(targetPath)!;
                    var executableCandidates = OperatingSystem.IsWindows()
                        ? new[] { Path.Combine(executableDirectory, projectName + ".exe"), Path.Combine(executableDirectory, projectName) }
                        : new[] { Path.Combine(executableDirectory, projectName) };
                    var executablePath = executableCandidates.FirstOrDefault(File.Exists);
                    if (executablePath is null)
                    {
                        return Failure(OutcomeKind.InfrastructureFailure,
                            $"Managed test output is missing its executable beside TargetPath '{targetPath}'.", prepared, processes, timings: timings);
                    }

                    var resolvedExecutable = Path.GetFullPath(executablePath);
                    mtpExecutables.Add(projectKey, resolvedExecutable);
                    prepared.Add(new PreparedTestTarget(target, projectPath, resolvedExecutable));
                    continue;
                }

                if (target.RunnerKind != "AotNative")
                {
                    return Failure(OutcomeKind.ValidationFailure,
                        $"Unsupported runner kind '{target.RunnerKind}' for test target '{target.Key}'.", prepared, processes, timings: timings);
                }
                if (string.IsNullOrWhiteSpace(target.RuntimeIdentifier))
                {
                    return Failure(OutcomeKind.ValidationFailure, $"AOT target '{target.Key}' requires a runtime identifier.", prepared, processes,
                        timings: timings);
                }

                var publishKey = $"{projectPath}|{target.Framework}|{target.RuntimeIdentifier}|{target.Configuration}";
                if (aotExecutables.TryGetValue(publishKey, out var cachedNativeExecutable))
                {
                    prepared.Add(new PreparedTestTarget(target, projectPath, cachedNativeExecutable));
                    continue;
                }

                if (published.Add(publishKey))
                {
                    var publishArguments = new List<string>
                    {
                        "publish", projectPath, "--configuration", target.Configuration,
                        "--runtime", target.RuntimeIdentifier, "--self-contained", "true",
                        "--framework", target.Framework, "--nologo", "-p:UseSharedCompilation=false"
                    };
                    if (request.NoRestore)
                    {
                        publishArguments.Add("--no-restore");
                    }
                    var publish = await RunDotNetAsync(root, outputDirectory, ++sequence, publishArguments,
                        TimeSpan.FromMinutes(45), "publish native AOT", target, timings, cancellationToken).ConfigureAwait(false);
                    processes.Add(publish);
                    if (publish.Outcome != OutcomeKind.Success)
                    {
                        return Failure(publish.Outcome, $"Native AOT publish failed for '{target.Project}' ({target.Framework}, {target.RuntimeIdentifier}).",
                            prepared, processes, publish.Failure, timings);
                    }
                }

                var configuration = target.Configuration.ToLowerInvariant();
                var publishDirectory = Path.Combine(root, "artifacts", "publish", Path.GetFileNameWithoutExtension(projectPath),
                    $"{configuration}_{target.Framework}_{target.RuntimeIdentifier}");
                var nativeName = Path.GetFileNameWithoutExtension(projectPath) + (target.RuntimeIdentifier.StartsWith("win-", StringComparison.OrdinalIgnoreCase) ? ".exe" : string.Empty);
                var nativePath = Path.Combine(publishDirectory, nativeName);
                if (!IsWithin(Path.Combine(root, "artifacts", "publish"), nativePath) || !File.Exists(nativePath))
                {
                    return Failure(OutcomeKind.InfrastructureFailure,
                        $"AOT publish completed but the expected native test executable is missing: {nativePath}", prepared, processes, timings: timings);
                }
                aotExecutables.Add(publishKey, nativePath);
                prepared.Add(new PreparedTestTarget(target, projectPath, nativePath));
            }

            return new TestPreparationResult(OutcomeKind.Success, prepared, processes, null) { Timings = timings.ToArray() };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure(OutcomeKind.Cancelled, "Test target preparation was cancelled.", prepared, processes, timings: timings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return Failure(OutcomeKind.InfrastructureFailure, "Could not prepare test targets.", prepared, processes, exception.Message, timings);
        }
    }

    private async Task<ProcessResult> RunDotNetAsync(
        string repositoryRoot,
        string runDirectory,
        int sequence,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        string operation,
        TestTarget target,
        ICollection<TestPreparationTiming> timings,
        CancellationToken cancellationToken)
    {
        var prefix = $"preparation-{sequence:D3}";
        var result = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = "dotnet",
            Arguments = arguments,
            WorkingDirectory = repositoryRoot,
            StandardOutputLogPath = Path.Combine(runDirectory, prefix + ".stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, prefix + ".stderr.log"),
            Timeout = timeout,
            ProgressInterval = TimeSpan.FromSeconds(5)
        }, cancellationToken).ConfigureAwait(false);
        timings.Add(new TestPreparationTiming("Preparation", operation,
            $"{target.Project}|{target.Framework}|{target.Configuration}|{target.RuntimeIdentifier}", [target.Key],
            Math.Round(result.Duration.TotalMilliseconds, 3)));
        return result;
    }

    private static TestPreparationResult Failure(
        OutcomeKind outcome,
        string failure,
        IReadOnlyList<PreparedTestTarget>? targets = null,
        IReadOnlyList<ProcessResult>? processes = null,
        string? details = null,
        IReadOnlyList<TestPreparationTiming>? timings = null)
        => new TestPreparationResult(outcome, targets ?? [], processes ?? [], details is null ? failure : $"{failure} {details}")
        {
            Timings = timings ?? []
        };

    private static bool IsWithin(string root, string path)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static StringComparer PathComparer
        => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
