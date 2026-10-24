using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Data;

internal sealed record DataForgeQualificationStepResult(
    string Status,
    int? ExitCode,
    double ElapsedSeconds,
    Guid? RunId = null,
    string? RunKind = null,
    string? ArtifactPath = null,
    string? RunDirectory = null,
    string? Error = null);

internal interface IDataForgeQualificationStepRunner
{
    Task<DataForgeQualificationStepResult> ExecuteAsync(
        CheckoutIdentity checkout,
        DataForgeQualificationStep step,
        string qualificationDirectory,
        CancellationToken cancellationToken);
}

internal sealed record DataForgeQualificationResult(CommandResult CommandResult, string? QualificationId, string? QualificationDirectory);

internal sealed class DataForgeQualificationService(
    ArtifactPaths artifactPaths,
    IAtomicFileWriter atomicFileWriter,
    IDataForgeQualificationStepRunner stepRunner,
    DevOpsConfiguration configuration,
    IRepositoryProvenanceReader provenanceReader,
    IDotNetSdkResolver sdkResolver,
    Action<string>? progress = null,
    Action<string>? errorOutput = null)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public async Task<DataForgeQualificationResult> RunAsync(
        CheckoutIdentity checkout,
        DataForgeQualificationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(options);
        var plan = DataForgeQualificationPlan.Create(options);
        var planFailures = plan.Validate(configuration, artifactPaths.RepositoryRoot);
        if (planFailures.Count > 0)
        {
            return new DataForgeQualificationResult(
                new CommandResult(OutcomeKind.ValidationFailure,
                    "DataForge qualification plan validation failed.", Details: string.Join(Environment.NewLine, planFailures)), null, null);
        }
        if (options.Dry)
        {
            progress?.Invoke("DataForge release qualification dry run");
            progress?.Invoke($"Wikipedia reference: {options.ReferencePath}");
            foreach (var step in plan.Steps)
                progress?.Invoke($"[{step.Group}] {step.Command}");
            return new DataForgeQualificationResult(new CommandResult(OutcomeKind.Success, "DataForge qualification plan resolved."), null, null);
        }

        var qualificationId = NewQualificationId();
        var qualificationDirectory = Path.Combine(artifactPaths.ArtifactRoot, "dataforge", "qualification", qualificationId);
        Directory.CreateDirectory(qualificationDirectory);
        var summaryPath = Path.Combine(qualificationDirectory, "qualification.json");
        var git = await provenanceReader.ReadAsync(checkout, qualificationDirectory, cancellationToken).ConfigureAwait(false);
        var sdk = await sdkResolver.ResolveAsync(checkout, qualificationDirectory, cancellationToken).ConfigureAwait(false);
        var summary = new QualificationDocument
        {
            QualificationId = qualificationId,
            StartedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            Source = new QualificationSourceDocument
            {
                Commit = git.Commit,
                Branch = git.Branch,
                Dirty = git.Dirty,
                Os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                Architecture = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                SdkVersion = sdk.SelectedVersion
            },
            Wikipedia = new QualificationWikipediaDocument
            {
                ReferencePath = Path.GetRelativePath(artifactPaths.RepositoryRoot, options.ReferencePath).Replace('\\', '/')
            }
        };

        if (git.Outcome != OutcomeKind.Success)
            summary.Blockers.Add($"Could not record source provenance: {git.Failure}");
        if (sdk.Outcome != OutcomeKind.Success)
            summary.Blockers.Add($"Could not resolve the SDK selected by global.json: {sdk.Failure}");
        await WriteSummaryAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
        progress?.Invoke($"DataForge qualification run {qualificationId}");
        progress?.Invoke($"Wikipedia reference: {options.ReferencePath}");
        progress?.Invoke($"Commit: {git.Commit ?? "unknown"}");

        if (git.Outcome != OutcomeKind.Success || sdk.Outcome != OutcomeKind.Success)
        {
            foreach (var step in plan.Steps)
                summary.Commands.Add(Skipped(step, "Skipped because qualification provenance or SDK resolution failed."));
            return await FinishAsync(summary, summaryPath, qualificationDirectory, RunState.Failed, cancellationToken).ConfigureAwait(false);
        }

        var buildPassed = false;
        var profileCatalogVerified = false;
        var referenceVerified = false;
        var wikipediaSmokePassed = false;
        var syntheticIdentities = new Dictionary<string, DataForgeSyntheticIdentity>(StringComparer.Ordinal);
        var cancellationObserved = false;

        foreach (var step in plan.Steps)
        {
            if (cancellationObserved)
            {
                summary.Commands.Add(Skipped(step, "Skipped because qualification was cancelled."));
                continue;
            }

            if (step.Kind == DataForgeQualificationStepKind.ReferenceVerification &&
                !File.Exists(Path.Combine(options.ReferencePath, "manifest.json")))
            {
                var failure = $"The frozen Wikipedia v1 reference is missing at '{options.ReferencePath}'; qualification does not download or rebuild it.";
                summary.Commands.Add(new QualificationCommandDocument(step, "Blocked", null, 0, error: failure));
                summary.Blockers.Add(failure);
                await WriteSummaryAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (step.Kind == DataForgeQualificationStepKind.ProfileVerification && !buildPassed)
            {
                summary.Commands.Add(Skipped(step, "Skipped because the solution build did not pass."));
                await WriteSummaryAsync(summaryPath, summary, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            if ((step.Kind is DataForgeQualificationStepKind.ReferenceVerification or DataForgeQualificationStepKind.Test or DataForgeQualificationStepKind.Benchmark) &&
                !profileCatalogVerified)
            {
                summary.Commands.Add(Skipped(step, "Skipped because required DataForge profiles were not verified."));
                await WriteSummaryAsync(summaryPath, summary, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            if ((step.Kind is DataForgeQualificationStepKind.Test or DataForgeQualificationStepKind.Benchmark) && !buildPassed)
            {
                summary.Commands.Add(Skipped(step, "Skipped because the solution build did not pass."));
                await WriteSummaryAsync(summaryPath, summary, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            if ((step.Group == "wikipedia-smoke" || step.Group == "wikipedia-benchmarks") && !referenceVerified)
            {
                summary.Commands.Add(Skipped(step, "Skipped because the frozen Wikipedia reference did not verify."));
                wikipediaSmokePassed = false;
                await WriteSummaryAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (step.Group == "wikipedia-benchmarks" && !wikipediaSmokePassed)
            {
                summary.Commands.Add(Skipped(step, "Skipped because one or more Wikipedia fast smoke suites failed."));
                await WriteSummaryAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
                continue;
            }

            progress?.Invoke($"[{step.Group}] {step.Name}");
            DataForgeQualificationStepResult result;
            try
            {
                result = await stepRunner.ExecuteAsync(checkout, step, qualificationDirectory, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                result = new DataForgeQualificationStepResult("Cancelled", null, 0, Error: "Qualification step was cancelled.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
            {
                result = new DataForgeQualificationStepResult("Failed", null, 0, Error: exception.Message);
            }

            if (result.Status == "Passed" && !HasRunEvidence(step, result))
            {
                result = result with
                {
                    Status = "Failed",
                    Error = $"Successful '{step.Name}' step did not return its expected run identity and artifact path."
                };
            }

            if (step.Kind == DataForgeQualificationStepKind.Build)
            {
                buildPassed = result.Status == "Passed";
                if (!buildPassed)
                    summary.Blockers.Add($"The solution build did not pass (exit code {result.ExitCode}).");
            }

            if (step.Kind == DataForgeQualificationStepKind.ProfileVerification && result.Status == "Passed")
            {
                var catalog = result.RunDirectory is null
                    ? new DataForgeProfileCatalogReadResult([], "DataForge profile listing did not return a run directory.")
                    : DataForgeQualificationEvidence.ReadProfileCatalog(result.RunDirectory);
                if (catalog.Failure is not null)
                {
                    result = result with { Status = "Failed", Error = catalog.Failure };
                }
                else
                {
                    var missing = configuration.RequiredDataForgeSyntheticProfiles
                        .Where(required => !catalog.Profiles.Any(available =>
                            string.Equals(available.ProfileId, required.Id, StringComparison.Ordinal) &&
                            available.ProfileVersion == required.Version))
                        .ToArray();
                    if (missing.Length > 0)
                    {
                        result = result with
                        {
                            Status = "Failed",
                            Error = "DataForge profiles required for qualification are missing or have a different version: " +
                                string.Join(", ", missing.Select(static profile => $"{profile.Id}/v{profile.Version}"))
                        };
                    }
                    else
                    {
                        profileCatalogVerified = true;
                    }
                }
            }

            if (step.Kind == DataForgeQualificationStepKind.ReferenceVerification && result.Status == "Passed")
            {
                var identity = DataForgeQualificationEvidence.ReadReferenceIdentity(options.ReferencePath, out var identityFailure);
                if (identity is null)
                {
                    result = result with { Status = "Failed", Error = identityFailure };
                    summary.Blockers.Add(identityFailure ?? "Wikipedia reference identity validation failed.");
                }
                else
                {
                    summary.Wikipedia.Identity = identity;
                    referenceVerified = true;
                    wikipediaSmokePassed = buildPassed;
                }
            }

            if (step.Group == "synthetic-benchmarks" && result.Status == "Passed")
            {
                if (result.RunId is null || result.RunDirectory is null || result.RunKind != "benchmark")
                {
                    result = result with { Status = "Failed", Error = "Successful synthetic benchmark did not return its benchmark run identity and directory." };
                }
                else
                {
                    var read = DataForgeQualificationEvidence.ReadSyntheticIdentities(result.RunDirectory, result.RunId.Value.ToString("D"));
                    foreach (var failure in read.Failures)
                        summary.Blockers.Add(failure);
                    foreach (var identity in read.Identities)
                    {
                        var key = SyntheticIdentityKey(identity);
                        if (syntheticIdentities.TryGetValue(key, out var existing))
                        {
                            if (!existing.BenchmarkRunIds.Contains(result.RunId.Value.ToString("D"), StringComparer.Ordinal))
                                existing.BenchmarkRunIds.Add(result.RunId.Value.ToString("D"));
                        }
                        else
                        {
                            syntheticIdentities.Add(key, identity);
                        }
                    }
                }
            }

            if (step.Group == "wikipedia-smoke" && result.Status == "Passed")
            {
                var error = result.RunDirectory is null || summary.Wikipedia.Identity is null
                    ? "Successful Wikipedia smoke did not return the verified dataset identity or run directory."
                    : DataForgeQualificationEvidence.ReadWikipediaReportError(result.RunDirectory, summary.Wikipedia.Identity);
                if (error is not null)
                    result = result with { Status = "Failed", Error = error };
                wikipediaSmokePassed &= result.Status == "Passed";
            }
            else if (step.Group == "wikipedia-smoke")
            {
                wikipediaSmokePassed = false;
            }

            if (step.Group == "wikipedia-benchmarks" && result.Status == "Passed")
            {
                var error = result.RunDirectory is null || summary.Wikipedia.Identity is null
                    ? "Successful Wikipedia benchmark did not return the verified dataset identity or run directory."
                    : DataForgeQualificationEvidence.ReadWikipediaReportError(result.RunDirectory, summary.Wikipedia.Identity);
                if (error is not null)
                    result = result with { Status = "Failed", Error = error };
            }

            var commandDocument = new QualificationCommandDocument(step, result.Status, result.ExitCode,
                result.ElapsedSeconds, result.RunId?.ToString("D"), result.RunKind, result.ArtifactPath, result.Error);
            summary.Commands.Add(commandDocument);
            if (result.Status == "Cancelled")
                cancellationObserved = true;
            else if (result.Status != "Passed")
                summary.Blockers.Add($"{step.Name} did not pass (exit code {result.ExitCode}).{(string.IsNullOrWhiteSpace(result.Error) ? string.Empty : " " + result.Error)}");

            if (step.Group == "synthetic-benchmarks" &&
                !plan.Steps.SkipWhile(candidate => candidate != step).Skip(1).Any(candidate => candidate.Group == "synthetic-benchmarks"))
            {
                foreach (var requiredProfile in configuration.RequiredDataForgeSyntheticProfiles)
                {
                    if (!syntheticIdentities.Values.Any(identity =>
                        identity.ProfileId == requiredProfile.Id && identity.ProfileVersion == requiredProfile.Version))
                        summary.Blockers.Add($"No normal benchmark run recorded the '{requiredProfile.Id}' synthetic identity at version {requiredProfile.Version}.");
                }
            }

            if (step.Group == "wikipedia-smoke" && result.Status != "Passed" && !string.IsNullOrWhiteSpace(result.Error))
                summary.Blockers.Add($"{step.Name} did not record the verified Wikipedia identity: {result.Error}");
            if (step.Group == "wikipedia-benchmarks" && result.Status != "Passed" && !string.IsNullOrWhiteSpace(result.Error))
                summary.Blockers.Add($"{step.Name} did not record the verified Wikipedia identity: {result.Error}");

            summary.Synthetic.Identities = syntheticIdentities.Values
                .OrderBy(identity => identity.ProfileId, StringComparer.Ordinal)
                .ThenBy(identity => identity.RecordCount)
                .ThenBy(identity => identity.ContentSha256, StringComparer.Ordinal)
                .ToList();
            summary.Wikipedia.Verified = referenceVerified;
            await WriteSummaryAsync(summaryPath, summary,
                cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
                cancellationObserved = true;
        }

        var finalState = cancellationObserved ? RunState.Cancelled :
            summary.Blockers.Count == 0 && summary.Commands.All(command => command.Status == "Passed")
                ? RunState.Succeeded
                : RunState.Failed;
        var finished = await FinishAsync(summary, summaryPath, qualificationDirectory, finalState, CancellationToken.None).ConfigureAwait(false);
        foreach (var blocker in summary.Blockers)
            errorOutput?.Invoke(blocker);
        return finished;
    }

    private async Task<DataForgeQualificationResult> FinishAsync(
        QualificationDocument summary,
        string summaryPath,
        string qualificationDirectory,
        RunState terminalState,
        CancellationToken cancellationToken)
    {
        summary.Status = terminalState switch
        {
            RunState.Succeeded => "Passed",
            RunState.Cancelled => "Cancelled",
            _ => "Failed"
        };
        summary.CompletedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        await WriteSummaryAsync(summaryPath, summary, cancellationToken).ConfigureAwait(false);
        var outcome = terminalState switch
        {
            RunState.Succeeded => OutcomeKind.Success,
            RunState.Cancelled => OutcomeKind.Cancelled,
            _ => OutcomeKind.CommandFailure
        };
        var message = outcome switch
        {
            OutcomeKind.Success => "DataForge qualification passed.",
            OutcomeKind.Cancelled => "DataForge qualification was cancelled.",
            _ => "DataForge qualification recorded one or more blockers."
        };
        return new DataForgeQualificationResult(new CommandResult(outcome, message, Details: qualificationDirectory), summary.QualificationId, qualificationDirectory);
    }

    private async Task WriteSummaryAsync(string path, QualificationDocument summary, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(summary, JsonOptions);
        await atomicFileWriter.WriteReplaceAsync(path, payload, cancellationToken).ConfigureAwait(false);
    }

    private static QualificationCommandDocument Skipped(DataForgeQualificationStep step, string reason)
        => new(step, "Skipped", null, 0, error: reason);

    private static string SyntheticIdentityKey(DataForgeSyntheticIdentity identity)
        => string.Join('|', identity.ProfileId, identity.ProfileVersion, identity.RecordCount, identity.Parameters.GetRawText(), identity.ContentSha256);

    private static bool HasRunEvidence(DataForgeQualificationStep step, DataForgeQualificationStepResult result)
    {
        var expectedKind = step.Kind switch
        {
            DataForgeQualificationStepKind.Build => "build",
            DataForgeQualificationStepKind.ProfileVerification => "dataforge",
            DataForgeQualificationStepKind.ReferenceVerification => "dataforge",
            DataForgeQualificationStepKind.Test => "test",
            DataForgeQualificationStepKind.Benchmark => "benchmark",
            _ => string.Empty
        };
        return result.RunId is not null && result.RunKind == expectedKind &&
            !string.IsNullOrWhiteSpace(result.ArtifactPath) &&
            !string.IsNullOrWhiteSpace(result.RunDirectory) && Directory.Exists(result.RunDirectory);
    }

    private static string NewQualificationId()
    {
        var os = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux";
        return $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..24] + $"-{os}";
    }

    private sealed class QualificationDocument
    {
        public int SchemaVersion { get; init; } = 1;
        public string QualificationId { get; init; } = string.Empty;
        public string Status { get; set; } = "Running";
        public string StartedAtUtc { get; init; } = string.Empty;
        public string? CompletedAtUtc { get; set; }
        public QualificationSourceDocument Source { get; init; } = new();
        public QualificationSyntheticDocument Synthetic { get; init; } = new();
        public QualificationWikipediaDocument Wikipedia { get; init; } = new();
        public List<QualificationCommandDocument> Commands { get; } = [];
        public List<string> Blockers { get; } = [];
    }

    private sealed class QualificationSourceDocument
    {
        public string? Commit { get; init; }
        public string? Branch { get; init; }
        public bool Dirty { get; init; }
        public string Os { get; init; } = string.Empty;
        public string Architecture { get; init; } = string.Empty;
        public string? SdkVersion { get; init; }
    }

    private sealed class QualificationSyntheticDocument
    {
        public string Mode { get; init; } = "synthetic";
        public List<DataForgeSyntheticIdentity> Identities { get; set; } = [];
    }

    private sealed class QualificationWikipediaDocument
    {
        public string ReferencePath { get; init; } = string.Empty;
        public bool Verified { get; set; }
        public DataForgeWikipediaIdentity? Identity { get; set; }
    }

    private sealed class QualificationCommandDocument(
        DataForgeQualificationStep step,
        string status,
        int? exitCode,
        double elapsedSeconds,
        string? runId = null,
        string? runKind = null,
        string? artifactPath = null,
        string? error = null)
    {
        public string Group { get; } = step.Group;
        public string Name { get; } = step.Name;
        public string Command { get; } = step.Command;
        public string Status { get; } = status;
        public int? ExitCode { get; } = exitCode;
        public double ElapsedSeconds { get; } = elapsedSeconds;
        public string? RunId { get; } = runId;
        public string? RunKind { get; } = runKind;
        public string? ArtifactPath { get; } = artifactPath;
        public string? Error { get; } = error;
    }
}
