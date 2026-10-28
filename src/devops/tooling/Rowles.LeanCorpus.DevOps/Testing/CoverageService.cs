using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record CoverageRequest(
    CheckoutIdentity Checkout,
    string Suite,
    string Framework,
    string Configuration,
    bool Clean,
    bool IncludePerformance,
    bool GenerateReport,
    string CommandLine);

internal sealed record CoverageServiceResult(
    CommandResult CommandResult,
    string? RunDirectory,
    RunManifest? Manifest,
    int CoverageFileCount);

internal sealed class CoverageService(
    ArtifactPaths paths,
    ArtifactRunStore runStore,
    CleanService cleanService,
    TestRunPlanner testPlanner,
    IManagedTestExecutor testExecutor,
    IProcessRunner processRunner)
{
    private const string ReportGeneratorPackage = "dotnet-reportgenerator-globaltool";
    private const string ReportGeneratorCommand = "reportgenerator";

    public async Task<CoverageServiceResult> RunAsync(CoverageRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Checkout);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Suite);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Framework);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Configuration);

        var filter = request.IncludePerformance ? string.Empty : "Coverage!=Skip";
        var testRequest = await testPlanner.PlanAsync(request.Checkout, new TestRunOptions
        {
            Selection = new TestTargetRequest
            {
                Suites = [request.Suite],
                Framework = request.Framework,
                Configuration = request.Configuration,
                CollectCoverage = true,
                Filter = filter
            },
            Profile = "integration",
            HangTimeout = "off",
            CommandLine = request.CommandLine
        }, cancellationToken).ConfigureAwait(false);

        if (request.Clean)
        {
            var cleanResult = await cleanService.CleanAsync(new CleanRequest
            {
                Checkout = request.Checkout,
                Target = "coverage"
            }, cancellationToken).ConfigureAwait(false);
            if (cleanResult.CommandResult.Outcome != OutcomeKind.Success)
            {
                return new CoverageServiceResult(cleanResult.CommandResult, cleanResult.RunDirectory,
                    cleanResult.Manifest, 0);
            }
        }

        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["suite"] = request.Suite,
            ["framework"] = request.Framework,
            ["configuration"] = request.Configuration,
            ["includePerformance"] = request.IncludePerformance ? "true" : "false",
            ["generateReport"] = request.GenerateReport ? "true" : "false",
            ["filter"] = filter
        };
        var manifest = await runStore.StartAsync("coverage", inputs,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = paths.GetRunDirectory("coverage", manifest.RunId);
        var rawDirectory = Path.Combine(runDirectory, "raw");
        Directory.CreateDirectory(rawDirectory);

        var commandResult = new CommandResult(OutcomeKind.InfrastructureFailure,
            "The managed coverage run did not complete.");
        var coverageFiles = Array.Empty<string>();
        var childExitCodes = new List<int>();
        var artifacts = new List<string> { "raw" };
        var toolVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var childRequest = testRequest with { ParentRunId = manifest.RunId };
            var testResult = await testExecutor.RunAsync(childRequest, cancellationToken).ConfigureAwait(false);
            if (testResult.Manifest is { } testManifest)
            {
                childExitCodes.AddRange(testManifest.ChildExitCodes);
                manifest = await runStore.UpdateRunningAsync(manifest,
                    testManifest.SourceCommit,
                    testManifest.SourceBranch,
                    testManifest.SourceDirty,
                    toolVersions: null,
                    cancellationToken: cancellationToken,
                    additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["testRunId"] = testManifest.RunId.ToString("D")
                    }).ConfigureAwait(false);
            }

            if (testResult.CommandResult.RawChildExitCode is { } testExitCode)
            {
                childExitCodes.Add(testExitCode);
            }

            if (Directory.Exists(testResult.RunDirectory))
            {
                CopyCoverageFiles(testResult.RunDirectory, rawDirectory);
            }
            coverageFiles = FindCoverageFiles(rawDirectory).ToArray();

            commandResult = testResult.CommandResult;
            if (commandResult.Outcome == OutcomeKind.Success && testResult.Manifest is null)
            {
                commandResult = new CommandResult(OutcomeKind.InfrastructureFailure,
                    "The managed test pipeline returned success without a terminal test run manifest.");
            }
            else if (commandResult.Outcome == OutcomeKind.Success && coverageFiles.Length == 0)
            {
                commandResult = new CommandResult(OutcomeKind.InfrastructureFailure,
                    "The managed test run completed without producing Cobertura coverage reports.",
                    Details: testResult.RunDirectory);
            }
            else if (commandResult.Outcome == OutcomeKind.Success && request.GenerateReport && coverageFiles.Length > 0)
            {
                var version = ReadPinnedToolVersion(request.Checkout.RepositoryRoot);
                toolVersions["ReportGenerator"] = version;
                manifest = await runStore.UpdateRunningAsync(manifest,
                    manifest.SourceCommit,
                    manifest.SourceBranch,
                    manifest.SourceDirty,
                    toolVersions,
                    cancellationToken,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["coverageFileCount"] = coverageFiles.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }).ConfigureAwait(false);

                var reportResult = await GenerateReportAsync(request.Checkout.RepositoryRoot,
                    coverageFiles, Path.Combine(runDirectory, "html"), runDirectory, cancellationToken).ConfigureAwait(false);
                if (reportResult.RawExitCode is { } reportExitCode)
                {
                    childExitCodes.Add(reportExitCode);
                }
                artifacts.Add("logs/reportgenerator.stdout.log");
                artifacts.Add("logs/reportgenerator.stderr.log");

                if (reportResult.Outcome != OutcomeKind.Success)
                {
                    commandResult = new CommandResult(reportResult.Outcome,
                        "ReportGenerator failed while building the HTML coverage report.",
                        reportResult.RawExitCode,
                        reportResult.Failure);
                }
                else if (!File.Exists(Path.Combine(runDirectory, "html", "index.html")))
                {
                    commandResult = new CommandResult(OutcomeKind.InfrastructureFailure,
                        "ReportGenerator exited successfully but did not create html/index.html.");
                }
                else
                {
                    artifacts.Add("html");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            commandResult = new CommandResult(OutcomeKind.Cancelled, "Coverage was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception)
        {
            commandResult = new CommandResult(OutcomeKind.InfrastructureFailure,
                "Could not complete the managed coverage run.", Details: exception.Message);
        }

        var terminalState = commandResult.Outcome switch
        {
            OutcomeKind.Success => RunState.Succeeded,
            OutcomeKind.Cancelled => RunState.Cancelled,
            OutcomeKind.Timeout => RunState.TimedOut,
            _ => RunState.Failed
        };
        manifest = await runStore.FinishAsync(manifest, terminalState,
            childExitCodes: childExitCodes,
            artifacts: artifacts,
            failure: commandResult.Outcome == OutcomeKind.Success ? null : commandResult.Message,
            cancellationToken: CancellationToken.None).ConfigureAwait(false);

        return new CoverageServiceResult(commandResult, runDirectory, manifest, coverageFiles.Length);
    }

    private async Task<ProcessResult> GenerateReportAsync(
        string repositoryRoot,
        IReadOnlyList<string> coverageFiles,
        string outputDirectory,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        var logDirectory = Path.Combine(runDirectory, "logs");
        Directory.CreateDirectory(logDirectory);
        var reportPaths = string.Join(';', coverageFiles);
        var arguments = new List<string>
        {
            "tool", "run", ReportGeneratorCommand, "--",
            $"-reports:{reportPaths}",
            $"-targetdir:{outputDirectory}",
            "-reporttypes:Html",
            "-title:LeanCorpus Coverage",
            $"-sourcedirs:{repositoryRoot}",
            "-assemblyfilters:+Rowles.*;+leancorpus-cli",
            "-filefilters:-**/obj/**/*.cs"
        };
        return await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = "dotnet",
            Arguments = arguments,
            WorkingDirectory = repositoryRoot,
            StandardOutputLogPath = Path.Combine(logDirectory, "reportgenerator.stdout.log"),
            StandardErrorLogPath = Path.Combine(logDirectory, "reportgenerator.stderr.log")
        }, cancellationToken).ConfigureAwait(false);
    }

    private static void CopyCoverageFiles(string sourceRoot, string rawDirectory)
    {
        var canonicalSource = Path.GetFullPath(sourceRoot);
        foreach (var sourcePath in EnumerateCoverageFiles(canonicalSource))
        {
            var relativePath = Path.GetRelativePath(canonicalSource, sourcePath);
            if (Path.IsPathRooted(relativePath) || relativePath == ".." ||
                relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Coverage report '{sourcePath}' escaped its managed test run directory.");
            }

            var destinationPath = Path.Combine(rawDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(sourcePath, destinationPath, overwrite: false);
        }
    }

    private static IEnumerable<string> FindCoverageFiles(string root)
        => EnumerateCoverageFiles(root).Order(StringComparer.Ordinal).ToArray();

    private static IEnumerable<string> EnumerateCoverageFiles(string root)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }

                var name = Path.GetFileName(entry);
                if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains(".coverage.cobertura.", StringComparison.OrdinalIgnoreCase))
                {
                    yield return Path.GetFullPath(entry);
                }
            }
        }
    }

    private static string ReadPinnedToolVersion(string repositoryRoot)
    {
        var manifestPath = Path.Combine(repositoryRoot, ".config", "dotnet-tools.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidDataException("The repository .NET tool manifest is missing; ReportGenerator must be pinned before generating a coverage report.");
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The repository .NET tool manifest contains invalid JSON.", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tools", out var tools) ||
                tools.ValueKind != JsonValueKind.Object ||
                !tools.TryGetProperty(ReportGeneratorPackage, out var tool) ||
                tool.ValueKind != JsonValueKind.Object ||
                !tool.TryGetProperty("version", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.String ||
                !tool.TryGetProperty("commands", out var commandElements) ||
                commandElements.ValueKind != JsonValueKind.Array ||
                !commandElements.EnumerateArray().Any(command =>
                    command.ValueKind == JsonValueKind.String &&
                    string.Equals(command.GetString(), ReportGeneratorCommand, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("The repository tool manifest does not pin ReportGenerator.");
            }

            return versionElement.GetString() is { Length: > 0 } version
                ? version
                : throw new InvalidDataException("The pinned ReportGenerator version is empty.");
        }
    }
}
