using System.Globalization;
using System.Text.Json;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Documentation;

internal sealed record DocumentationCoverageResult(string? SourceRunId, int CoverageFileCount, string? ReportDirectory);

internal sealed class DocumentationCoverageReportPreparer(IProcessRunner processRunner)
{
    public async Task<DocumentationCoverageResult> PrepareAsync(
        string repositoryRoot,
        string? sourceCommit,
        bool sourceDirty,
        string outputDirectory,
        string runDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(runDirectory);
        var repoRoot = Path.GetFullPath(repositoryRoot);
        var outputRoot = Path.GetFullPath(outputDirectory);
        EnsureOwnedDirectory(repoRoot, outputRoot);

        var selected = sourceDirty || string.IsNullOrWhiteSpace(sourceCommit)
            ? null
            : FindLatestSuccessfulRun(Path.Combine(repoRoot, "artifacts", "coverage", "runs"), sourceCommit);
        var xmlFiles = selected is null
            ? []
            : Directory.Exists(Path.Combine(selected.Directory, "raw"))
                ? Directory.EnumerateFiles(Path.Combine(selected.Directory, "raw"), "*.coverage.cobertura.*.xml", SearchOption.AllDirectories)
                    .OrderBy(static path => path, StringComparer.Ordinal).ToArray()
                : [];
        if (xmlFiles.Length == 0)
        {
            ResetDirectory(outputRoot);
            return new DocumentationCoverageResult(selected?.RunId, 0, null);
        }

        ResetDirectory(outputRoot);
        var reportPaths = string.Join(';', xmlFiles);
        var result = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = "dotnet",
            Arguments =
            [
                "tool", "run", "reportgenerator", "--",
                $"-reports:{reportPaths}",
                $"-targetdir:{outputRoot}",
                "-reporttypes:Html",
                "-title:LeanCorpus Coverage",
                $"-sourcedirs:{repoRoot}",
                "-assemblyfilters:+Rowles.*;+leancorpus-cli",
                "-filefilters:-**/obj/**/*.cs"
            ],
            WorkingDirectory = repoRoot,
            StandardOutputLogPath = Path.Combine(runDirectory, "reportgenerator.stdout.log"),
            StandardErrorLogPath = Path.Combine(runDirectory, "reportgenerator.stderr.log"),
            Timeout = TimeSpan.FromMinutes(20)
        }, cancellationToken).ConfigureAwait(false);
        if (result.Outcome != OutcomeKind.Success)
        {
            throw new InvalidDataException($"ReportGenerator failed while preparing documentation coverage: {result.Failure ?? string.Join(Environment.NewLine, result.StandardErrorTail)}");
        }
        if (!File.Exists(Path.Combine(outputRoot, "index.html")))
        {
            throw new InvalidDataException("ReportGenerator completed without creating docs/coverage/index.html.");
        }

        return new DocumentationCoverageResult(selected?.RunId, xmlFiles.Length, outputRoot);
    }

    private static CoverageRun? FindLatestSuccessfulRun(string runsRoot, string commit)
    {
        if (!Directory.Exists(runsRoot)) return null;
        var candidates = new List<CoverageRun>();
        foreach (var runDirectory in Directory.EnumerateDirectories(runsRoot))
        {
            var manifestPath = Path.Combine(runDirectory, "run.json");
            if (!File.Exists(manifestPath)) continue;
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
                var root = document.RootElement;
                if (!IsSuccessful(root) || IsDirty(root)) continue;
                var runCommit = GetString(root, "commit") ?? GetString(root, "sourceCommit");
                if (!string.Equals(runCommit, commit, StringComparison.OrdinalIgnoreCase)) continue;
                var completion = GetDate(root, "completedAtUtc") ?? GetDate(root, "completedUtc");
                if (completion is null) continue;
                var id = GetString(root, "runId") ?? Path.GetFileName(runDirectory);
                candidates.Add(new CoverageRun(runDirectory, id, completion.Value));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // A malformed or partial retained run is not valid documentation input.
            }
        }
        return candidates.OrderByDescending(static run => run.CompletedAtUtc).FirstOrDefault();
    }

    private static bool IsSuccessful(JsonElement root)
    {
        var status = GetString(root, "status");
        if (string.Equals(status, "Passed", StringComparison.Ordinal)) return true;
        return string.Equals(GetString(root, "state"), "succeeded", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDirty(JsonElement root)
    {
        if (root.TryGetProperty("dirty", out var dirty) && dirty.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return dirty.GetBoolean();
        }
        return root.TryGetProperty("sourceDirty", out dirty) && dirty.ValueKind == JsonValueKind.True;
    }

    private static string? GetString(JsonElement root, string key)
        => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? GetDate(JsonElement root, string key)
        => root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String &&
           DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;

    private static void EnsureOwnedDirectory(string repositoryRoot, string outputDirectory)
    {
        var artifactRoot = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts"));
        var output = Path.GetFullPath(outputDirectory);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!output.StartsWith(Path.TrimEndingDirectorySeparator(artifactRoot) + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidDataException($"Documentation coverage output must remain inside the repository artefact tree: {output}");
        }
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Documentation coverage refuses to reset a linked directory: {path}");
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"Documentation coverage refuses to remove a linked path: {entry}");
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

    private sealed record CoverageRun(string Directory, string RunId, DateTimeOffset CompletedAtUtc);
}
