using System.Security.Cryptography;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record BackupRestoreMatrixOptions
{
    public string Framework { get; init; } = "net10.0";
    public string Configuration { get; init; } = "Release";
    public int Count { get; init; } = 3;
    public string? AlternateTempRoot { get; init; }
    public string? SourceManifestPath { get; init; }
    public int? Iterations { get; init; }
    public bool NoRestore { get; init; }
    public string CommandLine { get; init; } = "devops test backup-restore-matrix";
}

internal sealed record BackupRestoreMatrixVariant(string Name, bool Compound, bool SourceDurable, string? TempRoot);

internal sealed record BackupRestoreMatrixResult(
    CommandResult CommandResult,
    string RunDirectory,
    RunManifest? Manifest,
    int CompletedCells);

internal sealed class BackupRestoreMatrixService
{
    private const string MeasurementTest = "FullyQualifiedName~BackupRestoreStateMachineTests.Backup_restore_operations_match_the_model";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] ExpectedFrameworks = ["net10.0", "net11.0"];

    private readonly DevOpsConfiguration _configuration;
    private readonly ArtifactPaths _paths;
    private readonly IProcessRunner _processRunner;
    private readonly IDotNetSdkResolver _sdkResolver;
    private readonly IRepositoryProvenanceReader _provenanceReader;
    private readonly IAtomicFileWriter _atomicFileWriter;
    private readonly BackupRestoreSourceManifestReader _sourceReader;

    public BackupRestoreMatrixService(
        DevOpsConfiguration configuration,
        ArtifactPaths paths,
        IProcessRunner processRunner,
        IDotNetSdkResolver sdkResolver,
        IRepositoryProvenanceReader provenanceReader,
        IAtomicFileWriter? atomicFileWriter = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _sdkResolver = sdkResolver ?? throw new ArgumentNullException(nameof(sdkResolver));
        _provenanceReader = provenanceReader ?? throw new ArgumentNullException(nameof(provenanceReader));
        _atomicFileWriter = atomicFileWriter ?? new AtomicFileWriter();
        _sourceReader = new BackupRestoreSourceManifestReader(_processRunner);
    }

    public static IReadOnlyList<BackupRestoreMatrixVariant> CreateVariants(string alternateTempRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alternateTempRoot);
        return
        [
            new("baseline", Compound: false, SourceDurable: true, TempRoot: null),
            new("compound", Compound: true, SourceDurable: true, TempRoot: null),
            new("source-nondurable", Compound: false, SourceDurable: false, TempRoot: null),
            new("combined", Compound: true, SourceDurable: false, TempRoot: null),
            new("alternate-temp", Compound: false, SourceDurable: true, TempRoot: alternateTempRoot)
        ];
    }

    public static string? ValidateOptions(BackupRestoreMatrixOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!ExpectedFrameworks.Contains(options.Framework, StringComparer.OrdinalIgnoreCase))
        {
            return "Matrix framework must be net10.0 or net11.0.";
        }
        if (options.Count < 1)
        {
            return "Matrix count must be a positive integer.";
        }
        if (options.Count > int.MaxValue / 5)
        {
            return "Matrix count is too large to represent its expected cell count.";
        }
        if (options.Iterations is <= 0)
        {
            return "Chaos iterations must be a positive integer when provided.";
        }
        if (string.IsNullOrWhiteSpace(options.Configuration))
        {
            return "Matrix configuration cannot be empty.";
        }
        if (string.IsNullOrWhiteSpace(options.CommandLine))
        {
            return "Matrix command line cannot be empty.";
        }
        if (options.AlternateTempRoot is not null && string.IsNullOrWhiteSpace(options.AlternateTempRoot))
        {
            return "Alternate temporary root cannot be empty.";
        }
        try
        {
            if (options.AlternateTempRoot is not null)
            {
                _ = Path.GetFullPath(options.AlternateTempRoot);
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"Alternate temporary root is invalid: {exception.Message}";
        }
        return null;
    }

    internal static IReadOnlyList<string> GetEnvironmentOverrideNames(bool includeIterations)
    {
        var names = new List<string>
        {
            "LEANCORPUS_BACKUP_RESTORE_MEASUREMENT",
            "LEANCORPUS_BACKUP_COMPOUND",
            "LEANCORPUS_BACKUP_SOURCE_DURABLE",
            "LEANCORPUS_BACKUP_TEMP_ROOT",
            "LEANCORPUS_BACKUP_SOURCE_COMMIT",
            "LEANCORPUS_BACKUP_SOURCE_FINGERPRINT"
        };
        if (includeIterations)
        {
            names.Add("CHAOS_ITERATIONS");
        }
        return names.Order(StringComparer.Ordinal).ToArray();
    }

    public async Task<BackupRestoreMatrixResult> RunAsync(
        CheckoutIdentity checkout,
        BackupRestoreMatrixOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(options);
        if (ValidateOptions(options) is { } validationError)
        {
            return new BackupRestoreMatrixResult(new CommandResult(OutcomeKind.ValidationFailure,
                "Backup/restore matrix options are invalid.", Details: validationError), string.Empty, null, 0);
        }
        if (!Path.GetFullPath(checkout.RepositoryRoot).Equals(_paths.RepositoryRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            return new BackupRestoreMatrixResult(new CommandResult(OutcomeKind.ValidationFailure,
                "Backup/restore matrix checkout does not match its artefact root."), string.Empty, null, 0);
        }

        var preflightDirectory = Path.Combine(Path.GetTempPath(), "leancorpus-backup-restore-preflight", Guid.NewGuid().ToString("N"));
        GitProvenance provenance;
        BackupRestoreSourceManifest source;
        try
        {
            Directory.CreateDirectory(preflightDirectory);
            provenance = await _provenanceReader.ReadAsync(checkout, preflightDirectory, cancellationToken).ConfigureAwait(false);
            if (provenance.Outcome != OutcomeKind.Success || string.IsNullOrWhiteSpace(provenance.Commit))
            {
                return new BackupRestoreMatrixResult(new CommandResult(provenance.Outcome,
                    "Backup/restore matrix source provenance could not be read.", Details: provenance.Failure), string.Empty, null, 0);
            }
            source = await _sourceReader.ReadAsync(checkout.RepositoryRoot, provenance.Commit,
                options.SourceManifestPath, checkout.RepositoryRoot, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BackupRestoreMatrixResult(new CommandResult(OutcomeKind.Cancelled,
                "Backup/restore matrix preflight was cancelled."), string.Empty, null, 0);
        }
        catch (Exception exception) when (IsHandled(exception))
        {
            return new BackupRestoreMatrixResult(new CommandResult(OutcomeKind.ValidationFailure,
                "Backup/restore matrix source validation failed.", Details: exception.Message), string.Empty, null, 0);
        }
        finally
        {
            TryDeleteDirectory(preflightDirectory);
        }

        var alternateRoot = options.AlternateTempRoot is null
            ? Path.Combine(checkout.RepositoryRoot, "artifacts", "temp", "backup-restore-matrix")
            : Path.GetFullPath(options.AlternateTempRoot);
        var variants = CreateVariants(alternateRoot);
        var expectedCells = checked(variants.Count * options.Count);
        var store = new ArtifactRunStore(_paths, _atomicFileWriter, runCategory: "test");
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["target"] = "backup-restore-matrix",
            ["framework"] = NormaliseFramework(options.Framework),
            ["configuration"] = options.Configuration,
            ["count"] = options.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["expectedCells"] = expectedCells.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["sourceCommit"] = source.Commit,
            ["sourceFingerprint"] = source.Fingerprint,
            ["environmentOverrideNames"] = JsonSerializer.Serialize(GetEnvironmentOverrideNames(options.Iterations is not null))
        };
        if (options.Iterations is { } iterations)
        {
            inputs["iterations"] = iterations.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        RunManifest manifest;
        string runDirectory;
        try
        {
            manifest = await store.StartAsync("test-backup-restore-matrix", inputs,
                sourceCommit: provenance.Commit, sourceDirty: provenance.Dirty,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            runDirectory = _paths.GetRunDirectory("test", manifest.RunId);
            manifest = await store.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
                cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BackupRestoreMatrixResult(new CommandResult(OutcomeKind.Cancelled,
                "Backup/restore matrix was cancelled before execution."), string.Empty, null, 0);
        }
        catch (Exception exception) when (IsHandled(exception))
        {
            return new BackupRestoreMatrixResult(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Backup/restore matrix run could not be created.", Details: exception.Message), string.Empty, null, 0);
        }

        var cells = new List<BackupRestoreMatrixCell>(expectedCells);
        var artifacts = new List<string> { "source-manifest.json", "matrix.json" };
        try
        {
            await _atomicFileWriter.WriteReplaceAsync(Path.Combine(runDirectory, "source-manifest.json"),
                JsonSerializer.SerializeToUtf8Bytes(source, JsonOptions), cancellationToken).ConfigureAwait(false);
            await WriteMatrixAsync(runDirectory, manifest.RunId, source, cells, expectedCells, "running", cancellationToken).ConfigureAwait(false);

            var planner = new TestRunPlanner(_configuration, new AffectedSelectionService(_processRunner));
            var executor = new TestExecutor(_paths,
                new ArtifactRunStore(_paths, _atomicFileWriter, runCategory: "test"), _processRunner, _sdkResolver,
                _provenanceReader);
            foreach (var variant in variants)
            {
                for (var launch = 1; launch <= options.Count; launch++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var measurementName = $"{variant.Name}-{launch}.json";
                    var measurementPath = Path.Combine(runDirectory, measurementName);
                    var overrides = new Dictionary<string, string?>(StringComparer.Ordinal)
                    {
                        ["LEANCORPUS_BACKUP_RESTORE_MEASUREMENT"] = measurementPath,
                        ["LEANCORPUS_BACKUP_COMPOUND"] = variant.Compound ? "true" : "false",
                        ["LEANCORPUS_BACKUP_SOURCE_DURABLE"] = variant.SourceDurable ? "true" : "false",
                        ["LEANCORPUS_BACKUP_TEMP_ROOT"] = variant.TempRoot,
                        ["LEANCORPUS_BACKUP_SOURCE_COMMIT"] = source.Commit,
                        ["LEANCORPUS_BACKUP_SOURCE_FINGERPRINT"] = source.Fingerprint
                    };
                    if (options.Iterations is { } configuredIterations)
                    {
                        overrides["CHAOS_ITERATIONS"] = configuredIterations.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }

                    var testRequest = await planner.PlanAsync(checkout, new TestRunOptions
                    {
                        Selection = new TestTargetRequest
                        {
                            Suites = ["core"],
                            Framework = NormaliseFramework(options.Framework),
                            FrameworkExplicit = true,
                            Configuration = options.Configuration,
                            Filter = MeasurementTest
                        },
                        Count = 1,
                        NoRestore = options.NoRestore,
                        Profile = "integration",
                        HangTimeout = "off",
                        Commit = string.Empty,
                        Range = string.Empty,
                        CommandLine = $"{options.CommandLine} --cell {variant.Name}-{launch}",
                        EnvironmentOverrides = overrides
                    }, cancellationToken).ConfigureAwait(false);
                    testRequest = testRequest with { ParentRunId = manifest.RunId };
                    var testResult = await executor.RunAsync(testRequest, cancellationToken).ConfigureAwait(false);
                    if (testResult.CommandResult.Outcome != OutcomeKind.Success || testResult.Manifest is null)
                    {
                        throw new BackupRestoreMatrixException(testResult.CommandResult.Outcome,
                            $"Matrix cell '{variant.Name}-{launch}' failed: {testResult.CommandResult.Details ?? testResult.CommandResult.Message}; child run {testResult.Manifest?.RunId.ToString("D") ?? "unavailable"}.");
                    }

                    var measurement = await ReadAndValidateMeasurementAsync(measurementPath, variant, source,
                        NormaliseFramework(options.Framework), options.Iterations, cancellationToken).ConfigureAwait(false);
                    cells.Add(new BackupRestoreMatrixCell(variant.Name, launch, testResult.Manifest.RunId,
                        measurementName, measurement));
                    artifacts.Add($"../{testResult.Manifest.RunId:D}/run.json");
                    artifacts.Add($"../{testResult.Manifest.RunId:D}/report.json");
                    await WriteMatrixAsync(runDirectory, manifest.RunId, source, cells, expectedCells,
                        "running", cancellationToken).ConfigureAwait(false);
                }
            }

            ValidateMatchedReports(cells);
            var endingProvenance = await _provenanceReader.ReadAsync(checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (endingProvenance.Outcome != OutcomeKind.Success || !string.Equals(endingProvenance.Commit, provenance.Commit, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Repository commit changed during backup/restore matrix measurement.");
            }
            var endingSource = await _sourceReader.ReadAsync(checkout.RepositoryRoot, provenance.Commit,
                options.SourceManifestPath, checkout.RepositoryRoot, cancellationToken).ConfigureAwait(false);
            if (!endingSource.Fingerprint.Equals(source.Fingerprint, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Source changed during backup/restore matrix measurement.");
            }

            await WriteMatrixAsync(runDirectory, manifest.RunId, source, cells, expectedCells, "passed", CancellationToken.None).ConfigureAwait(false);
            var completedManifest = await store.FinishAsync(manifest, RunState.Succeeded,
                artifacts: artifacts, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new BackupRestoreMatrixResult(new CommandResult(OutcomeKind.Success,
                $"Matched backup/restore matrix. Run evidence: {runDirectory}"), runDirectory,
                completedManifest, cells.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await WriteMatrixAsync(runDirectory, manifest.RunId, source, cells, expectedCells, "cancelled", CancellationToken.None).ConfigureAwait(false);
            var completedManifest = await store.FinishAsync(manifest, RunState.Cancelled,
                artifacts: artifacts, failure: "Backup/restore matrix was cancelled.", cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new BackupRestoreMatrixResult(new CommandResult(OutcomeKind.Cancelled,
                "Backup/restore matrix was cancelled."), runDirectory, completedManifest, cells.Count);
        }
        catch (BackupRestoreMatrixException exception)
        {
            await WriteMatrixAsync(runDirectory, manifest.RunId, source, cells, expectedCells, "failed", CancellationToken.None).ConfigureAwait(false);
            var completedManifest = await store.FinishAsync(manifest, RunState.Failed,
                artifacts: artifacts, failure: exception.Message, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new BackupRestoreMatrixResult(new CommandResult(exception.Outcome,
                "Backup/restore matrix did not complete.", Details: exception.Message), runDirectory, completedManifest, cells.Count);
        }
        catch (Exception exception) when (IsHandled(exception))
        {
            await WriteMatrixAsync(runDirectory, manifest.RunId, source, cells, expectedCells, "failed", CancellationToken.None).ConfigureAwait(false);
            var completedManifest = await store.FinishAsync(manifest, RunState.Failed,
                artifacts: artifacts, failure: exception.Message, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return new BackupRestoreMatrixResult(new CommandResult(OutcomeKind.InfrastructureFailure,
                "Backup/restore matrix evidence was incomplete or inconsistent.", Details: exception.Message),
                runDirectory, completedManifest, cells.Count);
        }
    }

    private async Task WriteMatrixAsync(
        string runDirectory,
        Guid runId,
        BackupRestoreSourceManifest source,
        IReadOnlyList<BackupRestoreMatrixCell> cells,
        int expectedCells,
        string state,
        CancellationToken cancellationToken)
    {
        var matrix = new BackupRestoreMatrixReport(1, runId, source.Commit, source.Fingerprint,
            state, cells.Count, expectedCells, cells);
        await _atomicFileWriter.WriteReplaceAsync(Path.Combine(runDirectory, "matrix.json"),
            JsonSerializer.SerializeToUtf8Bytes(matrix, JsonOptions), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<BackupRestoreMeasurementReport> ReadAndValidateMeasurementAsync(
        string path,
        BackupRestoreMatrixVariant variant,
        BackupRestoreSourceManifest source,
        string framework,
        int? expectedIterations,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Matrix cell '{variant.Name}' exited successfully without a measurement report.");
        }
        BackupRestoreMeasurementReport? report;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 16_384, FileOptions.Asynchronous | FileOptions.SequentialScan);
            report = await JsonSerializer.DeserializeAsync<BackupRestoreMeasurementReport>(stream, JsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Matrix report '{Path.GetFileName(path)}' is malformed.", exception);
        }
        if (report is null || !report.Passed || report.SourceCommit != source.Commit ||
            report.SourceFingerprint != source.Fingerprint || report.Compound != variant.Compound ||
            report.SourceDurable != variant.SourceDurable || !report.BackupRestoreDurable ||
            report.TempRoot != variant.TempRoot || report.Framework != $".NETCoreApp,Version=v{framework[3..]}" ||
            expectedIterations is { } iterations && report.EffectiveIterations != iterations ||
            !IsSha256(report.CoreAssemblySha256) || !IsSha256(report.TestAssemblySha256) ||
            !IsSha256(report.OperationFingerprint) || string.IsNullOrWhiteSpace(report.Runtime) || report.OperationCount < 1)
        {
            throw new InvalidOperationException($"Matrix report '{Path.GetFileName(path)}' failed its evidence checks.");
        }
        return report;
    }

    private static void ValidateMatchedReports(IReadOnlyList<BackupRestoreMatrixCell> cells)
    {
        var binariesAndRuntime = cells.Select(static cell =>
            $"{cell.Measurement.CoreAssemblySha256}/{cell.Measurement.TestAssemblySha256}/{cell.Measurement.Runtime}")
            .Distinct(StringComparer.Ordinal).ToArray();
        if (binariesAndRuntime.Length != 1)
        {
            throw new InvalidOperationException("Measured assemblies or runtime changed between matrix cells.");
        }
        var workloads = cells.Select(static cell =>
            $"{cell.Measurement.Seed}/{cell.Measurement.Gamma}/{cell.Measurement.EffectiveIterations}/{cell.Measurement.OperationCount}/{cell.Measurement.OperationFingerprint}")
            .Distinct(StringComparer.Ordinal).ToArray();
        if (workloads.Length != 1)
        {
            throw new InvalidOperationException("Matrix workload provenance does not match between cells.");
        }
    }

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(static character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static string NormaliseFramework(string framework)
        => framework.ToLowerInvariant();

    private static bool IsHandled(Exception exception)
        => exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or
            JsonException or FormatException or OverflowException or CryptographicException or NotSupportedException;

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class BackupRestoreMatrixException(OutcomeKind outcome, string message) : Exception(message)
    {
        public OutcomeKind Outcome { get; } = outcome;
    }

    private sealed record BackupRestoreMatrixReport(
        int SchemaVersion,
        Guid RunId,
        string SourceCommit,
        string SourceFingerprint,
        string State,
        int CompletedCells,
        int ExpectedCells,
        IReadOnlyList<BackupRestoreMatrixCell> Cells);

    private sealed record BackupRestoreMatrixCell(
        string Variant,
        int Launch,
        Guid TestRunId,
        string MeasurementPath,
        BackupRestoreMeasurementReport Measurement);

    private sealed record BackupRestoreMeasurementReport
    {
        public bool Passed { get; init; }
        public ulong Seed { get; init; }
        public ulong Gamma { get; init; }
        public int EffectiveIterations { get; init; }
        public int OperationCount { get; init; }
        public string OperationFingerprint { get; init; } = string.Empty;
        public bool Compound { get; init; }
        public bool SourceDurable { get; init; }
        public bool BackupRestoreDurable { get; init; }
        public string? TempRoot { get; init; }
        public string Framework { get; init; } = string.Empty;
        public string Runtime { get; init; } = string.Empty;
        public string? SourceCommit { get; init; }
        public string? SourceFingerprint { get; init; }
        public string CoreAssemblySha256 { get; init; } = string.Empty;
        public string TestAssemblySha256 { get; init; } = string.Empty;
    }
}
