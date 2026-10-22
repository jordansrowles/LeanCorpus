using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.DevOps.Documentation;

internal sealed record DocumentationStaticValidationResult(int MarkdownPages, int TocFiles, int HtmlPages, int LocalReferences, int Fragments);

internal sealed class DocumentationStaticValidator
{
    private static readonly Regex TocHrefPattern = new("^\\s*href:\\s*(?:'(?<single>[^']*)'|\"(?<double>[^\"]*)\"|(?<plain>\\S+))\\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex FrontMatterPattern = new("\\A---\\r?\\n(?<yaml>[\\s\\S]*?)\\r?\\n---(?=\\r?\\n|$)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ContributionUrlPattern = new("(?m)^docurl:\\s*'(?<url>[^']+)'\\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex HtmlReferencePattern = new("(?is)\\b(?:href|src)\\s*=\\s*[\"'](?<url>[^\"']+)[\"']", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex AnchorPattern = new("(?is)\\b(?:id|name)\\s*=\\s*[\"'](?<id>[^\"']+)[\"']", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public DocumentationStaticValidationResult Validate(string repositoryRoot, string contentDirectory, string apiDirectory, string siteDirectory,
        string productVersion, string? devOpsVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
        var repository = Path.GetFullPath(repositoryRoot);
        var contentRoot = Path.GetFullPath(contentDirectory);
        var apiRoot = Path.GetFullPath(apiDirectory);
        var siteRoot = Path.GetFullPath(siteDirectory);
        var artifactRoot = Path.GetFullPath(Path.Combine(repository, "artifacts", "docs"));

        var generatedRoot = Path.Combine(artifactRoot, "generated");
        var provenancePath = Path.Combine(generatedRoot, "provenance.json");
        var metadataPath = Path.Combine(generatedRoot, "site-metadata.json");
        RequireFile(provenancePath, "Generated provenance metadata is missing.");
        RequireFile(metadataPath, "Generated site metadata is missing.");
        using var provenanceDocument = JsonDocument.Parse(File.ReadAllBytes(provenancePath));
        using var metadataDocument = JsonDocument.Parse(File.ReadAllBytes(metadataPath));
        var provenance = provenanceDocument.RootElement;
        var metadata = metadataDocument.RootElement;
        var commit = GetString(provenance, "commit");
        if (commit is null || !Regex.IsMatch(commit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("The repository revision is missing or malformed.");
        }

        if (!string.Equals(GetString(metadata, "productVersion"), productVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Site version does not match the LeanCorpus project version.");
        }
        if (devOpsVersion is not null)
        {
            if (!string.Equals(GetString(metadata, "devOpsVersion"), devOpsVersion, StringComparison.Ordinal) ||
                !string.Equals(GetString(provenance, "devOpsVersion"), devOpsVersion, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Site metadata does not preserve the independently evaluated DevOps version.");
            }
        }
        else if (GetString(metadata, "devOpsVersion") is not null || GetString(provenance, "devOpsVersion") is not null)
        {
            throw new InvalidDataException("Site metadata borrowed a version when no DevOps project exists.");
        }

        foreach (var relative in RequiredContent)
        {
            RequireFile(Path.Combine(contentRoot, relative.Replace('/', Path.DirectorySeparatorChar)), $"Required staged document is missing: {relative}");
        }
        var changelogIndex = File.ReadAllText(Path.Combine(contentRoot, "changelog", "index.md"));
        var changelogToc = File.ReadAllText(Path.Combine(contentRoot, "changelog", "toc.yml"));
        if (!changelogIndex.Contains("## LeanCorpus DevOps", StringComparison.Ordinal) ||
            !changelogToc.Contains("name: 'LeanCorpus DevOps'", StringComparison.Ordinal))
        {
            throw new InvalidDataException("The changelog navigation does not keep the DevOps release stream independent.");
        }
        RequireFile(Path.Combine(siteRoot, "index.html"), "The generated site homepage is missing.");
        RequireFile(Path.Combine(siteRoot, "api", "index.html"), "The API landing page is missing.");

        var markdownFiles = Directory.EnumerateFiles(contentRoot, "*.md", SearchOption.AllDirectories).ToArray();
        if (markdownFiles.Length == 0) throw new InvalidDataException("No staged Markdown pages were found.");
        var expectedUrlPrefix = $"https://github.com/jordansrowles/LeanCorpus/blob/{commit}/";
        foreach (var file in markdownFiles)
        {
            var source = File.ReadAllText(file);
            var frontMatter = FrontMatterPattern.Match(source);
            if (!frontMatter.Success) throw new InvalidDataException($"Staged Markdown has no front matter: {file}");
            var urlMatch = ContributionUrlPattern.Match(frontMatter.Groups["yaml"].Value);
            if (!urlMatch.Success) throw new InvalidDataException($"Staged Markdown has no canonical contribution URL: {file}");
            var url = urlMatch.Groups["url"].Value;
            if (!url.StartsWith(expectedUrlPrefix, StringComparison.Ordinal) || url.Contains("/artifacts/", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Contribution URL does not use the source revision or points into generated artifacts: {url}");
            }
            var relativeSource = Uri.UnescapeDataString(url[expectedUrlPrefix.Length..]).Replace('/', Path.DirectorySeparatorChar);
            RequireContainedFile(repository, Path.Combine(repository, relativeSource), $"Contribution URL target does not exist in the repository: {relativeSource}");
        }

        var tocFiles = Directory.EnumerateFiles(contentRoot, "toc.yml", SearchOption.AllDirectories).ToArray();
        if (tocFiles.Length == 0) throw new InvalidDataException("No generated navigation TOCs were found.");
        foreach (var toc in tocFiles)
        {
            foreach (var target in ReadTocTargets(toc))
            {
                if (HasScheme(target) || target.StartsWith('#')) continue;
                var pathPart = target.Split(['?', '#'], 2)[0];
                if (string.IsNullOrWhiteSpace(pathPart)) continue;
                var decoded = Uri.UnescapeDataString(pathPart);
                var targetPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(toc)!, decoded.Replace('/', Path.DirectorySeparatorChar)));
                RequireContained(artifactRoot, targetPath, $"TOC target escapes the documentation artefact tree: {toc} -> {target}");
                if (!File.Exists(targetPath)) targetPath = Path.GetFullPath(Path.Combine(apiRoot, decoded.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(targetPath)) throw new InvalidDataException($"TOC target is missing: {toc} -> {target}");
            }
        }

        var htmlFiles = Directory.EnumerateFiles(siteRoot, "*.html", SearchOption.AllDirectories).ToArray();
        if (htmlFiles.Length == 0) throw new InvalidDataException("No generated HTML pages were found.");
        var referenceCount = 0;
        var fragmentCount = 0;
        var anchorsByPage = new Dictionary<string, HashSet<string>>(PathComparer);
        var localSite = new Uri("https://docs.local/");
        foreach (var file in htmlFiles)
        {
            var relativeHtml = Path.GetRelativePath(siteRoot, file).Replace('\\', '/');
            var htmlUri = new Uri(localSite, relativeHtml);
            var html = File.ReadAllText(file);
            foreach (Match match in HtmlReferencePattern.Matches(html))
            {
                var reference = WebUtility.HtmlDecode(match.Groups["url"].Value.Trim());
                if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith('#') || reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
                var targetUri = new Uri(htmlUri, reference);
                if (targetUri.Scheme is not ("http" or "https") || targetUri.Authority != localSite.Authority) continue;
                var relativeTarget = Uri.UnescapeDataString(targetUri.AbsolutePath.TrimStart('/'));
                if (string.IsNullOrWhiteSpace(relativeTarget)) relativeTarget = "index.html";
                var targetPath = Path.GetFullPath(Path.Combine(siteRoot, relativeTarget.Replace('/', Path.DirectorySeparatorChar)));
                RequireContained(siteRoot, targetPath, $"Generated HTML reference escapes the site: {file} -> {reference}");
                if (Directory.Exists(targetPath)) targetPath = Path.Combine(targetPath, "index.html");
                if (!File.Exists(targetPath)) throw new InvalidDataException($"Generated HTML references a missing local page or asset: {file} -> {reference}");
                if (!string.IsNullOrWhiteSpace(targetUri.Fragment) && Path.GetExtension(targetPath).Equals(".html", StringComparison.OrdinalIgnoreCase))
                {
                    var anchorKey = Path.GetFullPath(targetPath);
                    if (!anchorsByPage.TryGetValue(anchorKey, out var anchors))
                    {
                        anchors = ReadAnchors(anchorKey);
                        anchorsByPage.Add(anchorKey, anchors);
                    }
                    var fragment = Uri.UnescapeDataString(targetUri.Fragment.TrimStart('#'));
                    if (!anchors.Contains(fragment)) throw new InvalidDataException($"Generated HTML references a missing fragment: {file} -> {reference}");
                    fragmentCount++;
                }
                referenceCount++;
            }
        }

        return new DocumentationStaticValidationResult(markdownFiles.Length, tocFiles.Length, htmlFiles.Length, referenceCount, fragmentCount);
    }

    private static string[] RequiredContent =>
    [
        "index.md", "toc.yml", "articles/ADRs/index.md", "articles/ADRs/toc.yml",
        "articles/ADRs/ADR040-bounded-field-length-bodies.md", "articles/ADRs/ADR041-physical-index-runtime-lifecycle.md",
        "articles/ADRs/Server/ADR-S005-aot-and-framework-compatibility-boundary.md", "changelog/index.md", "changelog/toc.yml",
        "changelog/devops/unreleased.md", "benchmarks/overview.md", "benchmarks/toc.yml"
    ];

    private static IEnumerable<string> ReadTocTargets(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var match = TocHrefPattern.Match(line);
            if (!match.Success) continue;
            var target = match.Groups["single"].Success ? match.Groups["single"].Value
                : match.Groups["double"].Success ? match.Groups["double"].Value
                : match.Groups["plain"].Value;
            if (!string.IsNullOrWhiteSpace(target)) yield return target;
        }
    }

    private static HashSet<string> ReadAnchors(string htmlPath)
    {
        var anchors = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in AnchorPattern.Matches(File.ReadAllText(htmlPath)))
        {
            anchors.Add(WebUtility.HtmlDecode(match.Groups["id"].Value));
        }
        return anchors;
    }

    private static string? GetString(JsonElement element, string key)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool HasScheme(string value) => Regex.IsMatch(value, "^[a-z][a-z0-9+.-]*:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static void RequireFile(string path, string message)
    {
        if (!File.Exists(path)) throw new FileNotFoundException(message, path);
    }

    private static void RequireContainedFile(string root, string path, string message)
    {
        RequireContained(root, path, message);
        RequireFile(path, message);
    }

    private static void RequireContained(string root, string path, string message)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, PathComparison)) throw new InvalidDataException(message);
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
