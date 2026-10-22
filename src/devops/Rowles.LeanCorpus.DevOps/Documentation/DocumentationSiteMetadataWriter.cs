using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rowles.LeanCorpus.DevOps.Build;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Repository;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Documentation;

internal sealed record DocumentationVersionResult(string ProductVersion, string? DevOpsVersion, IReadOnlyDictionary<string, string> ToolVersions);

internal sealed class DocumentationSiteMetadataWriter(IProcessRunner processRunner)
{
    private const string RepositoryUrl = "https://github.com/jordansrowles/LeanCorpus";
    private static readonly Regex SemanticVersionPattern = new("^\\d+\\.\\d+\\.\\d+(?:[-+][0-9A-Za-z.-]+)?$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public async Task<DocumentationVersionResult> WriteAsync(
        CheckoutIdentity checkout,
        string generatedDirectory,
        string runDirectory,
        GitProvenance provenance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(checkout);
        ArgumentException.ThrowIfNullOrWhiteSpace(generatedDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        ArgumentNullException.ThrowIfNull(provenance);
        if (provenance.Outcome != OutcomeKind.Success || string.IsNullOrWhiteSpace(provenance.Commit))
        {
            throw new InvalidDataException(provenance.Failure ?? "Could not establish documentation source provenance.");
        }

        var versions = await EvaluateVersionsAsync(checkout.RepositoryRoot, runDirectory, cancellationToken).ConfigureAwait(false);
        var coreVersion = versions.ProductVersion;
        var devOpsVersion = versions.DevOpsVersion;

        var commit = provenance.Commit.ToLowerInvariant();
        var root = Path.GetFullPath(generatedDirectory);
        Directory.CreateDirectory(root);
        var provenanceDocument = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["productVersion"] = coreVersion,
            ["devOpsVersion"] = devOpsVersion,
            ["repository"] = RepositoryUrl,
            ["commit"] = commit,
            ["commitUrl"] = $"{RepositoryUrl}/commit/{commit}",
            ["workingTreeDirty"] = provenance.Dirty
        };
        var siteMetadata = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["_appName"] = $"LeanCorpus {coreVersion}",
            ["_appTitle"] = $"LeanCorpus {coreVersion}",
            ["_appFooter"] = "LeanCorpus: a fast, embeddable full-text search engine for .NET",
            ["_enableSearch"] = true,
            ["categoryLayout"] = "nested",
            ["productVersion"] = coreVersion,
            ["devOpsVersion"] = devOpsVersion,
            ["repositoryCommit"] = commit,
            ["repositoryUrl"] = RepositoryUrl,
            ["sourceRevisionUrl"] = $"{RepositoryUrl}/commit/{commit}"
        };
        var options = new JsonSerializerOptions { WriteIndented = true };
        await File.WriteAllTextAsync(Path.Combine(root, "provenance.json"),
            JsonSerializer.Serialize(provenanceDocument, options) + Environment.NewLine,
            new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(root, "site-metadata.json"),
            JsonSerializer.Serialize(siteMetadata, options) + Environment.NewLine,
            new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

        return versions;
    }

    public async Task<DocumentationVersionResult> EvaluateVersionsAsync(
        string repositoryRoot,
        string runDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        var root = Path.GetFullPath(repositoryRoot);
        const string coreProject = "src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj";
        const string devOpsProject = "src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj";
        var coreVersion = await ReadVersionAsync(root, coreProject, "core-version", runDirectory, cancellationToken).ConfigureAwait(false);
        var devOpsProjectPath = Path.Combine(root, devOpsProject.Replace('/', Path.DirectorySeparatorChar));
        var devOpsVersion = File.Exists(devOpsProjectPath)
            ? await ReadVersionAsync(root, devOpsProject, "devops-version", runDirectory, cancellationToken).ConfigureAwait(false)
            : null;
        var toolVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Core"] = coreVersion };
        if (devOpsVersion is not null) toolVersions["DevOps"] = devOpsVersion;
        return new DocumentationVersionResult(coreVersion, devOpsVersion, toolVersions);
    }

    private async Task<string> ReadVersionAsync(
        string repositoryRoot,
        string projectPath,
        string logName,
        string runDirectory,
        CancellationToken cancellationToken)
    {
        var project = Path.Combine(repositoryRoot, projectPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(project))
        {
            throw new FileNotFoundException($"Version source project was not found: {projectPath}", project);
        }

        var result = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = "dotnet",
            Arguments = ["msbuild", project, "-getProperty:Version", "-nologo"],
            WorkingDirectory = repositoryRoot,
            StandardOutputLogPath = Path.Combine(runDirectory, $"{logName}.stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, $"{logName}.stderr.log"),
            Timeout = TimeSpan.FromMinutes(2)
        }, cancellationToken).ConfigureAwait(false);
        if (result.Outcome == OutcomeKind.Cancelled && cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (result.Outcome == OutcomeKind.Timeout)
        {
            throw new TimeoutException($"Evaluating Version from '{projectPath}' exceeded its timeout.");
        }
        if (result.Outcome != OutcomeKind.Success)
        {
            throw new InvalidDataException($"Could not evaluate Version from '{projectPath}': {result.Failure ?? string.Join(Environment.NewLine, result.StandardErrorTail)}");
        }

        var versions = result.StandardOutputTail.Select(static line => line.Trim())
            .Where(static line => SemanticVersionPattern.IsMatch(line))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (versions.Length != 1)
        {
            throw new InvalidDataException($"Expected one evaluated SemVer Version from '{projectPath}'; received: {string.Join(Environment.NewLine, result.StandardOutputTail)}");
        }
        return versions[0];
    }
}
