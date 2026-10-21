using System.Text;
using Rowles.LeanCorpus.DevOps.Configuration;
using Microsoft.Extensions.FileSystemGlobbing;
using Rowles.LeanCorpus.DevOps.Infrastructure;
using Rowles.LeanCorpus.DevOps.Terminal;

namespace Rowles.LeanCorpus.DevOps.Testing;

internal sealed record AffectedTestSelection(
    IReadOnlyList<string> Paths,
    IReadOnlyList<string> Targets,
    IReadOnlyDictionary<string, IReadOnlyList<string>> AreasBySuite)
{
    public string Mode { get; init; } = "dirty";
    public string SuppliedCommit { get; init; } = string.Empty;
    public string SuppliedRange { get; init; } = string.Empty;
    public string ResolvedCommit { get; init; } = string.Empty;
    public string FirstParent { get; init; } = string.Empty;
    public string Head { get; init; } = string.Empty;
}

internal sealed class AffectedSelectionService(IProcessRunner processRunner)
{
    public static AffectedTestSelection MapChangedPaths(
        IReadOnlyDictionary<string, TestSuiteConfiguration> suites,
        IReadOnlyDictionary<string, CodeAreaConfiguration> codeAreas,
        IEnumerable<string> changedPaths)
    {
        ArgumentNullException.ThrowIfNull(suites);
        ArgumentNullException.ThrowIfNull(codeAreas);
        ArgumentNullException.ThrowIfNull(changedPaths);

        var pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var paths = changedPaths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Replace('\\', '/').TrimStart('/'))
            .Distinct(pathComparer)
            .OrderBy(static path => path, pathComparer)
            .ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidOperationException("Affected source selected no changed files. Refusing to run zero tests.");
        }

        var matchedPaths = new HashSet<string>(pathComparer);
        var areasBySuite = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (mappingName, mapping) in codeAreas.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            var matcher = new Matcher(pathComparer == StringComparer.OrdinalIgnoreCase
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
            foreach (var glob in mapping.Globs)
            {
                matcher.AddInclude(glob.Replace('\\', '/').TrimStart('/'));
            }

            foreach (var path in paths)
            {
                if (!matcher.Match(path).HasMatches)
                {
                    continue;
                }

                matchedPaths.Add(path);
                foreach (var target in mapping.Targets)
                {
                    var parts = target.Split(':', 2);
                    if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
                    {
                        throw new InvalidOperationException($"Code-area mapping '{mappingName}' contains invalid target '{target}'.");
                    }

                    if (!suites.ContainsKey(parts[0]))
                    {
                        throw new InvalidOperationException($"Code-area mapping '{mappingName}' references unknown test suite '{parts[0]}'.");
                    }

                    if (!areasBySuite.TryGetValue(parts[0], out var suiteAreas))
                    {
                        suiteAreas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        areasBySuite.Add(parts[0], suiteAreas);
                    }
                    suiteAreas.Add(parts[1]);
                }
            }
        }

        var unmatched = paths.Where(path => !matchedPaths.Contains(path)).ToArray();
        if (unmatched.Length > 0)
        {
            throw new InvalidOperationException(
                $"Selected files have no code-area mapping. Refusing to run zero tests: {string.Join(", ", unmatched)}");
        }
        if (areasBySuite.Count == 0)
        {
            throw new InvalidOperationException("No executable test areas matched the changed files.");
        }

        var orderedAreas = areasBySuite
            .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value.Order(pathComparer).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var targets = orderedAreas
            .SelectMany(static pair => pair.Value.Select(area => $"{pair.Key}:{area}"))
            .OrderBy(static target => target, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new AffectedTestSelection(paths, targets, orderedAreas);
    }

    public async Task<AffectedTestSelection> ResolveAsync(
        string repositoryRoot,
        IReadOnlyDictionary<string, TestSuiteConfiguration> suites,
        IReadOnlyDictionary<string, CodeAreaConfiguration> codeAreas,
        string commit = "",
    string range = "",
    CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (!Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException($"Repository '{repositoryRoot}' does not exist.");
        }
        if (!string.IsNullOrWhiteSpace(commit) && !string.IsNullOrWhiteSpace(range))
        {
            throw new InvalidOperationException("-Commit and -Range are mutually exclusive.");
        }

        var root = Path.GetFullPath(repositoryRoot);
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "LeanCorpus affected git " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var mode = "dirty";
            var resolvedCommit = string.Empty;
            var firstParent = string.Empty;
            IReadOnlyList<string> paths;
            if (!string.IsNullOrWhiteSpace(commit))
            {
                mode = "commit";
                resolvedCommit = (await ReadGitTextAsync(root, temporaryDirectory,
                    ["rev-parse", "--verify", "--end-of-options", commit + "^{commit}"], cancellationToken).ConfigureAwait(false)).Trim();
                var ancestry = (await ReadGitTextAsync(root, temporaryDirectory,
                    ["rev-list", "--parents", "-n", "1", resolvedCommit, "--"], cancellationToken).ConfigureAwait(false))
                    .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                firstParent = ancestry.Length > 1 ? ancestry[1] : string.Empty;
                var diffArguments = firstParent.Length > 0
                    ? new[] { "diff", "--name-only", "-z", firstParent, resolvedCommit, "--" }
                    : new[] { "diff-tree", "--root", "--no-commit-id", "-r", "--name-only", "-z", resolvedCommit, "--" };
                paths = await ReadGitPathsAsync(root, temporaryDirectory, diffArguments, cancellationToken).ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(range))
            {
                mode = "range";
                var separator = range.IndexOf("..", StringComparison.Ordinal);
                if (range.StartsWith("-", StringComparison.Ordinal) || separator <= 0 || separator >= range.Length - 2)
                {
                    throw new InvalidOperationException($"Invalid range '{range}': supply an explicit revision range such as A..B.");
                }
                paths = await ReadGitPathsAsync(root, temporaryDirectory,
                    ["diff", "--name-only", "-z", range, "--"], cancellationToken).ConfigureAwait(false);
                resolvedCommit = range;
            }
            else
            {
                var dirtyPaths = new List<string>();
                foreach (var arguments in new[]
                {
                    new[] { "diff", "--name-only", "-z", "--" },
                    new[] { "diff", "--cached", "--name-only", "-z", "--" },
                    new[] { "ls-files", "--others", "--exclude-standard", "-z" }
                })
                {
                    dirtyPaths.AddRange(await ReadGitPathsAsync(root, temporaryDirectory, arguments, cancellationToken).ConfigureAwait(false));
                }
                paths = NormalizePaths(dirtyPaths);
            }

            if (paths.Count == 0)
            {
                throw new InvalidOperationException($"Affected source {mode} '{commit}{range}' selected no changed files. Refusing to run zero tests.");
            }

            var head = (await ReadGitTextAsync(root, temporaryDirectory, ["rev-parse", "--verify", "HEAD"], cancellationToken).ConfigureAwait(false)).Trim();
            var mapped = MapChangedPaths(suites, codeAreas, paths);
            return mapped with
            {
                Mode = mode,
                SuppliedCommit = commit,
                SuppliedRange = range,
                ResolvedCommit = resolvedCommit,
                FirstParent = firstParent,
                Head = head
            };
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private async Task<IReadOnlyList<string>> ReadGitPathsAsync(
        string repositoryRoot,
        string logDirectory,
        IReadOnlyList<string> gitArguments,
        CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(repositoryRoot, logDirectory, gitArguments, cancellationToken).ConfigureAwait(false);
        var paths = new List<string>();
        var start = 0;
        for (var index = 0; index < output.Length; index++)
        {
            if (output[index] != 0)
            {
                continue;
            }
            if (index > start)
            {
                paths.Add(DecodeUtf8(output[start..index]));
            }
            start = index + 1;
        }
        if (start != output.Length)
        {
            throw new InvalidDataException("Git returned a NUL-delimited path list without its final NUL terminator.");
        }
        return NormalizePaths(paths);
    }

    private async Task<string> ReadGitTextAsync(
        string repositoryRoot,
        string logDirectory,
        IReadOnlyList<string> gitArguments,
        CancellationToken cancellationToken)
    {
        var output = await RunGitAsync(repositoryRoot, logDirectory, gitArguments, cancellationToken).ConfigureAwait(false);
        return DecodeUtf8(output);
    }

    private async Task<byte[]> RunGitAsync(
        string repositoryRoot,
        string logDirectory,
        IReadOnlyList<string> gitArguments,
        CancellationToken cancellationToken)
    {
        var invocationId = Guid.NewGuid().ToString("N");
        var stdoutPath = Path.Combine(logDirectory, invocationId + ".stdout.log");
        var stderrPath = Path.Combine(logDirectory, invocationId + ".stderr.log");
        var result = await processRunner.RunAsync(new ProcessRequest
        {
            ExecutablePath = "git",
            Arguments = ["-C", repositoryRoot, .. gitArguments],
            WorkingDirectory = repositoryRoot,
            StandardOutputLogPath = stdoutPath,
            StandardErrorLogPath = stderrPath,
            ProgressInterval = TimeSpan.FromSeconds(5)
        }, cancellationToken).ConfigureAwait(false);

        if (result.Outcome != OutcomeKind.Success)
        {
            throw new InvalidOperationException(
                $"Unable to resolve affected source with git {string.Join(' ', gitArguments)}: {result.Failure ?? string.Join(Environment.NewLine, result.StandardErrorTail)}");
        }
        return await File.ReadAllBytesAsync(stdoutPath, cancellationToken).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> NormalizePaths(IEnumerable<string> paths)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        return paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => path.Replace('\\', '/').TrimStart('/'))
            .Distinct(comparer)
            .OrderBy(static path => path, comparer)
            .ToArray();
    }

    private static string DecodeUtf8(ReadOnlySpan<byte> value)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(value);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Git returned a path that is not valid UTF-8.", exception);
        }
    }
}
