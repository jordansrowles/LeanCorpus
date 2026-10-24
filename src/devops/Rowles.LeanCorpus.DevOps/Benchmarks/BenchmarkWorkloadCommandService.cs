using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal sealed record WorkloadChildEvidence(
    string Engine, string Outcome, int? ExitCode, string InputSha256, string? OutputSha256,
    string? CheckpointSha256, string? DatasetSha256, string[] VerifiedLogicalIds,
    string[] CompletedSearchLogicalIds, string? Failure);

internal sealed class BenchmarkWorkloadCommandService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    Action<string>? progress,
    Action<string>? errorOutput,
    string dotnetExecutable)
{
    private static readonly TimeSpan LeaseWait = TimeSpan.FromMinutes(5);

    public async Task<BenchmarkCommandResult> RunAsync(BenchmarkCommandRequest request, CancellationToken cancellationToken)
    {
        var options = request.Options.Workload!;
        if (options.List)
        {
            progress?.Invoke("Workload scenarios: ingest-throughput, search-readonly, ingest-search-refresh.");
            progress?.Invoke("Workload engines: leancorpus, lucenenet, both. Dataset: leancorpus-search v1. Durability: explicit-commit.");
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.Success, "Workload options listed."), null, null);
        }

        var error = Validate(options, out var searchOperations, out var engines);
        if (error is not null)
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure, error), null, null);

        var project = RepositoryProjectCatalog.GetExistingProjectPath(artifactPaths.RepositoryRoot, RepositoryProjectRole.BenchmarkWorkloads);
        var buildArguments = BuildArguments(project, options);
        if (options.Dry)
        {
            progress?.Invoke($"Workload plan: scenario={options.Scenario}, engines={string.Join(',', engines)}, dataset={options.Dataset}/v1, " +
                $"seed={options.Seed}, documents={options.DocumentCount}, searches={searchOperations}, framework={options.Framework}.");
            progress?.Invoke($"Budget: duration={options.DurationSeconds}s, memory={options.MemoryBudgetMiB} MiB, " +
                $"concurrency={options.Concurrency}, queue={options.QueueCapacity}, refreshEvery={options.RefreshEveryDocuments}, durability={options.Durability}.");
            progress?.Invoke("dotnet " + string.Join(' ', buildArguments));
            progress?.Invoke("dotnet run --no-build --no-restore --configuration Release --framework " + options.Framework +
                " --project " + project + " -- <input.json> <terminal.json>");
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.Success, "Workload dry plan completed."), null, null);
        }

        var host = BenchmarkHostIdentity.Capture();
        var inputs = host.ToRunInputs().ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        inputs["workloadSchemaVersion"] = "1";
        inputs["scenario"] = options.Scenario;
        inputs["engines"] = string.Join(',', engines);
        inputs["dataset"] = options.Dataset;
        inputs["seed"] = options.Seed.ToString(CultureInfo.InvariantCulture);
        inputs["documentCount"] = options.DocumentCount.ToString(CultureInfo.InvariantCulture);
        inputs["searchOperations"] = searchOperations.ToString(CultureInfo.InvariantCulture);
        inputs["framework"] = options.Framework;
        inputs["durationSeconds"] = options.DurationSeconds.ToString(CultureInfo.InvariantCulture);
        inputs["memoryBudgetMiB"] = options.MemoryBudgetMiB.ToString(CultureInfo.InvariantCulture);
        inputs["concurrency"] = options.Concurrency.ToString(CultureInfo.InvariantCulture);
        inputs["queueCapacity"] = options.QueueCapacity.ToString(CultureInfo.InvariantCulture);
        inputs["refreshEveryDocuments"] = options.RefreshEveryDocuments.ToString(CultureInfo.InvariantCulture);
        inputs["durability"] = options.Durability;
        inputs["qualificationState"] = "Unqualified";
        inputs["eligibleForRatio"] = "false";
        inputs["comparisonBoundary"] = "logical-operation-parity-only";
        var manifest = await runStore.StartAsync("benchmark", inputs, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = artifactPaths.GetRunDirectory("benchmark", manifest.RunId);
        var children = new List<WorkloadChildEvidence>();
        var artifacts = new List<string>();
        var exits = new List<int>();
        var outcome = OutcomeKind.Success;
        string? failure = null;

        try
        {
            var provenance = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (provenance.Outcome != OutcomeKind.Success || provenance.Commit is not { Length: 40 } commit ||
                !commit.All(Uri.IsHexDigit))
            {
                outcome = OutcomeKind.InfrastructureFailure;
                failure = provenance.Failure ?? "The workload source commit could not be identified exactly.";
            }
            else
            {
                manifest = await runStore.UpdateRunningAsync(manifest, commit, provenance.Branch, provenance.Dirty,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
                var sdk = await sdkResolver.ResolveAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
                if (sdk.Outcome != OutcomeKind.Success)
                {
                    outcome = sdk.Outcome;
                    failure = sdk.Failure;
                }
                else
                {
                    manifest = await runStore.UpdateRunningAsync(manifest, commit, provenance.Branch, provenance.Dirty,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["dotnet-sdk-requested"] = sdk.RequestedVersion ?? string.Empty,
                            ["dotnet-sdk-selected"] = sdk.SelectedVersion ?? string.Empty,
                            ["dotnet-sdk-roll-forward"] = sdk.RollForward ?? "default"
                        }, cancellationToken).ConfigureAwait(false);
                    progress?.Invoke("Waiting for the exclusive benchmark host and repository leases.");
                    await using var hostLease = await ExecutionLease.AcquireExclusiveAsync(
                        BenchmarkCommandService.BenchmarkHostLeaseRoot, LeaseWait, cancellationToken).ConfigureAwait(false);
                    await using var repositoryLease = await ExecutionLease.AcquireExclusiveAsync(
                        artifactPaths.RepositoryRoot, LeaseWait, cancellationToken).ConfigureAwait(false);
                    progress?.Invoke("Building sustained workload worker.");
                    var build = await RunProcessAsync(buildArguments, "build", runDirectory,
                        TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
                    RecordProcess(build, "build", runDirectory, artifacts, exits);
                    if (build.Outcome != OutcomeKind.Success)
                    {
                        outcome = build.Outcome;
                        failure = build.Failure ?? "The sustained workload worker build failed.";
                    }
                    else
                    {
                        foreach (var engine in engines)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var directory = Path.Combine(runDirectory, "workload", engine);
                            Directory.CreateDirectory(directory);
                            var inputPath = Path.Combine(directory, "input.json");
                            var outputPath = Path.Combine(directory, "terminal.json");
                            var indexPath = Path.Combine(directory, "index");
                            var payload = CreateInput(options, engine, searchOperations, commit, provenance.Dirty, indexPath);
                            await File.WriteAllBytesAsync(inputPath, payload, cancellationToken).ConfigureAwait(false);
                            artifacts.Add(Relative(runDirectory, inputPath));
                            var inputHash = Hash(payload);
                            progress?.Invoke($"Running {options.Scenario} with {engine} ({options.DurationSeconds}s measured budget).");
                            var child = await RunProcessAsync([
                                "run", "--no-build", "--no-restore", "--configuration", "Release",
                                "--framework", options.Framework, "--project", project, "--", inputPath, outputPath
                            ], "workload-" + engine, runDirectory,
                                TimeSpan.FromSeconds(options.DurationSeconds + 60), cancellationToken).ConfigureAwait(false);
                            RecordProcess(child, "workload-" + engine, runDirectory, artifacts, exits);
                            var partialPath = outputPath + ".partial";
                            var partialHash = File.Exists(partialPath) ? Hash(await File.ReadAllBytesAsync(partialPath, CancellationToken.None).ConfigureAwait(false)) : null;
                            if (partialHash is not null) artifacts.Add(Relative(runDirectory, partialPath));
                            WorkloadChildEvidence evidence;
                            try
                            {
                                evidence = ReadTerminal(outputPath, payload, engine, options, searchOperations, child.RawExitCode, inputHash, partialHash);
                                artifacts.Add(Relative(runDirectory, outputPath));
                            }
                            catch (Exception exception) when (exception is IOException or JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
                            {
                                evidence = new WorkloadChildEvidence(engine, "Incomplete", child.RawExitCode,
                                    inputHash, File.Exists(outputPath) ? Hash(await File.ReadAllBytesAsync(outputPath, CancellationToken.None).ConfigureAwait(false)) : null,
                                    partialHash, null, [], [], exception.Message);
                                if (File.Exists(outputPath)) artifacts.Add(Relative(runDirectory, outputPath));
                            }
                            children.Add(evidence);
                            if (child.Outcome != OutcomeKind.Success || evidence.Outcome != "Completed")
                            {
                                outcome = child.Outcome == OutcomeKind.Success ? OutcomeKind.InfrastructureFailure : child.Outcome;
                                if (evidence.Outcome == "TimedOut") outcome = OutcomeKind.Timeout;
                                if (evidence.Outcome == "Cancelled") outcome = OutcomeKind.Cancelled;
                                failure = evidence.Failure ?? child.Failure ?? $"The {engine} workload did not complete valid terminal evidence.";
                                break;
                            }
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            outcome = OutcomeKind.Cancelled;
            failure = exception.Message;
        }
        catch (TimeoutException exception)
        {
            outcome = OutcomeKind.Timeout;
            failure = exception.Message;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            outcome = OutcomeKind.InfrastructureFailure;
            failure = exception.Message;
        }

        var semanticParity = HasSemanticParity(children);
        if (outcome == OutcomeKind.Success && children.Count == 2 && !semanticParity)
        {
            outcome = OutcomeKind.ValidationFailure;
            failure = "The two engines did not complete matching logical IDs and search requests.";
        }
        var semanticClass = children.Count == 2 ? semanticParity ? "MatchedOperations" : "Directional" : "SingleEngine";
        var state = outcome switch
        {
            OutcomeKind.Success => RunState.Succeeded,
            OutcomeKind.Timeout => RunState.TimedOut,
            OutcomeKind.Cancelled => RunState.Cancelled,
            _ => RunState.Failed
        };
        var reportPath = Path.Combine(runDirectory, "workload-report.json");
        await File.WriteAllBytesAsync(reportPath, JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, scenario = options.Scenario, framework = options.Framework,
            sourceCommit = manifest.SourceCommit, sourceDirty = manifest.SourceDirty,
            hostFingerprintSha256 = host.FingerprintSha256, semanticClass, semanticParity,
            qualificationState = "Unqualified", eligibleForRatio = false,
            completionBoundary = "explicit-commit-during-measured-interval", children,
            outcome = state.ToString(), failure
        }, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), CancellationToken.None).ConfigureAwait(false);
        artifacts.Add("workload-report.json");
        var finished = await runStore.FinishAsync(manifest, state, artifacts: artifacts, failure: failure,
            cancellationToken: CancellationToken.None, childExitCodes: exits,
            additionalInputs: new Dictionary<string, string>
            {
                ["semanticClass"] = semanticClass,
                ["semanticParity"] = semanticParity.ToString().ToLowerInvariant(),
                ["qualificationState"] = "Unqualified",
                ["eligibleForRatio"] = "false"
            }).ConfigureAwait(false);
        return new BenchmarkCommandResult(new CommandResult(outcome, outcome == OutcomeKind.Success
            ? $"Sustained workload {options.Scenario} completed with {children.Count} engine(s); evidence is unqualified."
            : $"Sustained workload {state.ToString().ToLowerInvariant()}: {failure}", Details: runDirectory),
            finished.RunId, runDirectory);
    }

    private static string? Validate(BenchmarkWorkloadOptions options, out int searchOperations, out string[] engines)
    {
        engines = options.Engine == "both" ? ["leancorpus", "lucenenet"] : [options.Engine];
        searchOperations = options.SearchOperations == -1 && options.Scenario != "ingest-throughput"
            ? options.DocumentCount : options.SearchOperations == -1 ? 0 : options.SearchOperations;
        if (options.Scenario is not ("ingest-throughput" or "search-readonly" or "ingest-search-refresh"))
            return "Unknown sustained workload scenario. Use benchmark workload --list.";
        if (engines.Any(static engine => engine is not ("leancorpus" or "lucenenet")))
            return "Unknown sustained workload engine. Use leancorpus, lucenenet or both.";
        if (options.Framework is not ("net10.0" or "net11.0") || options.Dataset != "leancorpus-search" || options.Durability != "explicit-commit")
            return "The worker supports net10.0/net11.0, leancorpus-search v1 and explicit-commit durability.";
        if (options.DocumentCount is < 2 or > 100_000 || searchOperations is < 0 or > 1_000_000 ||
            options.Concurrency is < 1 or > 32 || options.QueueCapacity is < 1 or > 4096 ||
            options.RefreshEveryDocuments < 1 || options.DurationSeconds is < 1 or > 3600 ||
            options.MemoryBudgetMiB is < 128 or > 65_536)
            return "Workload counts, concurrency, queue, refresh and resource budgets must be bounded.";
        if ((options.Scenario == "ingest-throughput") != (searchOperations == 0))
            return "Ingest has no search operations; search scenarios require at least one.";
        return null;
    }

    private static IReadOnlyList<string> BuildArguments(string project, BenchmarkWorkloadOptions options)
        => ["build", project, "-c", "Release", "-f", options.Framework,
            .. (options.NoRestore ? ["--no-restore"] : Array.Empty<string>()),
            "--disable-build-servers", "-m:1", "-p:UseSharedCompilation=false", "--tl:off"];

    private async Task<ProcessResult> RunProcessAsync(IReadOnlyList<string> arguments, string name,
        string runDirectory, TimeSpan timeout, CancellationToken token)
        => await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = arguments,
            WorkingDirectory = artifactPaths.RepositoryRoot,
            StandardOutputLogPath = Path.Combine(runDirectory, name + ".stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, name + ".stderr.log"),
            Timeout = timeout,
            ProgressInterval = TimeSpan.FromSeconds(5),
            Progress = value => progress?.Invoke($"{name}: {value.Elapsed:hh\\:mm\\:ss}, " +
                $"{value.StandardOutputLineCount} stdout and {value.StandardErrorLineCount} stderr lines.")
        }, token).ConfigureAwait(false);

    private void RecordProcess(ProcessResult result, string name, string runDirectory,
        List<string> artifacts, List<int> exits)
    {
        artifacts.Add(Relative(runDirectory, result.StandardOutputLogPath));
        artifacts.Add(Relative(runDirectory, result.StandardErrorLogPath));
        if (result.RawExitCode is int exitCode) exits.Add(exitCode);
        foreach (var line in result.StandardOutputTail) progress?.Invoke(line);
        foreach (var line in result.StandardErrorTail) errorOutput?.Invoke(line);
    }

    private static byte[] CreateInput(BenchmarkWorkloadOptions options, string engine, int searchOperations,
        string commit, bool dirty, string indexPath)
        => JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1, scenario = options.Scenario, engine, dataset = options.Dataset, datasetVersion = 1,
            seed = options.Seed, documentCount = options.DocumentCount, searchOperations,
            concurrency = options.Concurrency, queueCapacity = options.QueueCapacity,
            refreshEveryDocuments = options.RefreshEveryDocuments,
            durationMilliseconds = options.DurationSeconds * 1000,
            memoryBudgetMiB = options.MemoryBudgetMiB, durability = options.Durability,
            sourceCommit = commit, sourceDirty = dirty, indexPath
        }, new JsonSerializerOptions { WriteIndented = true });

    internal static WorkloadChildEvidence ReadTerminal(string path, byte[] input, string engine,
        BenchmarkWorkloadOptions options, int searchOperations, int? exitCode, string inputHash, string? partialHash)
    {
        if (!File.Exists(path)) throw new InvalidDataException("The worker produced no terminal JSON.");
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length is < 2 or > 67_108_864) throw new InvalidDataException("The worker terminal JSON is empty or exceeds its size limit.");
        using var expected = JsonDocument.Parse(input);
        using var result = JsonDocument.Parse(bytes);
        var root = result.RootElement;
        if (root.GetProperty("schemaVersion").GetInt32() != 1 ||
            !JsonElement.DeepEquals(root.GetProperty("request"), expected.RootElement) ||
            root.GetProperty("classification").GetString() != "Unqualified" ||
            root.GetProperty("completionBoundary").GetString() != "explicit-commit-during-measured-interval")
            throw new InvalidDataException("The worker terminal schema, request or completion boundary differs from its input.");
        var outcome = root.GetProperty("outcome").GetString() ?? throw new InvalidDataException("The worker outcome is absent.");
        var failure = root.TryGetProperty("failure", out var failureField) && failureField.ValueKind == JsonValueKind.String
            ? failureField.GetString() : null;
        var hash = root.GetProperty("datasetSha256").GetString();
        var verified = root.GetProperty("verifiedLogicalIds").EnumerateArray().Select(static value => value.GetString() ?? string.Empty).ToArray();
        var committedIds = root.GetProperty("committedLogicalIds").EnumerateArray().Select(static value => value.GetString() ?? string.Empty).ToArray();
        var searched = root.GetProperty("completedSearchLogicalIds").EnumerateArray().Select(static value => value.GetString() ?? string.Empty).ToArray();
        if (outcome == "Completed")
        {
            var expectedPreload = options.Scenario == "search-readonly" ? options.DocumentCount
                : options.Scenario == "ingest-search-refresh" ? Math.Max(1, Math.Min(16, options.DocumentCount / 4)) : 0;
            if (exitCode != 0 || hash is not { Length: 64 } || !hash.All(Uri.IsHexDigit) ||
                root.GetProperty("failedOperations").GetInt32() != 0 || root.GetProperty("cancelledOperations").GetInt32() != 0 ||
                root.GetProperty("preloadedDocuments").GetInt32() != expectedPreload ||
                root.GetProperty("admittedDocuments").GetInt32() != options.DocumentCount - expectedPreload ||
                root.GetProperty("committedDocuments").GetInt32() != options.DocumentCount - expectedPreload ||
                root.GetProperty("visibleDocuments").GetInt32() != options.DocumentCount ||
                root.GetProperty("completedSearches").GetInt32() != searchOperations ||
                verified.Length != options.DocumentCount || committedIds.Length != options.DocumentCount ||
                verified.Any(string.IsNullOrWhiteSpace) || verified.Distinct(StringComparer.Ordinal).Count() != options.DocumentCount ||
                !verified.Order(StringComparer.Ordinal).SequenceEqual(committedIds.Order(StringComparer.Ordinal), StringComparer.Ordinal) ||
                searched.Length != searchOperations || searched.Any(id => !verified.Contains(id, StringComparer.Ordinal)))
                throw new InvalidDataException("The worker marked incomplete or incorrect logical operations as completed.");
        }
        return new WorkloadChildEvidence(engine, outcome, exitCode, inputHash, Hash(bytes), partialHash, hash, verified, searched, failure);
    }

    internal static bool HasSemanticParity(IReadOnlyList<WorkloadChildEvidence> children)
        => children.Count == 2 &&
            children[0].Outcome == "Completed" && children[1].Outcome == "Completed" &&
            children[0].Engine != children[1].Engine &&
            children[0].DatasetSha256 is { Length: 64 } && children[0].DatasetSha256 == children[1].DatasetSha256 &&
            children[0].VerifiedLogicalIds.Length > 0 &&
            children[0].VerifiedLogicalIds.SequenceEqual(children[1].VerifiedLogicalIds, StringComparer.Ordinal) &&
            children[0].CompletedSearchLogicalIds.SequenceEqual(children[1].CompletedSearchLogicalIds, StringComparer.Ordinal);

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
}
