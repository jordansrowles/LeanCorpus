using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.DevOps.Documentation;

internal sealed class RepositoryDocumentationImporter
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private static readonly Regex MarkdownLinkPattern = new(
        "(?<prefix>!?\\[[^\\]]*\\]\\()(?<target><[^>]+>|[^)\\s]+)(?<suffix>\\s*(?:\"[^\"]*\"|'[^']*')?\\))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeout);
    private static readonly Regex GitCommitPattern = new("^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeout);

    public int Import(
        string repositoryRoot,
        string contentDirectory,
        string docsSourceDirectory,
        string manifestPath,
        string sourceCommit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(docsSourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCommit);
        if (!GitCommitPattern.IsMatch(sourceCommit))
        {
            throw new InvalidDataException("Documentation source commit must be a full 40-character Git SHA.");
        }

        var repoRoot = Path.GetFullPath(repositoryRoot);
        var contentRoot = Path.GetFullPath(contentDirectory);
        var docsRoot = Path.GetFullPath(docsSourceDirectory);
        var imports = ReadManifest(Path.GetFullPath(manifestPath));
        if (!Directory.Exists(repoRoot) || !Directory.Exists(contentRoot) || !Directory.Exists(docsRoot))
        {
            throw new DirectoryNotFoundException("Repository, staged content and canonical docs directories must exist before importing repository documentation.");
        }

        var destinationBySource = new Dictionary<string, string>(PathComparer);
        var plannedDestinations = new Dictionary<string, string>(PathComparer);
        var importPlans = new List<ImportPlan>(imports.Count);
        foreach (var entry in imports)
        {
            var sourcePath = ResolveRelativePath(repoRoot, entry.Source, "source");
            var resolvedSourcePath = ResolveExistingPath(sourcePath);
            if (!IsContained(ResolveExistingPath(repoRoot), resolvedSourcePath) || !File.Exists(sourcePath))
            {
                throw new InvalidDataException($"Repository documentation source not found inside the repository: {entry.Source}");
            }

            var destinationPath = ResolveRelativePath(contentRoot, entry.Destination, "destination");
            var resolvedDestinationPath = ResolveExistingPath(destinationPath);
            if (!IsContained(ResolveExistingPath(contentRoot), resolvedDestinationPath))
            {
                throw new InvalidDataException($"Documentation import destination escapes the staged content tree: {entry.Destination}");
            }
            if (destinationBySource.ContainsKey(sourcePath))
            {
                throw new InvalidDataException($"Repository documentation source is mapped more than once: {entry.Source}");
            }
            if (plannedDestinations.TryGetValue(destinationPath, out var priorSource))
            {
                throw new InvalidDataException($"Repository documentation destination is mapped more than once: {entry.Destination} (also used by '{priorSource}').");
            }
            if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
            {
                throw new InvalidDataException($"Repository documentation destination collides with staged content: {entry.Destination}");
            }

            var sourceRelative = NormalizeRelativePath(entry.Source);
            var destinationRelative = NormalizeRelativePath(entry.Destination);
            destinationBySource.Add(sourcePath, destinationRelative);
            destinationBySource.TryAdd(resolvedSourcePath, destinationRelative);
            plannedDestinations.Add(destinationPath, entry.Source);
            importPlans.Add(new ImportPlan(sourcePath, destinationPath, sourceRelative, destinationRelative));
        }

        var plannedWrites = new Dictionary<string, string>(PathComparer);
        foreach (var plan in importPlans)
        {
            var sourceContent = File.ReadAllText(plan.SourcePath);
            var rewritten = RewriteImportedLinks(sourceContent, plan.SourcePath, plan.DestinationPath,
                repoRoot, docsRoot, contentRoot, destinationBySource, sourceCommit);
            var sourceUrl = RepositoryWebLinks.BlobUrl(sourceCommit, plan.SourceRelative);
            var notice = $"> [!NOTE]\n> This page is generated from [{plan.SourceRelative}]({sourceUrl}). Edit the repository file, not this copy.\n\n";
            plannedWrites.Add(plan.DestinationPath, notice + rewritten.TrimStart());
        }

        var canonicalWrites = new List<KeyValuePair<string, string>>();
        foreach (var sourcePath in Directory.EnumerateFiles(docsRoot, "*.md", SearchOption.AllDirectories))
        {
            var sourceRelative = Path.GetRelativePath(docsRoot, sourcePath);
            var stagedPath = Path.GetFullPath(Path.Combine(contentRoot, sourceRelative));
            if (!File.Exists(stagedPath))
            {
                continue;
            }

            var stagedContent = File.ReadAllText(stagedPath);
            var rewritten = RewriteCanonicalLinks(stagedContent, sourcePath, stagedPath,
                repoRoot, docsRoot, contentRoot, destinationBySource, sourceCommit);
            canonicalWrites.Add(new KeyValuePair<string, string>(stagedPath, rewritten));
        }

        foreach (var write in plannedWrites)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(write.Key)!);
            WriteGeneratedContent(write.Key, write.Value);
        }
        foreach (var write in canonicalWrites)
        {
            WriteGeneratedContent(write.Key, write.Value);
        }

        return importPlans.Count;
    }

    internal static IReadOnlyList<(string Source, string Destination)> ReadImports(string manifestPath)
        => ReadManifest(Path.GetFullPath(manifestPath))
            .Select(static entry => (entry.Source, entry.Destination))
            .ToArray();

    private static IReadOnlyList<ImportEntry> ReadManifest(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Documentation import manifest was not found.", manifestPath);
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            var root = document.RootElement;
            RequireUniqueProperties(root, ["schemaVersion", "imports"], "documentation import manifest");
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var schemaVersion) ||
                schemaVersion.ValueKind != JsonValueKind.Number ||
                !schemaVersion.TryGetInt32(out var version) || version != 1)
            {
                throw new InvalidDataException($"Unsupported documentation import manifest: {manifestPath}");
            }
            if (!root.TryGetProperty("imports", out var importsElement) || importsElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"Documentation import manifest must contain an imports array: {manifestPath}");
            }

            var imports = new List<ImportEntry>();
            foreach (var element in importsElement.EnumerateArray())
            {
                RequireUniqueProperties(element, ["source", "destination"], "documentation import entry");
                if (element.ValueKind != JsonValueKind.Object ||
                    !element.TryGetProperty("source", out var sourceElement) || sourceElement.ValueKind != JsonValueKind.String ||
                    !element.TryGetProperty("destination", out var destinationElement) || destinationElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidDataException("Every documentation import must have a string source and destination.");
                }

                var source = sourceElement.GetString();
                var destination = destinationElement.GetString();
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
                {
                    throw new InvalidDataException("Every documentation import must have a non-empty source and destination.");
                }
                imports.Add(new ImportEntry(source, destination));
            }

            if (imports.Count == 0)
            {
                throw new InvalidDataException("The documentation import manifest contains no imports.");
            }
            return imports;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Documentation import manifest is not valid JSON: {manifestPath}", exception);
        }
    }

    private static void RequireUniqueProperties(JsonElement element, IReadOnlyCollection<string> allowed, string description)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"Each {description} must be a JSON object.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw new InvalidDataException($"Duplicate property '{property.Name}' in {description}.");
            }
            if (!allowed.Contains(property.Name, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Unknown property '{property.Name}' in {description}.");
            }
        }
    }

    private static string RewriteImportedLinks(
        string content,
        string sourcePath,
        string destinationPath,
        string repositoryRoot,
        string docsRoot,
        string contentRoot,
        IReadOnlyDictionary<string, string> destinationBySource,
        string sourceCommit)
    {
        return MarkdownLinkPattern.Replace(content, match =>
        {
            var target = UnwrapTarget(match.Groups["target"].Value);
            if (target.StartsWith('#') || HasUriScheme(target))
            {
                return match.Value;
            }

            var (pathPart, suffix) = SplitSuffix(target);
            if (string.IsNullOrWhiteSpace(pathPart))
            {
                return match.Value;
            }

            var resolvedTarget = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, DecodePath(pathPart)));
            var mappedDestination = FindMappedDestination(resolvedTarget, destinationBySource);
            if (mappedDestination is not null)
            {
                var stagedTarget = Path.GetFullPath(Path.Combine(contentRoot, mappedDestination));
                var rewritten = ToSiteRelativePath(Path.GetDirectoryName(destinationPath)!, stagedTarget);
                return RebuildLink(match, EscapeUriPath(rewritten) + suffix);
            }

            if (IsContained(docsRoot, resolvedTarget))
            {
                var docsRelative = Path.GetRelativePath(docsRoot, resolvedTarget);
                var stagedTarget = Path.GetFullPath(Path.Combine(contentRoot, docsRelative));
                return RebuildLink(match, EscapeUriPath(ToSiteRelativePath(Path.GetDirectoryName(destinationPath)!, stagedTarget)) + suffix);
            }

            if (!File.Exists(resolvedTarget) && !Directory.Exists(resolvedTarget))
            {
                throw new InvalidDataException($"Unresolved repository documentation link '{target}' in '{Path.GetRelativePath(repositoryRoot, sourcePath)}'.");
            }
            if (!IsContained(ResolveExistingPath(repositoryRoot), ResolveExistingPath(resolvedTarget)))
            {
                throw new InvalidDataException($"Repository documentation link escapes the repository: '{target}' in '{Path.GetRelativePath(repositoryRoot, sourcePath)}'.");
            }

            return RebuildLink(match, ToGitHubUrl(repositoryRoot, resolvedTarget, suffix, sourceCommit));
        });
    }

    private static string RewriteCanonicalLinks(
        string content,
        string sourcePath,
        string destinationPath,
        string repositoryRoot,
        string docsRoot,
        string contentRoot,
        IReadOnlyDictionary<string, string> destinationBySource,
        string sourceCommit)
    {
        return MarkdownLinkPattern.Replace(content, match =>
        {
            var target = UnwrapTarget(match.Groups["target"].Value);
            if (target.StartsWith('#') || target.StartsWith('/') || target.StartsWith("~/", StringComparison.Ordinal) || HasUriScheme(target))
            {
                return match.Value;
            }

            var (pathPart, suffix) = SplitSuffix(target);
            if (string.IsNullOrWhiteSpace(pathPart))
            {
                return match.Value;
            }

            var resolvedTarget = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourcePath)!, DecodePath(pathPart)));
            if (IsContained(docsRoot, resolvedTarget))
            {
                var docsRelative = Path.GetRelativePath(docsRoot, resolvedTarget);
                var stagedTarget = Path.GetFullPath(Path.Combine(contentRoot, docsRelative));
                if (File.Exists(stagedTarget) || Directory.Exists(stagedTarget))
                {
                    return RebuildLink(match, EscapeUriPath(ToSiteRelativePath(Path.GetDirectoryName(destinationPath)!, stagedTarget)) + suffix);
                }

                // Generated pages such as benchmark overviews are added later in the prepare workflow.
                return match.Value;
            }

            var mappedDestination = FindMappedDestination(resolvedTarget, destinationBySource);
            if (mappedDestination is not null)
            {
                var stagedTarget = Path.GetFullPath(Path.Combine(contentRoot, mappedDestination));
                return RebuildLink(match, EscapeUriPath(ToSiteRelativePath(Path.GetDirectoryName(destinationPath)!, stagedTarget)) + suffix);
            }

            if (!IsContained(ResolveExistingPath(repositoryRoot), ResolveExistingPath(resolvedTarget)) ||
                !File.Exists(resolvedTarget) && !Directory.Exists(resolvedTarget))
            {
                throw new InvalidDataException($"Unresolved canonical documentation link '{target}' in '{Path.GetRelativePath(docsRoot, sourcePath)}'.");
            }

            return RebuildLink(match, ToGitHubUrl(repositoryRoot, resolvedTarget, suffix, sourceCommit));
        });
    }

    private static string? FindMappedDestination(string targetPath, IReadOnlyDictionary<string, string> destinationBySource)
    {
        var fullPath = Path.GetFullPath(targetPath);
        if (destinationBySource.TryGetValue(fullPath, out var destination))
        {
            return destination;
        }

        if (Directory.Exists(fullPath))
        {
            var readmePath = Path.GetFullPath(Path.Combine(fullPath, "README.md"));
            if (destinationBySource.TryGetValue(readmePath, out destination))
            {
                return destination;
            }
        }
        return null;
    }

    private static string ToGitHubUrl(string repositoryRoot, string targetPath, string fragment, string sourceCommit)
    {
        var relativePath = Path.GetRelativePath(repositoryRoot, targetPath).Replace('\\', '/');
        var kind = Directory.Exists(targetPath) ? "tree" : "blob";
        var escapedPath = EscapeUriPath(relativePath);
        var sourceUrl = kind == "tree"
            ? RepositoryWebLinks.TreeUrl(sourceCommit, escapedPath)
            : RepositoryWebLinks.BlobUrl(sourceCommit, escapedPath);
        return sourceUrl + fragment;
    }

    private static string ResolveRelativePath(string root, string relativePath, string description)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) ||
            relativePath.StartsWith('/') || relativePath.StartsWith('\\') ||
            Regex.IsMatch(relativePath, "^[A-Za-z]:", RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeout) ||
            relativePath.Replace('\\', '/').Split('/').Any(static segment => segment == ".."))
        {
            throw new InvalidDataException($"Documentation import {description} must be a relative path inside its owned root: {relativePath}");
        }

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsContained(fullRoot, fullPath))
        {
            throw new InvalidDataException($"Documentation import {description} escapes its owned root: {relativePath}");
        }
        return fullPath;
    }

    private static string ResolveExistingPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? throw new InvalidDataException($"Could not resolve filesystem root for '{path}'.");
        var segments = fullPath[root.Length..].Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        foreach (var segment in segments)
        {
            current = Path.Combine(current, segment);
            if (!File.Exists(current) && !Directory.Exists(current))
            {
                continue;
            }

            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            var resolved = info.ResolveLinkTarget(returnFinalTarget: true);
            if (resolved is not null)
            {
                current = resolved.FullName;
            }
        }
        return Path.GetFullPath(current);
    }

    private static bool IsContained(string root, string candidate)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullCandidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        if (PathComparer.Equals(fullRoot, fullCandidate))
        {
            return false;
        }
        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison);
    }

    private static (string Path, string Suffix) SplitSuffix(string target)
    {
        var queryIndex = target.IndexOf('?');
        var fragmentIndex = target.IndexOf('#');
        var suffixIndex = queryIndex < 0 ? fragmentIndex
            : fragmentIndex < 0 ? queryIndex
            : Math.Min(queryIndex, fragmentIndex);
        return suffixIndex < 0 ? (target, string.Empty) : (target[..suffixIndex], target[suffixIndex..]);
    }

    private static string UnwrapTarget(string target)
        => target.Length >= 2 && target[0] == '<' && target[^1] == '>' ? target[1..^1] : target;

    private static bool HasUriScheme(string value)
        => Regex.IsMatch(value, "^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexMatchTimeoutPolicy.MatchTimeout);

    private static string DecodePath(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value);
        }
        catch (UriFormatException exception)
        {
            throw new InvalidDataException($"Documentation link path is not valid URI-escaped text: '{value}'.", exception);
        }
    }

    private static string ToSiteRelativePath(string fromDirectory, string targetPath)
        => Path.GetRelativePath(fromDirectory, targetPath).Replace('\\', '/');

    private static string RebuildLink(Match match, string target)
    {
        var originalTarget = match.Groups["target"].Value;
        var renderedTarget = originalTarget.Length >= 2 && originalTarget[0] == '<' && originalTarget[^1] == '>'
            ? $"<{target}>"
            : target;
        return match.Groups["prefix"].Value + renderedTarget + match.Groups["suffix"].Value;
    }

    private static string EscapeUriPath(string path)
        => string.Join('/', path.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));

    private static string NormalizeRelativePath(string path)
        => path.Replace('\\', '/');

    private static void WriteGeneratedContent(string path, string content)
        => File.WriteAllText(path, content + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private sealed record ImportEntry(string Source, string Destination);

    private sealed record ImportPlan(string SourcePath, string DestinationPath, string SourceRelative, string DestinationRelative);
}
