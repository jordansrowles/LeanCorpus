using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Benchmarks;

internal sealed record BenchmarkQualificationSummaryCommandResult(CommandResult CommandResult, string RunDirectory);

internal sealed class BenchmarkQualificationSummaryCommandService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IRepositoryProvenanceReader provenanceReader,
    IAtomicFileWriter? atomicWriter = null)
{
    private readonly IAtomicFileWriter _atomicWriter = atomicWriter ?? new AtomicFileWriter();

    public async Task<BenchmarkQualificationSummaryCommandResult> RunAsync(
        CheckoutIdentity checkout,
        BenchmarkQualificationSummaryCommandOptions options,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ReportsPath))
            return ValidationFailure("--reports must name a collected run-report file.");
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            return ValidationFailure("--output must name a qualification-summary file.");
        if (!options.PlanOnly && options.ExpectedRunCount is null or < 1)
            return ValidationFailure("--expected-run-count must be positive unless --plan-only is set.");

        string reportsPath = ResolvePath(checkout.RepositoryRoot, options.ReportsPath);
        string outputPath = ResolvePath(checkout.RepositoryRoot, options.OutputPath);
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["reportsPath"] = reportsPath,
            ["outputPath"] = outputPath,
            ["planOnly"] = options.PlanOnly.ToString().ToLowerInvariant(),
            ["expectedRunCount"] = options.PlanOnly ? "0" : options.ExpectedRunCount!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        RunManifest started = await runStore.StartAsync("benchmark qualification-summary", inputs,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        string runDirectory = artifactPaths.GetRunDirectory("benchmark", started.RunId);

        GitProvenance provenance = await provenanceReader.ReadAsync(checkout, runDirectory, cancellationToken).ConfigureAwait(false);
        if (provenance.Outcome != OutcomeKind.Success || string.IsNullOrWhiteSpace(provenance.Commit))
        {
            string failure = provenance.Failure ?? "Git source identity is unavailable.";
            await runStore.FinishAsync(started, RunState.Failed, childExitCode: 3, failure: failure,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return new BenchmarkQualificationSummaryCommandResult(
                new CommandResult(OutcomeKind.InfrastructureFailure, "Could not identify the benchmark summary source checkout.", 3, failure), runDirectory);
        }

        RunManifest running = await runStore.UpdateRunningAsync(started, provenance.Commit, provenance.Branch,
            provenance.Dirty, cancellationToken: cancellationToken).ConfigureAwait(false);
        try
        {
            string? collectedReports = File.Exists(reportsPath)
                ? await File.ReadAllTextAsync(reportsPath, cancellationToken).ConfigureAwait(false)
                : null;
            var shardExits = ReadShardExitCodes(options.PlanOnly ? 0 : options.ExpectedRunCount!.Value - 1, environment);
            BenchmarkQualificationSummaryDocument summary = BenchmarkQualificationSummaryBuilder.Build(
                collectedReports,
                provenance.Commit,
                options.PlanOnly,
                options.ExpectedRunCount ?? 0,
                shardExits);
            byte[] summaryBytes = JsonSerializer.SerializeToUtf8Bytes(summary, BenchmarkQualificationSummaryBuilder.JsonOptions);
            string evidencePath = Path.Combine(runDirectory, "qualification-summary.json");
            await _atomicWriter.WriteReplaceAsync(evidencePath, summaryBytes, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(Path.GetFullPath(outputPath), Path.GetFullPath(evidencePath), PathComparison))
                await _atomicWriter.WriteReplaceAsync(outputPath, summaryBytes, cancellationToken).ConfigureAwait(false);

            string? stepSummaryPath = GetEnvironmentValue("GITHUB_STEP_SUMMARY", environment);
            if (!string.IsNullOrWhiteSpace(stepSummaryPath))
            {
                string markdown = BenchmarkQualificationSummaryBuilder.CreateStepSummary(summary);
                await File.AppendAllTextAsync(stepSummaryPath, markdown, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            }

            bool succeeded = summary.FailureReasons.Count == 0;
            string? failureMessage = succeeded ? null : string.Join(" ", summary.FailureReasons);
            RunState terminalState = succeeded ? RunState.Succeeded : RunState.Failed;
            await runStore.FinishAsync(running, terminalState, childExitCode: succeeded ? 0 : 1,
                artifacts: ["qualification-summary.json", "git-commit.stdout.log", "git-commit.stderr.log",
                    "git-branch.stdout.log", "git-branch.stderr.log", "git-status.stdout.log", "git-status.stderr.log"],
                failure: failureMessage,
                cancellationToken: cancellationToken,
                additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sourceCommit"] = summary.SourceCommit,
                    ["runCount"] = summary.RunCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["qualifiedPairCount"] = summary.QualifiedPairCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["failureReasonCodes"] = JsonSerializer.Serialize(summary.FailureReasonCodes)
                }).ConfigureAwait(false);

            var result = succeeded
                ? new CommandResult(OutcomeKind.Success, "Benchmark qualification summary written.", Details: outputPath)
                : new CommandResult(OutcomeKind.CommandFailure, "Benchmark qualification is incomplete.", 1,
                    $"{failureMessage}{Environment.NewLine}Summary: {outputPath}");
            return new BenchmarkQualificationSummaryCommandResult(result, runDirectory);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await runStore.FinishAsync(running, RunState.Cancelled, childExitCode: 130,
                failure: "Benchmark qualification summary was cancelled.", cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new BenchmarkQualificationSummaryCommandResult(
                new CommandResult(OutcomeKind.Cancelled, "Benchmark qualification summary was cancelled.", 130), runDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            await runStore.FinishAsync(running, RunState.Failed, childExitCode: 3, failure: exception.Message,
                cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new BenchmarkQualificationSummaryCommandResult(
                new CommandResult(OutcomeKind.InfrastructureFailure, "Could not write the benchmark qualification summary.", 3, exception.Message), runDirectory);
        }
    }

    private static IReadOnlyDictionary<string, string?> ReadShardExitCodes(int shardCount,
        IReadOnlyDictionary<string, string?>? environment)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        for (var shard = 1; shard <= shardCount; shard++)
        {
            string name = $"SHARD{shard}_EXIT";
            values[name] = GetEnvironmentValue(name, environment);
        }
        return values;
    }

    private static string? GetEnvironmentValue(string name, IReadOnlyDictionary<string, string?>? environment)
    {
        if (environment is not null && environment.TryGetValue(name, out string? value)) return value;
        return Environment.GetEnvironmentVariable(name);
    }

    private static string ResolvePath(string repositoryRoot, string path)
        => Path.GetFullPath(path, repositoryRoot);

    private static BenchmarkQualificationSummaryCommandResult ValidationFailure(string message)
        => new(new CommandResult(OutcomeKind.ValidationFailure, message), string.Empty);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
