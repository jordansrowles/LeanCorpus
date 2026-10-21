using System.Collections.Immutable;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Data;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal sealed record BenchmarkInvocation(string ProjectKey, string ProjectPath, IReadOnlyList<string> Arguments);

internal sealed record BenchmarkExecutionPlan(
    string Suite,
    string Strategy,
    string Framework,
    string Dataset,
    int DocumentCount,
    bool CorpusOnly,
    string? ReferencePath,
    IReadOnlyList<BenchmarkInvocation> Invocations);

internal sealed record BenchmarkProjectResult(string Project, string Status, int? ExitCode, string Path, string? Failure);

internal sealed record BenchmarkCommandResult(CommandResult CommandResult, Guid? RunId, string? RunDirectory);

internal static class BenchmarkCommandPlanner
{
    private static readonly string[] ProjectKeys = ["core", "text", "compression"];
    private static readonly HashSet<string> StandaloneSuites = new(StringComparer.OrdinalIgnoreCase)
    {
        "codec-frame", "codec-frame-read", "codec-migration", "compound-file", "windows-filesystem", "windows-storage"
    };

    public static BenchmarkExecutionPlan Create(BenchmarkCommandOptions options, DevOpsConfiguration configuration, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var suite = options.Suite.Trim().ToLowerInvariant();
        var strategyName = options.Strategy.Trim().ToLowerInvariant();
        var dataset = options.Dataset.Trim().ToLowerInvariant();
        if (suite is "remote" or "affected")
            throw new InvalidOperationException($"The benchmark '{suite}' workflow is not yet managed; use `pwsh ./devops.ps1 benchmark {suite}` during migration.");
        if (suite is not ("all" or "core" or "text") && !configuration.BenchmarkSuites.ContainsKey(suite))
            throw new InvalidOperationException($"Unknown benchmark suite '{suite}'. Use --list to see available suites.");
        if (!configuration.BenchmarkStrategies.TryGetValue(strategyName, out var strategy))
            throw new InvalidOperationException($"Unknown benchmark strategy '{strategyName}'. Use --list to see available strategies.");
        if (options.Framework is not ("net10.0" or "net11.0"))
            throw new InvalidOperationException("Benchmark framework must be net10.0 or net11.0.");
        if (options.DocumentCount < 0)
            throw new InvalidOperationException("Document count cannot be negative.");
        if (options.BookCount < 1)
            throw new InvalidOperationException("Gutenberg book count must be a positive integer.");
        if (dataset is not ("synthetic" or "wikipedia"))
            throw new InvalidOperationException("Benchmark dataset must be synthetic or wikipedia.");
        if (options.SourceManifest is not null && string.IsNullOrWhiteSpace(options.SourceManifest))
            throw new InvalidOperationException("Source manifest must be a non-empty path when specified.");

        var areaNames = SplitNames(options.Area);
        var groupNames = SplitNames(options.Group);
        if ((areaNames.Length != 0 || groupNames.Length != 0) && suite is not ("core" or "text"))
            throw new InvalidOperationException("Area and group selection require the core or text benchmark suite.");
        if ((areaNames.Length != 0 || groupNames.Length != 0) && options.PrepareData)
            throw new InvalidOperationException("PrepareData cannot be combined with area or group selection.");
        if (dataset == "wikipedia")
        {
            if (!configuration.BenchmarkDatasets.TryGetValue("wikipedia", out var wikipediaSuites) || !wikipediaSuites.Contains(suite, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Suite '{suite}' is not supported with the Wikipedia reference dataset.");
            if (areaNames.Length != 0 || groupNames.Length != 0)
                throw new InvalidOperationException("Area and group selection are not supported with the Wikipedia reference dataset.");
            if (options.PrepareData)
                throw new InvalidOperationException("PrepareData is not supported with the Wikipedia reference dataset.");
            if (suite is "merge" or "flush" && ContainsFilter(options.Arguments))
                throw new InvalidOperationException($"Wikipedia '{suite}' runs select only the plain-text benchmark method; custom filters are not supported.");
        }
        else if (options.ReferencePath is not null)
        {
            throw new InvalidOperationException("ReferencePath requires the Wikipedia dataset.");
        }

        if (options.PrepareData && !NeedsGutenbergData(suite, options.Arguments))
            throw new InvalidOperationException($"PrepareData is not required or supported for benchmark suite '{suite}'.");

        var selectedProjects = suite switch
        {
            "all" => ProjectKeys,
            "text" => ["text"],
            "compression" => ["compression"],
            _ => ["core"]
        };
        if ((areaNames.Length != 0 || groupNames.Length != 0) && ContainsFilter(options.Arguments))
            throw new InvalidOperationException("A custom BenchmarkDotNet filter cannot be combined with area or group selection.");

        var documentCount = options.DocumentCount > 0 ? options.DocumentCount : strategy.DocCount;
        if (options.Controlled)
        {
            if (documentCount <= 0)
                documentCount = 1000;
        }
        if (dataset == "wikipedia" && documentCount > 20_000)
            throw new InvalidOperationException($"The Wikipedia reference contains 20,000 records; requested document count {documentCount} exceeds that limit.");
        var corpusOnly = options.CorpusOnly || (options.Controlled && !StandaloneSuites.Contains(suite));
        var referencePath = dataset == "wikipedia"
            ? ResolvePath(repositoryRoot, options.ReferencePath ?? "artifacts/dataforge/reference/leancorpus-wikipedia-en-v1")
            : null;
        var projectPathMap = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["core"] = "src/devops/Rowles.LeanCorpus.Benchmarks/Rowles.LeanCorpus.Benchmarks.csproj",
            ["text"] = "src/devops/Rowles.Text.Benchmarks/Rowles.Text.Benchmarks.csproj",
            ["compression"] = "src/devops/Rowles.LeanCorpus.Benchmarks.Compression/Rowles.LeanCorpus.Benchmarks.Compression.csproj"
        };
        var invocations = new List<BenchmarkInvocation>();

        foreach (var project in selectedProjects)
        {
            var benchmarks = areaNames.Length == 0 && groupNames.Length == 0
                ? []
                : SelectBenchmarks(configuration, project, areaNames, groupNames);
            if (areaNames.Length != 0 || groupNames.Length != 0)
            {
                if (benchmarks.Length == 0)
                    throw new InvalidOperationException($"No benchmark groups matched suite '{suite}', Area='{options.Area}', Group='{options.Group}'.");
                foreach (var benchmark in benchmarks)
                {
                    invocations.Add(new BenchmarkInvocation(project, Path.Combine(repositoryRoot, projectPathMap[project]),
                        BuildArguments(project, suite, strategy.Job, options.Arguments, documentCount, corpusOnly, options.GcDump, dataset, benchmark)));
                }
            }
            else
            {
                invocations.Add(new BenchmarkInvocation(project, Path.Combine(repositoryRoot, projectPathMap[project]),
                    BuildArguments(project, suite, strategy.Job, options.Arguments, documentCount, corpusOnly, options.GcDump, dataset, null)));
            }
        }

        return new BenchmarkExecutionPlan(suite, strategyName, options.Framework, dataset, documentCount, corpusOnly, referencePath,
            invocations.ToImmutableArray());
    }

    private static string[] BuildArguments(
        string project,
        string suite,
        IReadOnlyList<string> jobArguments,
        IReadOnlyList<string> passThrough,
        int documentCount,
        bool corpusOnly,
        bool gcDump,
        string dataset,
        string? benchmarkClass)
    {
        var arguments = new List<string>();
        if (project == "core")
        {
            arguments.AddRange(["--suite", benchmarkClass is not null || suite is "all" or "core" ? "all" : suite]);
            if (documentCount > 0)
                arguments.AddRange(["--doccount", documentCount.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            if (corpusOnly)
                arguments.Add("--corpus-only");
            if (gcDump)
                arguments.Add("--gcdump");
            if (dataset == "wikipedia" && suite == "merge")
                arguments.AddRange(["--filter", "*LeanCorpus_Merge_PlainText*"]);
            if (dataset == "wikipedia" && suite == "flush")
                arguments.AddRange(["--filter", "*LeanCorpus_Flush_TextOnly*"]);
            if (benchmarkClass is not null)
                arguments.Add("--");
        }
        arguments.AddRange(jobArguments);
        arguments.AddRange(passThrough);
        if (benchmarkClass is not null)
            arguments.AddRange(["--filter", $"*{benchmarkClass}*"]);
        return arguments.ToArray();
    }

    private static string[] SelectBenchmarks(
        DevOpsConfiguration configuration,
        string project,
        IReadOnlyList<string> areas,
        IReadOnlyList<string> groups)
    {
        var relevant = configuration.BenchmarkGroups
            .Where(pair => pair.Value.Project.Equals(project, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var knownAreas = relevant.Select(pair => pair.Value.Area).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownGroups = relevant.Select(pair => pair.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownArea = areas.FirstOrDefault(area => !knownAreas.Contains(area));
        if (unknownArea is not null)
            throw new InvalidOperationException($"Unknown benchmark area '{unknownArea}' for suite '{project}'.");
        var unknownGroup = groups.FirstOrDefault(group => !knownGroups.Contains(group));
        if (unknownGroup is not null)
            throw new InvalidOperationException($"Unknown benchmark group '{unknownGroup}' for suite '{project}'.");

        return relevant
            .Where(pair => (areas.Count == 0 || areas.Contains(pair.Value.Area, StringComparer.OrdinalIgnoreCase)) &&
                (groups.Count == 0 || groups.Contains(pair.Key, StringComparer.OrdinalIgnoreCase)))
            .SelectMany(pair => pair.Value.Benchmarks)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] SplitNames(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool ContainsFilter(IReadOnlyList<string> arguments)
        => arguments.Any(argument => argument.Equals("--filter", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-f", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("--filter=", StringComparison.OrdinalIgnoreCase) ||
            argument.StartsWith("-f=", StringComparison.OrdinalIgnoreCase));

    private static bool NeedsGutenbergData(string suite, IReadOnlyList<string> arguments)
        => suite is "gutenberg-index" or "gutenberg-search" ||
            (suite == "text" && arguments.Any(argument => argument.Contains("GutenbergAnalysisBenchmarks", StringComparison.OrdinalIgnoreCase)));

    private static string ResolvePath(string repositoryRoot, string path)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repositoryRoot, path));
}

internal sealed class BenchmarkCommandService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    Action<string>? progress = null,
    Action<string>? errorOutput = null,
    string dotnetExecutable = "dotnet")
{
    private static readonly string BenchmarkHostLeaseRoot = Path.Combine(Path.GetTempPath(), "LeanCorpus.DevOps.BenchmarkHost");
    private static readonly TimeSpan BenchmarkRunLimit = TimeSpan.FromMinutes(360);
    private static readonly TimeSpan BenchmarkLeaseWait = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<BenchmarkCommandResult> RunAsync(BenchmarkCommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var configuration = new ConfigurationLoader(artifactPaths.RepositoryRoot).Load();
        if (request.Options.List)
        {
            var suites = new[] { "all", "core", "text", "compression" }
                .Concat(configuration.BenchmarkSuites.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase);
            progress?.Invoke("Available benchmark suites: " + string.Join(", ", suites));
            progress?.Invoke("Available strategies: " + string.Join(", ", configuration.BenchmarkStrategies.Keys.OrderBy(static value => value, StringComparer.Ordinal)));
            progress?.Invoke("Datasets: synthetic, wikipedia");
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.Success, "Benchmark options listed."), null, null);
        }

        BenchmarkExecutionPlan plan;
        try
        {
            plan = BenchmarkCommandPlanner.Create(request.Options, configuration, artifactPaths.RepositoryRoot);
        }
        catch (InvalidOperationException exception)
        {
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure, exception.Message), null, null);
        }

        if (request.Options.PrepareData)
        {
            using var httpClient = new HttpClient();
            var download = await new GutenbergDownloadService(httpClient, progress).DownloadAsync(new GutenbergDownloadRequest
            {
                RepositoryRoot = artifactPaths.RepositoryRoot,
                BookCount = request.Options.BookCount
            }, cancellationToken).ConfigureAwait(false);
            if (download.CommandResult.Outcome != OutcomeKind.Success)
                return new BenchmarkCommandResult(download.CommandResult, null, null);
        }

        if (request.Options.Dry)
        {
            foreach (var invocation in plan.Invocations)
                progress?.Invoke("dotnet " + string.Join(" ", QuoteArguments(BuildDotNetArguments(invocation, plan))));
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.Success, "Benchmark dry run completed; no benchmark targets were executed."), null, null);
        }

