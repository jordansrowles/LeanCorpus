using System.Diagnostics;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Benchmarks;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Commands;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Data;

internal sealed class DataForgeQualificationStepRunner(
    ArtifactPaths artifactPaths,
    IProcessRunner processRunner,
    IDotNetSdkResolver sdkResolver,
    IRepositoryProvenanceReader provenanceReader,
    IReadOnlyDictionary<string, TestSuiteConfiguration> testSuites,
    Action<string>? progress = null,
    Action<string>? errorOutput = null,
    string dotnetExecutable = "dotnet") : IDataForgeQualificationStepRunner
{
    public Task<DataForgeQualificationStepResult> ExecuteAsync(
        CheckoutIdentity checkout,
        DataForgeQualificationStep step,
        string qualificationDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentNullException.ThrowIfNull(step);
        ArgumentException.ThrowIfNullOrWhiteSpace(qualificationDirectory);
        return step.Kind switch
        {
            DataForgeQualificationStepKind.Build => RunBuildAsync(checkout, cancellationToken),
            DataForgeQualificationStepKind.ProfileVerification => RunDataForgeAsync(checkout, step, cancellationToken),
            DataForgeQualificationStepKind.ReferenceVerification => RunDataForgeAsync(checkout, step, cancellationToken),
            DataForgeQualificationStepKind.Test => RunTestAsync(checkout, step, qualificationDirectory, cancellationToken),
            DataForgeQualificationStepKind.Benchmark => RunBenchmarkAsync(checkout, step, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(step), step.Kind, "Unsupported DataForge qualification step.")
        };
    }

    private async Task<DataForgeQualificationStepResult> RunBuildAsync(CheckoutIdentity checkout, CancellationToken cancellationToken)
    {
        var result = await new BuildService(artifactPaths, new ArtifactRunStore(artifactPaths), processRunner, sdkResolver,
            provenanceReader, dotnetExecutable).BuildAsync(new BuildRequest { Checkout = checkout }, cancellationToken).ConfigureAwait(false);
        return FromCommandResult(result.CommandResult, result.RunId, "build", result.RunDirectory);
    }

    private async Task<DataForgeQualificationStepResult> RunDataForgeAsync(
        CheckoutIdentity checkout,
        DataForgeQualificationStep step,
        CancellationToken cancellationToken)
    {
        var result = await new DataForgeCommandService(artifactPaths, new ArtifactRunStore(artifactPaths, runCategory: "dataforge"),
            processRunner, sdkResolver, provenanceReader, progress, errorOutput, dotnetExecutable)
            .RunAsync(new DataForgeCommandRequest(checkout, step.Arguments), cancellationToken).ConfigureAwait(false);
        return FromCommandResult(result.CommandResult, result.RunId, "dataforge", result.RunDirectory);
    }

    private async Task<DataForgeQualificationStepResult> RunBenchmarkAsync(
        CheckoutIdentity checkout,
        DataForgeQualificationStep step,
        CancellationToken cancellationToken)
    {
        var benchmarkOptions = new BenchmarkCommandOptions(
            step.Suite ?? throw new InvalidOperationException("A qualification benchmark suite is required."),
            step.Strategy ?? throw new InvalidOperationException("A qualification benchmark strategy is required."),
            step.Framework ?? "net11.0",
            DocumentCount: 0,
            step.Dataset,
            step.Dataset.Equals("wikipedia", StringComparison.OrdinalIgnoreCase) ? GetReferencePath(step.Arguments) : null,
            Area: null,
            Group: null,
            SourceCommit: null,
            SourceRef: null,
            SourceManifest: null,
            Controlled: false,
            CorpusOnly: false,
            GcDump: false,
            PrepareData: false,
            BookCount: 200,
            Dry: false,
            List: false,
            Arguments: step.BenchmarkArguments ?? [],
            RepositoryPath: checkout.RepositoryRoot);
        var result = await new BenchmarkCommandService(artifactPaths, new ArtifactRunStore(artifactPaths, runCategory: "benchmark"),
            processRunner, sdkResolver, provenanceReader, progress, errorOutput, dotnetExecutable)
            .RunAsync(new BenchmarkCommandRequest(checkout, benchmarkOptions), cancellationToken).ConfigureAwait(false);
        return FromCommandResult(result.CommandResult, result.RunId, "benchmark", result.RunDirectory);
    }

    private async Task<DataForgeQualificationStepResult> RunTestAsync(
        CheckoutIdentity checkout,
        DataForgeQualificationStep step,
        string qualificationDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(step.Suite) || !testSuites.TryGetValue(step.Suite, out var suite))
            return new DataForgeQualificationStepResult("Failed", null, 0, Error: $"Unknown qualification test suite '{step.Suite}'.");
        if (!suite.DataForgeQualification)
            return new DataForgeQualificationStepResult("Failed", null, 0,
                Error: $"Configured test suite '{step.Suite}' is not eligible for DataForge qualification.");
        if (!suite.Runner.Equals("Mtp", StringComparison.Ordinal))
            return new DataForgeQualificationStepResult("Failed", null, 0,
                Error: $"Qualification test suite '{step.Suite}' must use the Mtp runner.");
        if (string.IsNullOrWhiteSpace(step.Framework) || !suite.Frameworks.Contains(step.Framework, StringComparer.OrdinalIgnoreCase))
            return new DataForgeQualificationStepResult("Failed", null, 0,
                Error: $"Test suite '{step.Suite}' does not support qualification framework '{step.Framework}'.");

        var relativeProjectPath = suite.Project;
        var projectPath = Path.GetFullPath(Path.Combine(checkout.RepositoryRoot, relativeProjectPath));
        if (!IsWithin(checkout.RepositoryRoot, projectPath) || !File.Exists(projectPath))
            return new DataForgeQualificationStepResult("Failed", null, 0, Error: $"Qualification test project was not found inside the selected checkout: '{projectPath}'.");

        var command = new[] { "test", "-Suite", step.Suite, "-Framework", step.Framework, "-CI" };
        var runStore = new ArtifactRunStore(artifactPaths, runCategory: "test");
        var manifest = await runStore.StartAsync("test",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["suite"] = step.Suite,
                ["project"] = relativeProjectPath,
                ["framework"] = step.Framework,
                ["configuration"] = "Release",
                ["selectionSource"] = "DataForge qualification"
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = artifactPaths.GetRunDirectory("test", manifest.RunId);
        var targetDirectory = Path.Combine(runDirectory, "targets", $"{step.Suite}-{step.Framework}");
        Directory.CreateDirectory(targetDirectory);

        try
        {
            var git = await provenanceReader.ReadAsync(checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (git.Outcome != OutcomeKind.Success)
                return await FinishTestEarlyAsync(runStore, manifest, "Failed", null, runDirectory, git.Failure).ConfigureAwait(false);
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty, cancellationToken: cancellationToken).ConfigureAwait(false);

            var sdk = await sdkResolver.ResolveAsync(checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (sdk.Outcome != OutcomeKind.Success)
                return await FinishTestEarlyAsync(runStore, manifest, "Failed", null, runDirectory, sdk.Failure).ConfigureAwait(false);
            var sdkRoot = ResolveSdkRoot(sdk.SdkDirectory);
            if (sdkRoot is null)
                return await FinishTestEarlyAsync(runStore, manifest, "Failed", null, runDirectory,
                    "The selected .NET SDK did not report an SDK directory from which the runtime root could be resolved.").ConfigureAwait(false);
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["dotnet-sdk-requested"] = sdk.RequestedVersion ?? string.Empty,
                    ["dotnet-sdk-selected"] = sdk.SelectedVersion ?? string.Empty,
                    ["dotnet-sdk-roll-forward"] = sdk.RollForward ?? "default"
                }, cancellationToken).ConfigureAwait(false);

            var targetPath = await ResolveTargetPathAsync(checkout, projectPath, step.Framework, targetDirectory, cancellationToken).ConfigureAwait(false);
            if (targetPath.Outcome != OutcomeKind.Success || string.IsNullOrWhiteSpace(targetPath.TargetPath))
                return await FinishTestEarlyAsync(runStore, manifest, "Failed", targetPath.ExitCode, runDirectory,
                    targetPath.Failure ?? "MSBuild did not return a target assembly for the qualification test.").ConfigureAwait(false);

            var executablePath = Path.Combine(Path.GetDirectoryName(targetPath.TargetPath)!,
                Path.GetFileNameWithoutExtension(targetPath.TargetPath) + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
            if (!File.Exists(executablePath))
                return await FinishTestEarlyAsync(runStore, manifest, "Failed", null, runDirectory,
                    $"The built MTP test executable was not found at '{executablePath}'.").ConfigureAwait(false);

            var resultPath = Path.Combine(targetDirectory, "results.trx");
            var arguments = new[]
            {
                "--results-directory", targetDirectory,
                "--report-xunit-trx",
                "--report-xunit-trx-filename", "results.trx",
                "--xunit-config-filename", Path.Combine(checkout.RepositoryRoot, ".config", "devops", "testconfig.json"),
                "--parallel", "collections",
                "--report-gh"
            };
            manifest = await runStore.UpdateRunningAsync(manifest, git.Commit, git.Branch, git.Dirty,
                cancellationToken: cancellationToken,
                additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["effectiveExecutable"] = Path.GetRelativePath(checkout.RepositoryRoot, executablePath).Replace('\\', '/'),
                    ["effectiveArguments"] = JsonSerializer.Serialize(arguments),
                    ["trxPath"] = Path.GetRelativePath(checkout.RepositoryRoot, resultPath).Replace('\\', '/')
                }).ConfigureAwait(false);

            var timer = Stopwatch.StartNew();
            var child = await processRunner.RunAsync(new ProcessRequest
            {
                ExecutablePath = executablePath,
                Arguments = arguments,
                WorkingDirectory = checkout.RepositoryRoot,
                StandardOutputLogPath = Path.Combine(targetDirectory, "stdout.log"),
                StandardErrorLogPath = Path.Combine(targetDirectory, "stderr.log"),
                EnvironmentVariables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DOTNET_ROOT"] = sdkRoot,
                    [$"DOTNET_ROOT_{System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant()}"] = sdkRoot
                },
                ProgressInterval = TimeSpan.FromSeconds(60),
                Progress = value => progress?.Invoke($"  {step.Name} is still running ({value.Elapsed:hh\\:mm\\:ss}; {value.StandardOutputLineCount} stdout and {value.StandardErrorLineCount} stderr lines).")
            }, cancellationToken).ConfigureAwait(false);
            timer.Stop();

            var trx = child.Outcome == OutcomeKind.Success
                ? DataForgeQualificationEvidence.ReadMtpTestTrx(resultPath)
                : new DataForgeMtpTestResult(false, null, child.Failure ?? $"MTP test process exited with code {child.RawExitCode}.");
            var passed = child.Outcome == OutcomeKind.Success && trx.Passed;
            var state = child.Outcome switch
            {
                OutcomeKind.Cancelled => RunState.Cancelled,
                OutcomeKind.Timeout => RunState.TimedOut,
                _ when passed => RunState.Succeeded,
                _ => RunState.Failed
            };
            var artifacts = new[]
            {
                Path.GetRelativePath(runDirectory, Path.Combine(targetDirectory, "stdout.log")),
                Path.GetRelativePath(runDirectory, Path.Combine(targetDirectory, "stderr.log")),
                Path.GetRelativePath(runDirectory, resultPath)
            };
            var finished = await runStore.FinishAsync(manifest, state, child.RawExitCode, artifacts,
                passed ? null : trx.Failure ?? child.Failure, CancellationToken.None).ConfigureAwait(false);
            return new DataForgeQualificationStepResult(passed ? "Passed" : state == RunState.Cancelled ? "Cancelled" : "Failed",
                child.RawExitCode, timer.Elapsed.TotalSeconds, finished.RunId, "test",
                Path.GetRelativePath(checkout.RepositoryRoot, runDirectory).Replace('\\', '/'), runDirectory,
                passed ? null : trx.Failure ?? child.Failure);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            return await FinishTestEarlyAsync(runStore, manifest, "Cancelled", null, runDirectory, exception.Message).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return await FinishTestEarlyAsync(runStore, manifest, "Failed", null, runDirectory, exception.Message).ConfigureAwait(false);
        }
    }

    private async Task<(OutcomeKind Outcome, string? TargetPath, int? ExitCode, string? Failure)> ResolveTargetPathAsync(
        CheckoutIdentity checkout,
        string projectPath,
        string framework,
        string logDirectory,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = dotnetExecutable,
            Arguments = ["msbuild", projectPath, "--nologo", "--disable-build-servers", "-m:1", "-getProperty:TargetPath",
                "-property:Configuration=Release", $"-property:TargetFramework={framework}", "-property:UseSharedCompilation=false"],
            WorkingDirectory = checkout.RepositoryRoot,
            StandardOutputLogPath = Path.Combine(logDirectory, "target-path.stdout.log"),
            StandardErrorLogPath = Path.Combine(logDirectory, "target-path.stderr.log"),
            Timeout = TimeSpan.FromMinutes(2)
        }, cancellationToken).ConfigureAwait(false);
        if (result.Outcome != OutcomeKind.Success)
            return (result.Outcome, null, result.RawExitCode, result.Failure ?? "dotnet msbuild failed while resolving the MTP executable.");

        var targetPath = result.StandardOutputTail.Select(line => line.Trim())
            .LastOrDefault(line => Path.IsPathRooted(line) && Path.GetExtension(line).Equals(".dll", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(targetPath))
            return (OutcomeKind.InfrastructureFailure, null, result.RawExitCode, "dotnet msbuild did not report a rooted TargetPath ending in .dll.");
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (!IsWithin(checkout.RepositoryRoot, fullTargetPath))
            return (OutcomeKind.ValidationFailure, null, result.RawExitCode, "The MTP TargetPath resolves outside the selected checkout.");
        return (OutcomeKind.Success, fullTargetPath, result.RawExitCode, null);
    }

    private async Task<DataForgeQualificationStepResult> FinishTestEarlyAsync(
        ArtifactRunStore runStore,
        RunManifest manifest,
        string status,
        int? exitCode,
        string runDirectory,
        string? failure)
    {
        var state = status == "Cancelled" ? RunState.Cancelled : RunState.Failed;
        var terminal = await runStore.FinishAsync(manifest, state, exitCode, failure: failure,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);
        return new DataForgeQualificationStepResult(status, exitCode, 0, terminal.RunId, "test",
            Path.GetRelativePath(artifactPaths.RepositoryRoot, runDirectory).Replace('\\', '/'), runDirectory, failure);
    }

    private DataForgeQualificationStepResult FromCommandResult(
        CommandResult commandResult,
        Guid? runId,
        string runKind,
        string? runDirectory)
    {
        var status = commandResult.Outcome switch
        {
            OutcomeKind.Success => "Passed",
            OutcomeKind.Cancelled => "Cancelled",
            _ => "Failed"
        };
        var artifactPath = runDirectory is null ? null : Path.GetRelativePath(artifactPaths.RepositoryRoot, runDirectory).Replace('\\', '/');
        return new DataForgeQualificationStepResult(status, commandResult.ExitCode, 0, runId, runKind, artifactPath, runDirectory,
            commandResult.Outcome == OutcomeKind.Success ? null : commandResult.Details ?? commandResult.Message);
    }

    private static string GetReferencePath(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index].Equals("-ReferencePath", StringComparison.OrdinalIgnoreCase))
                return arguments[index + 1];
        }
        throw new InvalidOperationException("The Wikipedia qualification benchmark is missing its reference path.");
    }

    private static bool IsWithin(string root, string candidate)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullCandidate = Path.GetFullPath(candidate);
        var prefix = fullRoot + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullCandidate.StartsWith(prefix, comparison);
    }

    private static string? ResolveSdkRoot(string? sdkDirectory)
    {
        if (string.IsNullOrWhiteSpace(sdkDirectory))
            return null;

        var sdkPath = new DirectoryInfo(Path.GetFullPath(sdkDirectory));
        var sdkRootDirectory = sdkPath.Parent;
        if (!string.Equals(sdkRootDirectory?.Name, "sdk", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return null;

        return sdkRootDirectory?.Parent?.FullName;
    }
}
