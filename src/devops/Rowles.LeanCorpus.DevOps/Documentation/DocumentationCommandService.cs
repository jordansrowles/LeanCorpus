using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Artifacts;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Configuration;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;
using Rowles.LeanCorpus.DevOps.Testing;

namespace Rowles.LeanCorpus.DevOps.Documentation;

internal sealed record DocumentationCommandRequest
{
    public required CheckoutIdentity Checkout { get; init; }
    public required string Action { get; init; }
    public bool SkipBenchmarks { get; init; }
    public bool SkipCoverage { get; init; }
    public string Framework { get; init; } = "net10.0";
    public bool NoRestore { get; init; }
    public string CommandLine { get; init; } = "./devops docs build";
}

internal sealed record DocumentationCommandResult(CommandResult CommandResult, string RunDirectory, RunManifest Manifest,
    DocumentationStaticValidationResult? StaticValidation = null);

internal sealed class DocumentationCommandService(
    ArtifactPaths artifactPaths,
    ArtifactRunStore runStore,
    IProcessRunner processRunner,
    IRepositoryProvenanceReader provenanceReader,
    DocumentationSiteMetadataWriter metadataWriter,
    TestRunPlanner testPlanner,
    IManagedTestExecutor testExecutor,
    DevOpsConfiguration configuration,
    Action<string>? progress = null)
{
    private readonly DocumentationContentStager _stager = new();
    private readonly RepositoryDocumentationImporter _importer = new();
    private readonly DocumentationSectionCatalogWriter _catalogWriter = new();
    private readonly AdrIndexGenerator _adrGenerator = new();
    private readonly BenchmarkDocumentationGenerator _benchmarkGenerator = new();
    private readonly DocumentationCoverageReportPreparer _coveragePreparer = new(processRunner);
    private readonly DocumentationStaticValidator _staticValidator = new();

    public async Task<DocumentationCommandResult> RunAsync(DocumentationCommandRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Checkout);
        if (!DocumentationActionCatalog.TryParse(request.Action, out var action))
        {
            throw new InvalidOperationException($"Unknown docs action '{request.Action}'. Expected: {DocumentationActionCatalog.ExpectedNames}.");
        }
        var actionName = DocumentationActionCatalog.GetName(action);
        if (action == DocumentationAction.Test && (request.SkipBenchmarks || request.SkipCoverage))
        {
            throw new InvalidOperationException("Docs test does not accept preparation skip options.");
        }
        if (action != DocumentationAction.Test && (request.NoRestore || !request.Framework.Equals("net10.0", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Docs framework and no-restore options are valid only with docs test.");
        }

        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["action"] = actionName,
            ["skipBenchmarks"] = request.SkipBenchmarks ? "true" : "false",
            ["skipCoverage"] = request.SkipCoverage ? "true" : "false",
            ["framework"] = request.Framework,
            ["noRestore"] = request.NoRestore ? "true" : "false",
            ["commandLine"] = request.CommandLine
        };
        var manifest = await runStore.StartAsync("docs", inputs, cancellationToken: cancellationToken).ConfigureAwait(false);
        var runDirectory = artifactPaths.GetRunDirectory("docs", manifest.RunId);
        Directory.CreateDirectory(runDirectory);
        var childExitCodes = new List<int>();
        var artifacts = new List<string>();
        DocumentationStaticValidationResult? staticValidation = null;
        CommandResult commandResult;

        try
        {
            await using var lease = await ExecutionLease.AcquireExclusiveAsync(artifactPaths.RepositoryRoot,
                TimeSpan.FromMinutes(10), cancellationToken).ConfigureAwait(false);
            var provenance = await provenanceReader.ReadAsync(request.Checkout, runDirectory, cancellationToken).ConfigureAwait(false);
            if (provenance.Outcome != OutcomeKind.Success || string.IsNullOrWhiteSpace(provenance.Commit))
            {
                throw new DocumentationOperationException(OutcomeKind.InfrastructureFailure,
                    provenance.Failure ?? "Git source provenance could not be established.");
            }
            manifest = await runStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            DocumentationVersionResult? versions = null;
            switch (action)
            {
                case DocumentationAction.Prepare:
                    versions = await PrepareInputsAsync(request, provenance, runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
                    artifacts.AddRange(["content", "generated"]);
                    break;
                case DocumentationAction.Metadata:
                    versions = await PrepareInputsAsync(request, provenance, runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
                    await RegenerateMetadataAsync(runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
                    artifacts.AddRange(["content", "generated", "api", "diagnostics"]);
                    break;
                case DocumentationAction.Build:
                    versions = await PrepareInputsAsync(request, provenance, runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
                    var metadataWarnings = await RegenerateMetadataAsync(runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
                    var buildWarnings = await BuildSiteAsync(runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
                    manifest = await runStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
                        toolVersions: versions.ToolVersions,
                        cancellationToken: cancellationToken,
                        additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["docfxMetadataWarningCount"] = metadataWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["docfxBuildWarningCount"] = buildWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        }).ConfigureAwait(false);
                    artifacts.AddRange(["content", "generated", "api", "site", "coverage", "diagnostics"]);
                    break;
                case DocumentationAction.Test:
                    versions = await metadataWriter.EvaluateVersionsAsync(artifactPaths.RepositoryRoot, runDirectory, cancellationToken).ConfigureAwait(false);
                    staticValidation = _staticValidator.Validate(artifactPaths.RepositoryRoot,
                        DocsPath("content"), DocsPath("api"), DocsPath("site"), versions.ProductVersion, versions.DevOpsVersion);
                    WriteProgress($"Static documentation checks passed: {staticValidation.MarkdownPages} Markdown pages, {staticValidation.TocFiles} TOCs, {staticValidation.HtmlPages} HTML pages, {staticValidation.LocalReferences} local references and {staticValidation.Fragments} fragments.");
                    var testRequest = await testPlanner.PlanAsync(request.Checkout, new TestRunOptions
                    {
                        Selection = new TestTargetRequest { Suites = ["docs-site"], Framework = request.Framework, Configuration = "Release" },
                        Profile = "integration",
                        NoRestore = request.NoRestore,
                        HangTimeout = "off",
                        CommandLine = request.CommandLine
                    }, cancellationToken).ConfigureAwait(false);
                    var testResult = await testExecutor.RunAsync(testRequest with { ParentRunId = manifest.RunId }, cancellationToken).ConfigureAwait(false);
                    if (testResult.Manifest is { } testManifest)
                    {
                        childExitCodes.AddRange(testManifest.ChildExitCodes);
                        manifest = await runStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
                            toolVersions: versions.ToolVersions,
                            cancellationToken: cancellationToken,
                            additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                            {
                                ["docsSiteTestRunId"] = testManifest.RunId.ToString("D")
                            }).ConfigureAwait(false);
                    }
                    if (testResult.CommandResult.RawChildExitCode is { } testExitCode) childExitCodes.Add(testExitCode);
                    if (testResult.CommandResult.Outcome != OutcomeKind.Success)
                    {
                        throw new DocumentationOperationException(testResult.CommandResult.Outcome,
                            testResult.CommandResult.Message, testResult.CommandResult.RawChildExitCode,
                            JoinDetails(testResult.RunDirectory, testResult.CommandResult.Details));
                    }
                    artifacts.AddRange(["content", "api", "site"]);
                    break;
                case DocumentationAction.Serve:
                    versions = await PrepareInputsAsync(request, provenance, runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
                    var serveMetadataWarnings = await RegenerateMetadataAsync(runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
                    var serveBuildWarnings = await BuildSiteAsync(runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
                    manifest = await runStore.UpdateRunningAsync(manifest, provenance.Commit, provenance.Branch, provenance.Dirty,
                        toolVersions: versions.ToolVersions,
                        cancellationToken: cancellationToken,
                        additionalInputs: new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["docfxMetadataWarningCount"] = serveMetadataWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            ["docfxBuildWarningCount"] = serveBuildWarnings.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        }).ConfigureAwait(false);
                    artifacts.AddRange(["content", "generated", "api", "site", "coverage", "diagnostics"]);
                    break;
            }

            await WriteContentManifestAsync(runDirectory, manifest.RunId, provenance.Commit, cancellationToken).ConfigureAwait(false);
            artifacts.Add("content-manifest.json");
            commandResult = new CommandResult(OutcomeKind.Success, $"Docs {actionName} completed. Evidence: {runDirectory}");
            if (action == DocumentationAction.Serve)
            {
                WriteProgress("Serving the generated docs site at http://0.0.0.0:8080");
                var result = await RunProcessAsync("docfx-serve", "dotnet",
                    ["tool", "run", "docfx", "--", "serve", DocsPath("site"), "--hostname", "0.0.0.0", "-p", "8080"],
                    artifactPaths.RepositoryRoot, runDirectory, childExitCodes, cancellationToken,
                    timeout: null).ConfigureAwait(false);
                commandResult = result.Outcome == OutcomeKind.Success
                    ? new CommandResult(OutcomeKind.Success, $"Docs serve stopped. Evidence: {runDirectory}", result.RawExitCode)
                    : new CommandResult(result.Outcome, "Docs serve did not complete successfully.", result.RawExitCode, JoinDetails(runDirectory, result.Failure));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            commandResult = new CommandResult(OutcomeKind.Cancelled, $"Docs {actionName} was cancelled.", Details: runDirectory);
        }
        catch (DocumentationOperationException exception)
        {
            commandResult = new CommandResult(exception.Outcome, exception.Message, exception.RawExitCode, JoinDetails(runDirectory, exception.Details));
        }
        catch (InvalidOperationException exception)
        {
            commandResult = new CommandResult(OutcomeKind.ValidationFailure, exception.Message, Details: runDirectory);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or JsonException or TimeoutException)
        {
            commandResult = new CommandResult(OutcomeKind.InfrastructureFailure,
                $"Could not complete managed docs {actionName}.", Details: JoinDetails(runDirectory, exception.Message));
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
        return new DocumentationCommandResult(commandResult, runDirectory, manifest, staticValidation);
    }

    private async Task<DocumentationVersionResult> PrepareInputsAsync(DocumentationCommandRequest request,
        GitProvenance provenance, string runDirectory, List<int> childExitCodes, CancellationToken cancellationToken)
    {
        var repository = artifactPaths.RepositoryRoot;
        var sourceContent = Path.Combine(repository, "docs", "content");
        var siteConfig = Path.Combine(repository, "docs", "site-config");
        var manifestPath = Path.Combine(siteConfig, "imports.json");
        var configPath = Path.Combine(siteConfig, "docfx.json");
        RequireFile(sourceContent, "Canonical documentation directory is missing.", directory: true);
        RequireFile(manifestPath, "Documentation import manifest is missing.");
        RequireFile(configPath, "DocFX configuration is missing.");
        using var importsDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(manifestPath, cancellationToken).ConfigureAwait(false));
        using var configDocument = JsonDocument.Parse(await File.ReadAllBytesAsync(configPath, cancellationToken).ConfigureAwait(false));

        var content = DocsPath("content");
        _stager.CopyCanonicalContent(sourceContent, content);
        await PrepareDiagramAssetsAsync(siteConfig, content, runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
        _importer.Import(repository, content, sourceContent, manifestPath, provenance.Commit!);

        _stager.CopyChangelog(repository, content);
        _catalogWriter.Generate(Path.Combine(content, "changelog"), "Changelog",
            "Versioned LeanCorpus, Rowles.Text, Community Server and managed DevOps release notes.", separateServerChangelog: true);

        var adrSource = Path.Combine(sourceContent, "articles", "ADRs");
        var adrStaging = Path.Combine(content, "articles", "ADRs");
        var adrCount = _adrGenerator.Generate(adrSource, Path.Combine(adrStaging, "index.md"), adrStaging);
        WriteProgress($"Validated and staged {adrCount} Core and Server ADR records.");

        var benchmarkDirectory = Path.Combine(content, "benchmarks");
        var qualificationAudit = Path.Combine(sourceContent, "benchmarks", "qualification-audit.md");
        var diagramsDirectory = Path.Combine(siteConfig, "diagrams");
        var chartBundle = Path.Combine(diagramsDirectory, "node_modules", "chart.js", "dist", "chart.umd.js");
        var benchmarkScript = Path.Combine(siteConfig, "benchmark-charts.js");
        var benchmarkResult = _benchmarkGenerator.Generate(Path.Combine(repository, "artifacts", "benchmark", "runs"),
            benchmarkDirectory, chartBundle, benchmarkScript, configuration, request.SkipBenchmarks, qualificationAudit);
        foreach (var warning in benchmarkResult.Warnings) WriteProgress($"Benchmark docs: {warning}");
        _stager.SetContributionMetadata(repository, content, manifestPath, provenance.Commit!);
        _catalogWriter.GenerateSectionTocs(content);

        if (!request.SkipCoverage)
        {
            var coverageResult = await _coveragePreparer.PrepareAsync(repository, provenance.Commit, provenance.Dirty,
                DocsPath("coverage"), runDirectory, cancellationToken).ConfigureAwait(false);
            WriteProgress(coverageResult.SourceRunId is null
                ? "Current-commit coverage evidence is unavailable; documentation will be built without a coverage report."
                : $"Prepared documentation coverage from run {coverageResult.SourceRunId} ({coverageResult.CoverageFileCount} file(s)).");
        }

        var versions = await metadataWriter.WriteAsync(request.Checkout, DocsPath("generated"), runDirectory, provenance, cancellationToken).ConfigureAwait(false);
        return versions;
    }

    private async Task PrepareDiagramAssetsAsync(string siteConfig, string contentDirectory, string runDirectory,
        List<int> childExitCodes, CancellationToken cancellationToken)
    {
        var diagramSource = Path.Combine(siteConfig, "diagrams");
        var script = Path.Combine(diagramSource, "build.mjs");
        var nodeModules = Path.Combine(diagramSource, "node_modules");
        var output = Path.Combine(contentDirectory, "assets", "diagrams");
        RequireFile(script, "Pinned documentation diagram builder is missing.");
        Directory.CreateDirectory(output);
        var bytefield = Path.Combine(nodeModules, "bytefield-svg");
        var chart = Path.Combine(nodeModules, "chart.js", "dist", "chart.umd.js");
        if (!Directory.Exists(bytefield) || !File.Exists(chart))
        {
            var npm = await RunProcessAsync("diagram-npm-ci", "npm", ["ci", "--prefix", diagramSource],
                artifactPaths.RepositoryRoot, runDirectory, childExitCodes, cancellationToken,
                timeout: TimeSpan.FromMinutes(10)).ConfigureAwait(false);
            EnsureSuccess(npm, "Pinned diagram dependency restore failed.");
        }

        var node = await RunProcessAsync("diagram-node-version", "node", ["--version"], diagramSource,
            runDirectory, childExitCodes, cancellationToken, timeout: TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        EnsureSuccess(node, "Node.js is required to prepare documentation diagram assets.");
        if (!node.StandardOutputTail.Any(static line => Version.TryParse(line.Trim().TrimStart('v'), out var version) && version.Major >= 20))
        {
            throw new DocumentationOperationException(OutcomeKind.ValidationFailure, "Node.js 20 or newer is required to prepare documentation diagram assets.");
        }

        var diagramBuild = await RunProcessAsync("diagram-build", "node", [script], diagramSource,
            runDirectory, childExitCodes, cancellationToken,
            new Dictionary<string, string?> { ["LEAN_CORPUS_DOCS_DIAGRAM_OUTPUT"] = output }, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
        EnsureSuccess(diagramBuild, "Documentation diagram generation failed.");
    }

    private async Task<int> RegenerateMetadataAsync(string runDirectory, List<int> childExitCodes, CancellationToken cancellationToken)
    {
        var api = DocsPath("api");
        ClearApiMetadata(api);
        var log = Path.Combine(DocsPath("diagnostics"), "docfx-metadata.jsonl");
        var result = await RunDocFxAsync("metadata", log, runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "DocFX metadata generation failed.");
        RemoveExternalInheritedMembers(api);
        return CountDocFxWarnings(log);
    }

    private async Task<int> BuildSiteAsync(string runDirectory, List<int> childExitCodes, CancellationToken cancellationToken)
    {
        ResetOwnedDirectory(DocsPath("site"));
        var log = Path.Combine(DocsPath("diagnostics"), "docfx-build.jsonl");
        var result = await RunDocFxAsync("build", log, runDirectory, childExitCodes, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(result, "DocFX site build failed.");
        if (!File.Exists(Path.Combine(DocsPath("site"), "index.html")))
        {
            throw new DocumentationOperationException(OutcomeKind.InfrastructureFailure,
                "DocFX completed but did not create the documentation site homepage.");
        }
        DocumentationPublicationPolicy.ValidateOutput(DocsPath("content"), DocsPath("site"));
        return CountDocFxWarnings(log);
    }

    private Task<ProcessResult> RunDocFxAsync(string command, string diagnosticLog, string runDirectory,
        List<int> childExitCodes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(diagnosticLog)!);
        if (File.Exists(diagnosticLog)) File.Delete(diagnosticLog);
        var config = Path.Combine(artifactPaths.RepositoryRoot, "docs", "site-config", "docfx.json");
        return RunProcessAsync($"docfx-{command}", "dotnet",
            ["tool", "run", "docfx", "--", command, config, "--log", diagnosticLog, "--logLevel", "warning"],
            artifactPaths.RepositoryRoot, runDirectory, childExitCodes, cancellationToken,
            timeout: TimeSpan.FromMinutes(45));
    }

    private async Task<ProcessResult> RunProcessAsync(string name, string executable, IReadOnlyList<string> arguments,
        string workingDirectory, string runDirectory, List<int> childExitCodes, CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string?>? environmentVariables = null, TimeSpan? timeout = null)
    {
        var result = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = executable,
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            StandardOutputLogPath = Path.Combine(runDirectory, $"{name}.stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, $"{name}.stderr.log"),
            EnvironmentVariables = environmentVariables,
            Timeout = timeout,
            Progress = progressEvent =>
            {
                if (progressEvent.StandardOutputLineCount > 0 || progressEvent.StandardErrorLineCount > 0)
                {
                    WriteProgress($"{name} is still running ({progressEvent.Elapsed:mm\\:ss}).");
                }
            }
        }, cancellationToken).ConfigureAwait(false);
        if (result.RawExitCode is { } exitCode) childExitCodes.Add(exitCode);
        return result;
    }

    private static void EnsureSuccess(ProcessResult result, string description)
    {
        if (result.Outcome != OutcomeKind.Success)
        {
            throw new DocumentationOperationException(result.Outcome, description, result.RawExitCode,
                JoinDetails(result.StandardErrorLogPath, result.StandardOutputLogPath, result.Failure));
        }
    }

    private async Task WriteContentManifestAsync(string runDirectory, Guid runId, string commit, CancellationToken cancellationToken)
    {
        var docsRoot = DocsPath(string.Empty);
        var files = new List<object>();
        foreach (var file in DocumentationOwnedOutputCatalog.EnumerateFiles(docsRoot, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new FileInfo(file);
            await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
            files.Add(new
            {
                path = Path.GetRelativePath(docsRoot, file).Replace('\\', '/'),
                length = info.Length,
                sha256 = Convert.ToHexStringLower(hash)
            });
        }
        var document = new
        {
            schemaVersion = 1,
            runId = runId.ToString("D"),
            sourceCommit = commit.ToLowerInvariant(),
            files
        };
        await File.WriteAllTextAsync(Path.Combine(runDirectory, "content-manifest.json"),
            JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
    }

    private static void ClearApiMetadata(string apiDirectory)
    {
        Directory.CreateDirectory(apiDirectory);
        foreach (var file in Directory.EnumerateFiles(apiDirectory, "*.yml", SearchOption.TopDirectoryOnly)) File.Delete(file);
        var toc = Path.Combine(apiDirectory, "toc.yml");
        if (File.Exists(toc)) File.Delete(toc);
    }

    private static void RemoveExternalInheritedMembers(string apiDirectory)
    {
        if (!Directory.Exists(apiDirectory)) return;
        foreach (var file in Directory.EnumerateFiles(apiDirectory, "*.yml", SearchOption.TopDirectoryOnly)
            .Where(static path => !string.Equals(Path.GetFileName(path), "toc.yml", StringComparison.OrdinalIgnoreCase)))
        {
            var lines = File.ReadAllLines(file);
            var output = new List<string>();
            for (var index = 0; index < lines.Length; index++)
            {
                if (!string.Equals(lines[index], "  inheritedMembers:", StringComparison.Ordinal))
                {
                    output.Add(lines[index]);
                    continue;
                }

                var kept = new List<string>();
                index++;
                while (index < lines.Length && lines[index].StartsWith("  - ", StringComparison.Ordinal))
                {
                    if (lines[index][4..].StartsWith("Rowles.LeanCorpus.", StringComparison.Ordinal)) kept.Add(lines[index]);
                    index++;
                }
                if (kept.Count > 0)
                {
                    output.Add("  inheritedMembers:");
                    output.AddRange(kept);
                }
                index--;
            }
            File.WriteAllLines(file, output, new UTF8Encoding(false));
        }
    }

    private int CountDocFxWarnings(string path)
    {
        if (!File.Exists(path)) return 0;
        var count = 0;
        foreach (var line in File.ReadLines(path))
        {
            try
            {
                using var record = JsonDocument.Parse(line);
                if (record.RootElement.ValueKind == JsonValueKind.Object && record.RootElement.TryGetProperty("severity", out var severity) &&
                    severity.ValueKind == JsonValueKind.String && string.Equals(severity.GetString(), "warning", StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            }
            catch (JsonException)
            {
                // Retain malformed DocFX diagnostics for review, as the legacy logger does.
            }
        }
        return count;
    }

    private string DocsPath(string name)
    {
        var root = Path.GetFullPath(Path.Combine(artifactPaths.ArtifactRoot, "docs"));
        var path = string.IsNullOrEmpty(name) ? root : Path.Combine(root, name);
        var fullPath = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var trimmedRoot = Path.TrimEndingDirectorySeparator(root);
        if (!string.Equals(fullPath, trimmedRoot, comparison) &&
            !fullPath.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException($"Documentation artefact path escapes its owned root: {fullPath}");
        }
        var artifactRoot = Path.GetFullPath(artifactPaths.ArtifactRoot);
        if (Directory.Exists(artifactRoot) && (File.GetAttributes(artifactRoot) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"Documentation artefact root cannot be a linked directory: {artifactRoot}");
        }
        var current = artifactRoot;
        var relative = Path.GetRelativePath(artifactRoot, fullPath);
        if (relative != ".")
        {
            foreach (var segment in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                current = Path.Combine(current, segment);
                if ((Directory.Exists(current) || File.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"Documentation artefact path cannot contain linked directories: {current}");
                }
            }
        }
        return fullPath;
    }

    private static void ResetOwnedDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Documentation build refuses to reset a linked directory: {path}");
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"Documentation build refuses to remove a linked staged path: {entry}");
                }
                if ((attributes & FileAttributes.Directory) != 0) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
        }
        else
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void RequireFile(string path, string message, bool directory = false)
    {
        if (directory ? !Directory.Exists(path) : !File.Exists(path))
        {
            throw new DocumentationOperationException(OutcomeKind.ValidationFailure, $"{message} {path}");
        }
    }

    private void WriteProgress(string message) => progress?.Invoke(message);

    private static string? JoinDetails(params string?[] details)
    {
        var values = details.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return values.Length == 0 ? null : string.Join(Environment.NewLine, values);
    }

    private sealed class DocumentationOperationException(OutcomeKind outcome, string message, int? rawExitCode = null, string? details = null) : Exception(message)
    {
        public OutcomeKind Outcome { get; } = outcome;
        public int? RawExitCode { get; } = rawExitCode;
        public string? Details { get; } = details;
    }
}