        if (request.Options.SourceManifest is { } sourceManifest && !File.Exists(ResolvePath(sourceManifest, artifactPaths.RepositoryRoot)))
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure, $"Source manifest '{sourceManifest}' does not exist."), null, null);
        if (request.Options.GcDump && !await IsDotNetToolAvailableAsync(request.Checkout, cancellationToken).ConfigureAwait(false))
            return new BenchmarkCommandResult(new CommandResult(OutcomeKind.ValidationFailure, "The dotnet-gcdump tool is required for --gc-dump."), null, null);

        var runInputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["suite"] = plan.Suite,
            ["strategy"] = plan.Strategy,
            ["framework"] = plan.Framework,
            ["dataset"] = plan.Dataset,
            ["documentCount"] = plan.DocumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["projectKeys"] = string.Join(",", plan.Invocations.Select(static invocation => invocation.ProjectKey).Distinct(StringComparer.Ordinal)),
            ["arguments"] = JsonSerializer.Serialize(request.Options.Arguments),
            ["referencePath"] = plan.ReferencePath ?? string.Empty,
            ["sourceCommit"] = request.Options.SourceCommit ?? string.Empty,
            ["sourceRef"] = request.Options.SourceRef ?? string.Empty,
            ["sourceManifest"] = request.Options.SourceManifest is null ? string.Empty : ResolvePath(request.Options.SourceManifest, artifactPaths.RepositoryRoot),
            ["controlled"] = request.Options.Controlled.ToString().ToLowerInvariant(),
            ["corpusOnly"] = plan.CorpusOnly.ToString().ToLowerInvariant(),
            ["gcDump"] = request.Options.GcDump.ToString().ToLowerInvariant(),
            ["benchmarkLock"] = "host-exclusive"
        };
        var manifest = await runStore.StartAsync("benchmark", runInputs, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = artifactPaths.GetRunDirectory("benchmark", manifest.RunId);
        var childExitCodes = new List<int>();
        var artifacts = new List<string>();
        var results = new List<BenchmarkProjectResult>();
        var failure = (OutcomeKind?)null;
        string? failureDetails = null;
        using var runTimeout = new CancellationTokenSource(BenchmarkRunLimit);
        using var runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runTimeout.Token);
        var runToken = runCancellation.Token;

        try
        {
            var provenance = await provenanceReader.ReadAsync(request.Checkout, runDirectory, runToken).ConfigureAwait(false);
            if (provenance.Outcome != OutcomeKind.Success)
                return await FinishAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure, provenance.Failure, childExitCodes, artifacts, results).ConfigureAwait(false);
            manifest = await runStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
                cancellationToken: runToken).ConfigureAwait(false);

            var sdk = await sdkResolver.ResolveAsync(request.Checkout, runDirectory, runToken).ConfigureAwait(false);
            if (sdk.Outcome != OutcomeKind.Success)
                return await FinishAsync(manifest, runDirectory, sdk.Outcome, sdk.Failure, childExitCodes, artifacts, results).ConfigureAwait(false);
            manifest = await runStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dotnet-sdk-requested"] = sdk.RequestedVersion ?? string.Empty,
                    ["dotnet-sdk-selected"] = sdk.SelectedVersion ?? string.Empty,
                    ["dotnet-sdk-roll-forward"] = sdk.RollForward ?? "default"
                }, runToken).ConfigureAwait(false);

            var referenceIdentity = plan.Dataset == "wikipedia"
                ? await VerifyWikipediaReferenceAsync(request.Checkout, plan, runDirectory, artifacts, childExitCodes, runToken).ConfigureAwait(false)
                : null;
            if (plan.Dataset == "wikipedia" && referenceIdentity is null)
                return await FinishAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure,
                    "The frozen Wikipedia reference could not be verified.", childExitCodes, artifacts, results).ConfigureAwait(false);

            progress?.Invoke("Waiting for the exclusive benchmark host lease.");
            await using var hostLease = await ExecutionLease.AcquireExclusiveAsync(
                BenchmarkHostLeaseRoot, BenchmarkLeaseWait, runToken).ConfigureAwait(false);
            await using var repositoryLease = await ExecutionLease.AcquireExclusiveAsync(
                artifactPaths.RepositoryRoot, BenchmarkLeaseWait, runToken).ConfigureAwait(false);
            progress?.Invoke("Exclusive benchmark host and repository leases acquired.");

            foreach (var (invocation, index) in plan.Invocations.Select((value, index) => (value, index)))
            {
                runToken.ThrowIfCancellationRequested();
                var projectDirectory = Path.Combine(runDirectory, invocation.ProjectKey, index.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(projectDirectory);
                var stdout = Path.Combine(projectDirectory, "process.stdout.log");
                var stderr = Path.Combine(projectDirectory, "process.stderr.log");
                progress?.Invoke($"Running benchmark project {invocation.ProjectKey} ({index + 1}/{plan.Invocations.Count}).");
                var process = await processRunner.RunAsync(new ProcessRequest
                {
                    ExecutablePath = dotnetExecutable,
                    Arguments = BuildDotNetArguments(invocation, plan),
                    WorkingDirectory = artifactPaths.RepositoryRoot,
                    StandardOutputLogPath = stdout,
                    StandardErrorLogPath = stderr,
                    EnvironmentVariables = BuildEnvironment(manifest.RunId, projectDirectory, request.Options, plan),
                    Timeout = BenchmarkRunLimit,
                    ProgressInterval = TimeSpan.FromSeconds(60),
                    Progress = value => progress?.Invoke($"Benchmark {invocation.ProjectKey} is still running ({value.Elapsed:hh\\:mm\\:ss}; {value.StandardOutputLineCount} stdout and {value.StandardErrorLineCount} stderr lines).")
                }, runToken).ConfigureAwait(false);
                if (process.RawExitCode is int exitCode)
                    childExitCodes.Add(exitCode);
                artifacts.Add(Relative(runDirectory, stdout));
                artifacts.Add(Relative(runDirectory, stderr));
                foreach (var line in process.StandardOutputTail)
                    progress?.Invoke(line);
                foreach (var line in process.StandardErrorTail)
                    errorOutput?.Invoke(line);

                if (process.Outcome != OutcomeKind.Success)
                {
                    if (process.Outcome == OutcomeKind.Cancelled && runToken.IsCancellationRequested)
                        runToken.ThrowIfCancellationRequested();
                    results.Add(new BenchmarkProjectResult(invocation.ProjectKey, "Failed", process.RawExitCode, Relative(runDirectory, projectDirectory), process.Failure));
                    failure = process.Outcome;
                    failureDetails = process.Failure ?? $"Benchmark project '{invocation.ProjectKey}' exited with code {process.RawExitCode}.";
                    break;
                }

                var resultError = ValidateResults(invocation.ProjectKey, projectDirectory);
                if (resultError is not null)
                {
                    results.Add(new BenchmarkProjectResult(invocation.ProjectKey, "Incomplete", process.RawExitCode, Relative(runDirectory, projectDirectory), resultError));
                    failure = OutcomeKind.InfrastructureFailure;
                    failureDetails = resultError;
                    break;
                }
                results.Add(new BenchmarkProjectResult(invocation.ProjectKey, "Passed", process.RawExitCode, Relative(runDirectory, projectDirectory), null));
            }

            await WriteReportsAsync(runDirectory, manifest.RunId, plan, results, referenceIdentity, runToken).ConfigureAwait(false);
            artifacts.Add("run-report.json");
            artifacts.Add("report.md");
            if (failure is null)
                return await FinishAsync(manifest, runDirectory, OutcomeKind.Success, null, childExitCodes, artifacts, results).ConfigureAwait(false);
            return await FinishAsync(manifest, runDirectory, failure.Value, failureDetails, childExitCodes, artifacts, results).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (runTimeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await WriteReportsAsync(runDirectory, manifest.RunId, plan, results, null, CancellationToken.None).ConfigureAwait(false);
            artifacts.AddRange(["run-report.json", "report.md"]);
            return await FinishAsync(manifest, runDirectory, OutcomeKind.Timeout,
                $"Benchmark run exceeded its {BenchmarkRunLimit.TotalMinutes:0}-minute budget. {exception.Message}", childExitCodes, artifacts, results).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            await WriteReportsAsync(runDirectory, manifest.RunId, plan, results, null, CancellationToken.None).ConfigureAwait(false);
            artifacts.AddRange(["run-report.json", "report.md"]);
            return await FinishAsync(manifest, runDirectory, OutcomeKind.Cancelled, exception.Message, childExitCodes, artifacts, results).ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            await WriteReportsAsync(runDirectory, manifest.RunId, plan, results, null, CancellationToken.None).ConfigureAwait(false);
            artifacts.AddRange(["run-report.json", "report.md"]);
            return await FinishAsync(manifest, runDirectory, OutcomeKind.Timeout,
                $"Benchmark execution could not acquire its exclusive lease within {BenchmarkLeaseWait.TotalMinutes:0} minutes. {exception.Message}", childExitCodes, artifacts, results).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            await WriteReportsAsync(runDirectory, manifest.RunId, plan, results, null, CancellationToken.None).ConfigureAwait(false);
            artifacts.AddRange(["run-report.json", "report.md"]);
            return await FinishAsync(manifest, runDirectory, OutcomeKind.InfrastructureFailure, exception.Message, childExitCodes, artifacts, results).ConfigureAwait(false);
        }
    }

    private async Task<Dictionary<string, object?>?> VerifyWikipediaReferenceAsync(
        CheckoutIdentity checkout,
        BenchmarkExecutionPlan plan,
        string runDirectory,
        List<string> artifacts,
        List<int> childExitCodes,
        CancellationToken cancellationToken)
    {
        var toolProject = Path.Combine(artifactPaths.RepositoryRoot, "src", "devops", "Rowles.DataForge.Tool", "Rowles.DataForge.Tool.csproj");
        var stdout = Path.Combine(runDirectory, "wikipedia-reference.stdout.log");
        var stderr = Path.Combine(runDirectory, "wikipedia-reference.stderr.log");
        var result = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = ["run", "--project", toolProject, "--framework", "net10.0", "--configuration", "Release", "--", "reference", "verify", "-ReferencePath", plan.ReferencePath!],
            WorkingDirectory = checkout.RepositoryRoot,
            StandardOutputLogPath = stdout,
            StandardErrorLogPath = stderr,
            Timeout = TimeSpan.FromMinutes(10),
            ProgressInterval = TimeSpan.FromSeconds(30),
            Progress = value => progress?.Invoke($"Verifying the frozen Wikipedia reference ({value.Elapsed:hh\\:mm\\:ss}).")
        }, cancellationToken).ConfigureAwait(false);
        if (result.RawExitCode is int exitCode)
            childExitCodes.Add(exitCode);
        artifacts.Add(Relative(runDirectory, stdout));
        artifacts.Add(Relative(runDirectory, stderr));
        if (result.Outcome != OutcomeKind.Success)
            return null;

        var referenceIdentity = DataForgeQualificationEvidence.ReadReferenceIdentity(plan.ReferencePath!, out var failure);
        if (referenceIdentity is null)
            throw new InvalidDataException(failure ?? "The verified artefact manifest does not identify the required Wikipedia reference.");
        var identity = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["sourceKind"] = referenceIdentity.SourceKind,
            ["datasetId"] = referenceIdentity.DatasetId,
            ["datasetVersion"] = referenceIdentity.DatasetVersion,
            ["recordCount"] = referenceIdentity.RecordCount,
            ["logicalByteCount"] = referenceIdentity.LogicalByteCount,
            ["contentSha256"] = referenceIdentity.ContentSha256,
            ["artefactSha256"] = referenceIdentity.ArtefactSha256,
            ["dumpDate"] = referenceIdentity.DumpDate,
            ["primarySha1"] = referenceIdentity.PrimarySha1,
            ["indexSha1"] = referenceIdentity.IndexSha1
        };
        return identity;
    }

    private IReadOnlyDictionary<string, string?> BuildEnvironment(
        Guid runId,
        string artifactDirectory,
        BenchmarkCommandOptions options,
        BenchmarkExecutionPlan plan)
    {
        var sourceManifest = options.SourceManifest is null ? string.Empty : ResolvePath(options.SourceManifest, artifactPaths.RepositoryRoot);
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["LEANCORPUS_RUN_ID"] = runId.ToString("D"),
            ["LEANCORPUS_ARTIFACT_DIR"] = artifactDirectory,
            ["BENCH_DATASET_MODE"] = plan.Dataset,
            ["BENCH_REFERENCE_PATH"] = plan.ReferencePath ?? string.Empty,
            ["BENCH_DOC_COUNT"] = plan.DocumentCount > 0 ? plan.DocumentCount.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty,
            ["BENCH_SOURCE_COMMIT"] = options.SourceCommit ?? string.Empty,
            ["BENCH_SOURCE_REF"] = options.SourceRef ?? string.Empty,
            ["BENCH_SOURCE_MANIFEST"] = sourceManifest
        };
    }

    private IReadOnlyList<string> BuildDotNetArguments(BenchmarkInvocation invocation, BenchmarkExecutionPlan plan)
        => ["run", "--configuration", "Release", "--framework", plan.Framework, "--project", invocation.ProjectPath, "--", .. invocation.Arguments];

    private static string? ValidateResults(string project, string projectDirectory)
    {
        var resultsDirectory = Path.Combine(projectDirectory, "_runner", "results");
        if (!Directory.Exists(resultsDirectory))
            return $"BenchmarkDotNet did not create a results directory for project '{project}'.";
        var resultFiles = Directory.EnumerateFiles(resultsDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(static path => Path.GetExtension(path) is ".md" or ".csv" or ".json")
            .Where(static path => new FileInfo(path).Length > 0)
            .ToArray();
        if (resultFiles.Length == 0)
            return $"BenchmarkDotNet produced no non-empty result files for project '{project}'.";
        if (project == "core")
        {
            var reportPath = Path.Combine(projectDirectory, "report.json");
            if (!File.Exists(reportPath))
                return "The core benchmark runner did not produce its required report.json.";
            try
            {
                using var report = JsonDocument.Parse(File.ReadAllBytes(reportPath));
                var root = report.RootElement;
                if (root.GetProperty("schemaVersion").GetInt32() != 4 || root.GetProperty("totalBenchmarkCount").GetInt32() < 1)
                    return "The core benchmark report is incomplete or uses an unsupported schema.";
                foreach (var suite in root.GetProperty("suites").EnumerateArray())
                {
                    if (suite.GetProperty("failedBenchmarkCount").GetInt32() != 0 || suite.GetProperty("missingBenchmarkCount").GetInt32() != 0)
                        return "The core benchmark report contains failed or missing benchmark results.";
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or KeyNotFoundException)
            {
                return $"The core benchmark report is invalid: {exception.Message}";
            }
        }
        return null;
    }

    private async Task WriteReportsAsync(
        string runDirectory,
        Guid runId,
        BenchmarkExecutionPlan plan,
        IReadOnlyList<BenchmarkProjectResult> results,
        IReadOnlyDictionary<string, object?>? datasetIdentity,
        CancellationToken cancellationToken)
    {
        var report = new
        {
            schemaVersion = 1,
            runId,
            status = results.All(static result => result.Status == "Passed") && results.Count == plan.Invocations.Count ? "Passed" : "Failed",
            framework = plan.Framework,
            strategy = plan.Strategy,
            dataset = new { mode = plan.Dataset, identity = datasetIdentity },
            comparable = true,
            projects = results
        };
        await WriteAtomicAsync(Path.Combine(runDirectory, "run-report.json"), JsonSerializer.Serialize(report, JsonOptions), cancellationToken).ConfigureAwait(false);
        var markdown = new List<string>
        {
            $"# Benchmark run {runId:D}",
            string.Empty,
            $"Status: {report.status}",
            string.Empty,
            "| Project | Status | Path |",
            "| --- | --- | --- |"
        };
        markdown.AddRange(results.Select(result => $"| {result.Project} | {result.Status} | `{result.Path}` |"));
        await WriteAtomicAsync(Path.Combine(runDirectory, "report.md"), string.Join(Environment.NewLine, markdown) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }

    private async Task<BenchmarkCommandResult> FinishAsync(
        RunManifest manifest,
        string runDirectory,
        OutcomeKind outcome,
        string? failure,
        IReadOnlyList<int> childExitCodes,
        IReadOnlyList<string> artifacts,
        IReadOnlyList<BenchmarkProjectResult> results)
    {
        var state = outcome switch
        {
            OutcomeKind.Success => RunState.Succeeded,
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        var finished = await runStore.FinishAsync(manifest, state, artifacts: artifacts, failure: failure,
            cancellationToken: CancellationToken.None, childExitCodes: childExitCodes).ConfigureAwait(false);
        var commandResult = new CommandResult(outcome, outcome == OutcomeKind.Success
            ? $"Benchmark run succeeded with {results.Count} project invocations."
            : $"Benchmark run {state.ToString().ToLowerInvariant()}: {failure}",
            Details: runDirectory);
        return new BenchmarkCommandResult(commandResult, finished.RunId, runDirectory);
    }

    private async Task<bool> IsDotNetToolAvailableAsync(CheckoutIdentity checkout, CancellationToken cancellationToken)
    {
        var probeDirectory = Path.Combine(artifactPaths.ArtifactRoot, "temp", "benchmark-gcdump-probe", Guid.NewGuid().ToString("N"));
        var result = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = ["gcdump", "--version"],
            WorkingDirectory = checkout.RepositoryRoot,
            StandardOutputLogPath = Path.Combine(probeDirectory, "stdout.log"),
            StandardErrorLogPath = Path.Combine(probeDirectory, "stderr.log"),
            Timeout = TimeSpan.FromSeconds(30)
        }, cancellationToken).ConfigureAwait(false);
        return result.Outcome == OutcomeKind.Success;
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temporary, content, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static string ResolvePath(string path, string repositoryRoot)
        => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(repositoryRoot, path));

    private static string QuoteArguments(IEnumerable<string> arguments)
        => string.Join(" ", arguments.Select(argument => argument.Any(char.IsWhiteSpace) ? $"\"{argument.Replace("\"", "\\\"")}\"" : argument));
}

internal sealed record BenchmarkCommandRequest(CheckoutIdentity Checkout, BenchmarkCommandOptions Options);
