using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed record PackageConsumerValidationRequest
{
    public required CheckoutIdentity Checkout { get; init; }

    public required string PackageDirectory { get; init; }

    public string Scope { get; init; } = "all";
}

internal sealed record PackageConsumerValidationResult(
    CommandResult CommandResult,
    Guid? RunId,
    string? RunDirectory,
    string? AcceptancePath,
    RunManifest? Manifest);

internal sealed class PackageConsumerValidationService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IRepositoryProvenanceReader provenanceReader,
    string dotnetExecutable = "dotnet",
    Action<string>? progress = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<PackageConsumerValidationResult> ValidateAsync(PackageConsumerValidationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = ValidateRequest(request);
        if (validation is not null)
        {
            return new PackageConsumerValidationResult(new CommandResult(OutcomeKind.ValidationFailure, validation), null, null, null, null);
        }

        ExecutionLease lease;
        try
        {
            lease = await ExecutionLease.AcquireSharedAsync(artifactPaths.RepositoryRoot, TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new PackageConsumerValidationResult(new CommandResult(OutcomeKind.Cancelled, "Package consumer validation was cancelled while waiting for the shared build lease."), null, null, null, null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
        {
            return new PackageConsumerValidationResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not acquire the shared build lease.", Details: exception.Message), null, null, null, null);
        }
        await using var executionLease = lease;

        RunManifest manifest;
        try
        {
            manifest = await runStore.StartAsync(
                request.Scope == "compression" ? "pack consumers compression" : "pack consumers",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["scope"] = request.Scope,
                    ["packageDirectory"] = Path.GetFullPath(request.PackageDirectory)
                },
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new PackageConsumerValidationResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not create consumer validation run evidence.", Details: exception.Message), null, null, null, null);
        }

        var runDirectory = artifactPaths.GetRunDirectory("test", manifest.RunId);
        var consumerRoot = Path.Combine(runDirectory, "consumer-projects");
        var cacheRoot = Path.Combine(runDirectory, "isolated-caches");
        var acceptancePath = Path.Combine(runDirectory, "acceptance.json");
        var results = new List<ScenarioAcceptance>();
        var completedDirectories = new List<string>();
        var completedCaches = new List<string>();
        var childExitCodes = new List<int>();
        var artifacts = new List<string>();
        PackageManifestIndex? packageIndex = null;
        var sourceCommit = (string?)null;
        var status = "failed";
        string? failure = null;
        OutcomeKind? failureOutcome = null;
        try
        {
            Directory.CreateDirectory(consumerRoot);
            Directory.CreateDirectory(cacheRoot);
            var git = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            AddGitArtifacts(artifacts);
            if (git.Outcome != OutcomeKind.Success)
            {
                var detail = string.IsNullOrWhiteSpace(git.Failure) ? string.Empty : $" {git.Failure}";
                throw new ConsumerFailureException(OutcomeKind.InfrastructureFailure, $"Could not record package consumer source provenance.{detail}");
            }
            sourceCommit = git.Commit;
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty, cancellationToken: cancellationToken).ConfigureAwait(false);

            try
            {
                packageIndex = PackageManifestReader.Read(request.PackageDirectory);
            }
            catch (InvalidDataException exception)
            {
                throw new ConsumerFailureException(OutcomeKind.ValidationFailure, exception.Message, exception);
            }
            if (!string.IsNullOrWhiteSpace(sourceCommit) && !string.Equals(packageIndex.SourceCommit, sourceCommit, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Package manifest source commit {packageIndex.SourceCommit} does not match the current checkout commit {sourceCommit}.");
            }
            sourceCommit = packageIndex.SourceCommit;
            var externalSources = await GetExternalSourcesAsync(request.Checkout.RepositoryRoot, runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
            var packagePolicy = PackagePolicyReader.Read(
                Path.Combine(request.Checkout.RepositoryRoot, "scripts", "devops", "packaging", "package-policy.json"),
                request.Checkout.RepositoryRoot);
            var scenarios = ConsumerScenarioCatalog.Create(request.Scope);
            foreach (var scenario in scenarios)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Invoke($"Package consumer {scenario.Name} {scenario.Framework} started.");
                var scenarioDirectory = Path.Combine(consumerRoot, $"{scenario.Name}-{scenario.Framework}");
                var cacheDirectory = Path.Combine(cacheRoot, $"{scenario.Name}-{scenario.Framework}");
                var scenarioPolicy = packagePolicy.Packages.SingleOrDefault(entry => entry.PackageId == scenario.PackageId);
                var row = await RunScenarioAsync(request, scenario, scenarioPolicy, packageIndex, externalSources,
                    scenarioDirectory, cacheDirectory, runDirectory, childExitCodes, artifacts, results, cancellationToken).ConfigureAwait(false);
                results.Add(row);
                completedDirectories.Add(scenarioDirectory);
                completedCaches.Add(cacheDirectory);
                progress?.Invoke($"Package consumer {scenario.Name} {scenario.Framework} passed.");
            }

            status = "passed";
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            status = "cancelled";
            failure = exception.Message;
            failureOutcome = OutcomeKind.Cancelled;
        }
        catch (ConsumerFailureException exception)
        {
            failure = exception.Message;
            failureOutcome = exception.Outcome;
        }
        catch (InvalidDataException exception)
        {
            failure = exception.Message;
            failureOutcome = OutcomeKind.ValidationFailure;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or System.Xml.XmlException)
        {
            failure = exception.Message;
            failureOutcome = OutcomeKind.InfrastructureFailure;
        }

        if (status == "passed")
        {
            try
            {
                foreach (var directory in completedDirectories)
                {
                    DeleteOwnedDirectory(directory, runDirectory);
                }
                foreach (var cache in completedCaches)
                {
                    DeleteOwnedDirectory(cache, runDirectory);
                }
                DeleteOwnedDirectory(consumerRoot, runDirectory);
                DeleteOwnedDirectory(cacheRoot, runDirectory);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                status = "failed";
                failure = $"Successful consumer working directory cleanup failed: {exception.Message}";
                failureOutcome = OutcomeKind.InfrastructureFailure;
            }
        }

        try
        {
            var acceptance = new AcceptanceDocument(1, sourceCommit, Path.Combine(request.PackageDirectory, "manifest.json"), DateTimeOffset.UtcNow,
                status, failure, results);
            await new AtomicFileWriter().WriteReplaceAsync(acceptancePath, JsonSerializer.SerializeToUtf8Bytes(acceptance, JsonOptions), CancellationToken.None).ConfigureAwait(false);
            artifacts.Add(Path.GetRelativePath(artifactPaths.ArtifactRoot, acceptancePath).Replace('\\', '/'));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            status = "failed";
            failure = $"Could not write package consumer acceptance evidence: {exception.Message}";
            failureOutcome = OutcomeKind.InfrastructureFailure;
        }

        var terminalState = status switch
        {
            "passed" => RunState.Succeeded,
            "cancelled" => RunState.Cancelled,
            _ => RunState.Failed
        };
        try
        {
            manifest = await runStore.FinishAsync(manifest, terminalState, artifacts: artifacts, failure: failure,
                cancellationToken: CancellationToken.None, childExitCodes: childExitCodes).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return new PackageConsumerValidationResult(new CommandResult(OutcomeKind.InfrastructureFailure, "Could not finalise package consumer run evidence.", Details: exception.Message), manifest.RunId, runDirectory, acceptancePath, manifest);
        }

        var outcome = status == "passed" ? OutcomeKind.Success : failureOutcome ?? OutcomeKind.CommandFailure;
        var message = status == "passed"
            ? $"Package consumer acceptance evidence: {acceptancePath}"
            : status == "cancelled" ? "Package consumer validation was cancelled." : "Package consumer validation failed.";
        return new PackageConsumerValidationResult(new CommandResult(outcome, message, Details: failure), manifest.RunId, runDirectory, acceptancePath, manifest);
    }

    private async Task<ScenarioAcceptance> RunScenarioAsync(
        PackageConsumerValidationRequest request,
        ConsumerScenario scenario,
        PackagePolicyEntry? packagePolicy,
        PackageManifestIndex packageIndex,
        IReadOnlyList<string> externalSources,
        string scenarioDirectory,
        string cacheDirectory,
        string runDirectory,
        List<int> childExitCodes,
        List<string> artifacts,
        List<ScenarioAcceptance> results,
        CancellationToken cancellationToken)
    {
        if (!packageIndex.Packages.TryGetValue(scenario.PackageId, out var primary))
        {
            throw new InvalidDataException($"Package '{scenario.PackageId}' is absent from the generated package manifest.");
        }
        Directory.CreateDirectory(scenarioDirectory);
        Directory.CreateDirectory(cacheDirectory);
        var row = new ScenarioAcceptance(scenario.Name, scenario.PackageId, primary.Version, primary.Sha256, scenario.Framework,
            "not-run", "not-run", "not-run", [], scenarioDirectory, cacheDirectory, "running", null);
        try
        {
            if (scenario.RuntimeKind == "compression")
            {
                if (packagePolicy is null)
                {
                    throw new InvalidDataException($"Package policy is missing {scenario.PackageId}.");
                }
                PackageArchiveInspector.ValidateCompressionDependencyRange(primary.Path, packagePolicy);
            }

            var consumer = PackageConsumerProjectWriter.Write(request.Checkout.RepositoryRoot, scenarioDirectory, scenario, packageIndex);
            var config = Path.Combine(scenarioDirectory, "NuGet.Config");
            PackageConsumerNuGetConfig.Write(config, request.PackageDirectory, externalSources);
            var restore = await RunChildAsync("restore", ["restore", consumer.ProjectPath, "--packages", cacheDirectory, "--configfile", config,
                "--disable-build-servers", "-p:NuGetAudit=false"], scenarioDirectory, scenarioDirectory, runDirectory, childExitCodes, artifacts,
                TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
            RequireSuccess(restore, "Consumer restore");
            row = row with { Restore = "passed" };

            var assetsPath = Path.Combine(scenarioDirectory, "obj", "project.assets.json");
            var verified = PackageConsumerAssetsVerifier.Verify(assetsPath, cacheDirectory, scenario, packageIndex);
            row = row with { VerifiedPackages = verified };

            var build = await RunChildAsync("build", ["build", consumer.ProjectPath, "-c", "Release", "--no-restore", "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false"],
                scenarioDirectory, scenarioDirectory, runDirectory, childExitCodes, artifacts, TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
            RequireSuccess(build, "Consumer build");
            row = row with { Build = "passed" };
            var consumerDll = Path.Combine(scenarioDirectory, "bin", "Release", scenario.Framework, "Consumer.dll");

            switch (scenario.RuntimeKind)
            {
                case "core":
                    await RunCoreScenarioAsync(consumerDll, scenarioDirectory, runDirectory, childExitCodes, artifacts, cancellationToken).ConfigureAwait(false);
                    row = row with { Runtime = "passed-write-read-cleanup" };
                    break;
                case "compression":
                    await RunCompressionScenarioAsync(consumer, consumerDll, scenario, primary, packageIndex, request.PackageDirectory, externalSources,
                        scenarioDirectory, cacheDirectory, runDirectory, childExitCodes, artifacts, cancellationToken).ConfigureAwait(false);
                    row = row with { Runtime = "passed-round-trip-marker-and-rejects-core-3.1.1" };
                    break;
                case "server-local":
                    await RunServerLocalScenarioAsync(consumerDll, scenario, packageIndex, cacheDirectory, scenarioDirectory, runDirectory,
                        childExitCodes, artifacts, cancellationToken).ConfigureAwait(false);
                    row = row with { Runtime = "passed-started-and-bound" };
                    break;
                default:
                    var runtime = await RunChildAsync("runtime", [consumerDll], scenarioDirectory, scenarioDirectory, runDirectory, childExitCodes, artifacts,
                        TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
                    RequireSuccess(runtime, "Consumer runtime");
                    var runtimeOutput = string.Join(Environment.NewLine, runtime.StandardOutputTail);
                    if (scenario.Kind == "text" && !runtimeOutput.Contains("TEXT quick|fox", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Text consumer output was not deterministic.");
                    }
                    if (scenario.Kind == "sourcegen" && !runtimeOutput.Contains("SOURCEGEN Product", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("SourceGen output did not prove generated mapper use.");
                    }
                    if (scenario.Kind == "orchard" && !runtimeOutput.Contains("ORCHARD module bootstrap passed", StringComparison.Ordinal))
                    {
                        throw new InvalidDataException("Orchard consumer did not finish its module bootstrap.");
                    }
                    row = row with { Runtime = "passed" };
                    break;
            }

            return row with { Status = "passed" };
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            row = row with { Status = "cancelled", Error = exception.Message };
            results.Add(row);
            File.WriteAllText(Path.Combine(scenarioDirectory, "exception.txt"), exception.ToString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException or System.ComponentModel.Win32Exception or System.Xml.XmlException or ConsumerFailureException)
        {
            row = row with { Status = "failed", Error = exception.Message };
            results.Add(row);
            File.WriteAllText(Path.Combine(scenarioDirectory, "exception.txt"), exception.ToString(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            throw;
        }
    }

    private async Task RunCoreScenarioAsync(string consumerDll, string workingDirectory, string runDirectory, List<int> childExitCodes, List<string> artifacts, CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(workingDirectory, "index");
        foreach (var mode in new[] { "write", "read" })
        {
            var result = await RunChildAsync($"runtime-{mode}", [consumerDll, mode, indexPath], workingDirectory, workingDirectory,
                runDirectory, childExitCodes, artifacts, TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            RequireSuccess(result, $"Core {mode} process");
        }
        DeleteOwnedDirectory(indexPath, workingDirectory);
    }

    private async Task RunCompressionScenarioAsync(
        PackageConsumerProjectFiles consumer,
        string consumerDll,
        ConsumerScenario scenario,
        PackageManifestPackage primary,
        PackageManifestIndex packageIndex,
        string packageDirectory,
        IReadOnlyList<string> externalSources,
        string workingDirectory,
        string cacheDirectory,
        string runDirectory,
        List<int> childExitCodes,
        List<string> artifacts,
        CancellationToken cancellationToken)
    {
        var indexPath = Path.Combine(workingDirectory, "index");
        foreach (var mode in new[] { "write", "read" })
        {
            var result = await RunChildAsync($"runtime-{mode}", [consumerDll, mode, indexPath], workingDirectory, workingDirectory,
                runDirectory, childExitCodes, artifacts, TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
            RequireSuccess(result, $"Compression {mode} process");
        }
        DeleteOwnedDirectory(indexPath, workingDirectory);

        var core = packageIndex.Packages["LeanCorpus"];
        var projectXml = await File.ReadAllTextAsync(consumer.ProjectPath, cancellationToken).ConfigureAwait(false);
        var pinnedCoreReference = $"Include=\"LeanCorpus\" Version=\"[{core.Version}]\"";
        if (!projectXml.Contains(pinnedCoreReference, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Compression consumer project does not contain the expected pinned Core package reference.");
        }
        await File.WriteAllTextAsync(consumer.ProjectPath, projectXml.Replace(pinnedCoreReference, "Include=\"LeanCorpus\" Version=\"[3.1.1]\"", StringComparison.Ordinal),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken).ConfigureAwait(false);
        var negativeConfig = Path.Combine(workingDirectory, "Negative.NuGet.Config");
        PackageConsumerNuGetConfig.Write(negativeConfig, packageDirectory, externalSources, allowExternalLeanCorpus: true);
        var negative = await RunChildAsync("negative-restore", ["restore", consumer.ProjectPath, "--packages", cacheDirectory, "--configfile", negativeConfig,
            "--disable-build-servers", "-p:NuGetAudit=false"], workingDirectory, workingDirectory, runDirectory, childExitCodes, artifacts,
            TimeSpan.FromMinutes(5), cancellationToken).ConfigureAwait(false);
        var negativeStdout = await File.ReadAllTextAsync(Path.Combine(workingDirectory, "negative-restore.stdout.log"), cancellationToken).ConfigureAwait(false);
        var negativeStderr = await File.ReadAllTextAsync(Path.Combine(workingDirectory, "negative-restore.stderr.log"), cancellationToken).ConfigureAwait(false);
        var negativeText = negativeStdout + Environment.NewLine + negativeStderr;
        if (negative.Outcome == OutcomeKind.Success || !negativeText.Contains("NU1605", StringComparison.Ordinal) && !negativeText.Contains("NU1107", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Expected the incompatible Core 3.1.1 restore to fail with NU1605/NU1107; see the negative restore logs.");
        }
        _ = scenario;
        _ = primary;
    }

    private async Task RunServerLocalScenarioAsync(
        string consumerDll,
        ConsumerScenario scenario,
        PackageManifestIndex packageIndex,
        string cacheDirectory,
        string workingDirectory,
        string runDirectory,
        List<int> childExitCodes,
        List<string> artifacts,
        CancellationToken cancellationToken)
    {
        var localPackage = packageIndex.Packages[scenario.PackageId];
        var localAssembly = Path.Combine(cacheDirectory, localPackage.PackageId.ToLowerInvariant(), localPackage.Version.ToLowerInvariant(),
            "lib", scenario.Framework, "Rowles.LeanCorpus.Server.Local.dll");
        if (!File.Exists(localAssembly))
        {
            throw new InvalidDataException($"Server.Local package assembly is missing from the isolated cache: {localAssembly}");
        }
        var outputDirectory = Path.GetDirectoryName(consumerDll)!;
        var deps = Path.Combine(outputDirectory, "Consumer.deps.json");
        var runtimeConfig = Path.Combine(outputDirectory, "Consumer.runtimeconfig.json");
        var dataRoot = Path.Combine(workingDirectory, "server-local-data");
        var result = await RunChildAsync("runtime", ["exec", "--depsfile", deps, "--runtimeconfig", runtimeConfig, "--additionalprobingpath", cacheDirectory,
            localAssembly, "--urls", "http://127.0.0.1:0", "--LeanCorpus:DataRoot", dataRoot], workingDirectory, workingDirectory,
            runDirectory, childExitCodes, artifacts, TimeSpan.FromSeconds(45), cancellationToken,
            "Now listening on: http://127.0.0.1:").ConfigureAwait(false);
        if (result.Outcome != OutcomeKind.Success || !result.StoppedByCondition)
        {
            throw new InvalidDataException("Server.Local did not bind its ephemeral loopback endpoint before the smoke timeout.");
        }
    }

    private async Task<IReadOnlyList<string>> GetExternalSourcesAsync(string repositoryRoot, string runDirectory, List<int> childExitCodes, CancellationToken cancellationToken)
    {
        var stdout = Path.Combine(runDirectory, "nuget-sources.stdout.log");
        var stderr = Path.Combine(runDirectory, "nuget-sources.stderr.log");
        try
        {
            var result = await processRunner.RunAsync(new ProcessRequest
            {
                ExecutablePath = dotnetExecutable,
                Arguments = ["nuget", "list", "source", "--format", "short"],
                WorkingDirectory = repositoryRoot,
                StandardOutputLogPath = stdout,
                StandardErrorLogPath = stderr,
                Timeout = TimeSpan.FromMinutes(1)
            }, cancellationToken).ConfigureAwait(false);
            if (result.RawExitCode is { } exitCode)
            {
                childExitCodes.Add(exitCode);
            }
            if (result.Outcome != OutcomeKind.Success)
            {
                throw new InvalidDataException("Cannot discover enabled NuGet feeds for isolated consumers.");
            }
            var sources = result.StandardOutputTail
                .Where(static line => line.Length > 2 && line[0] == 'E' && char.IsWhiteSpace(line[1]))
                .Select(static line => line[2..].Trim())
                .Where(static value => value.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (sources.Length == 0)
            {
                throw new InvalidDataException("Cannot discover enabled NuGet feeds for isolated consumers.");
            }
            return sources;
        }
        finally
        {
            TryDeleteFile(stdout);
            TryDeleteFile(stderr);
        }
    }

    private async Task<ProcessResult> RunChildAsync(
        string name,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string scenarioDirectory,
        string runDirectory,
        List<int> childExitCodes,
        List<string> artifacts,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        string? stopOnOutputPrefix = null)
    {
        var stdout = Path.Combine(scenarioDirectory, name + ".stdout.log");
        var stderr = Path.Combine(scenarioDirectory, name + ".stderr.log");
        var process = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            StandardOutputLogPath = stdout,
            StandardErrorLogPath = stderr,
            Timeout = timeout,
            ProgressInterval = TimeSpan.FromSeconds(1),
            StopOnStandardOutputPrefix = stopOnOutputPrefix,
            Progress = item => progress?.Invoke($"{name}: {item.StandardOutputLineCount} stdout and {item.StandardErrorLineCount} stderr lines after {item.Elapsed.TotalSeconds:0}s.")
        }, cancellationToken).ConfigureAwait(false);
        if (process.RawExitCode is { } exitCode)
        {
            childExitCodes.Add(exitCode);
        }
        AddArtifact(artifacts, stdout, runDirectory);
        AddArtifact(artifacts, stderr, runDirectory);
        return process;
    }

    private static void RequireSuccess(ProcessResult result, string operation)
    {
        if (result.Outcome != OutcomeKind.Success)
        {
            var outcome = result.Outcome is OutcomeKind.Timeout or OutcomeKind.Cancelled or OutcomeKind.InfrastructureFailure
                ? result.Outcome
                : OutcomeKind.CommandFailure;
            throw new ConsumerFailureException(outcome, $"{operation} failed with outcome {result.Outcome} and exit code {result.RawExitCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(none)"}; see the retained stdout and stderr logs.");
        }
    }

    private static void AddGitArtifacts(List<string> artifacts)
    {
        foreach (var name in new[] { "git-commit.stdout.log", "git-commit.stderr.log", "git-branch.stdout.log", "git-branch.stderr.log", "git-status.stdout.log", "git-status.stderr.log" })
        {
            artifacts.Add(name);
        }
    }

    private static void AddArtifact(List<string> artifacts, string path, string runDirectory)
    {
        var relative = Path.GetRelativePath(runDirectory, path);
        if (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            artifacts.Add(relative.Replace('\\', '/'));
        }
    }

    private static void DeleteOwnedDirectory(string path, string ownerRoot)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(Path.GetFullPath(ownerRoot), fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Consumer cleanup path resolved outside its run directory: {fullPath}");
        }
        if (!Directory.Exists(fullPath))
        {
            return;
        }
        DeleteOwnedTree(fullPath);
        if (Directory.Exists(fullPath))
        {
            throw new IOException($"Consumer cleanup did not remove {fullPath}");
        }
    }

    private static void DeleteOwnedTree(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"Consumer cleanup path contains a link: {path}");
        }
        if ((attributes & FileAttributes.Directory) == 0)
        {
            File.Delete(path);
            return;
        }
        foreach (var child in Directory.EnumerateFileSystemEntries(path).ToArray())
        {
            DeleteOwnedTree(child);
        }
        Directory.Delete(path, recursive: false);
    }

    private string? ValidateRequest(PackageConsumerValidationRequest request)
    {
        if (request.Checkout is null || !Directory.Exists(request.Checkout.RepositoryRoot))
        {
            return "A valid LeanCorpus checkout is required.";
        }
        if (!PathsEqual(artifactPaths.RepositoryRoot, request.Checkout.RepositoryRoot))
        {
            return "The package consumer service artefact root does not match the selected checkout.";
        }
        if (request.Scope is not ("all" or "compression"))
        {
            return "Package consumer scope must be 'all' or 'compression'.";
        }
        if (!Directory.Exists(request.PackageDirectory))
        {
            return $"Package directory does not exist: {request.PackageDirectory}";
        }
        return null;
    }

    private static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), comparison);
    }

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record AcceptanceDocument(
        int SchemaVersion,
        string? SourceCommit,
        string PackageManifest,
        DateTimeOffset GeneratedAtUtc,
        string Status,
        string? Failure,
        IReadOnlyList<ScenarioAcceptance> Results);

    private sealed record ScenarioAcceptance(
        string Scenario,
        string PackageId,
        string PackageVersion,
        string PackageSha256,
        string TargetFramework,
        string Restore,
        string Build,
        string Runtime,
        IReadOnlyList<VerifiedConsumerPackage> VerifiedPackages,
        string WorkingDirectory,
        string CacheDirectory,
        string Status,
        string? Error);

    private sealed class ConsumerFailureException(OutcomeKind outcome, string message, Exception? innerException = null)
        : Exception(message, innerException)
    {
        public OutcomeKind Outcome { get; } = outcome;
    }
}
