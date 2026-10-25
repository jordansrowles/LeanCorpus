using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.DevOps.Documentation;

internal sealed class DocumentationSectionCatalogWriter
{
    private static readonly Regex HeadingPattern = new("^#\\s+(.+?)\\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex VersionFilePattern = new("^(?<major>\\d+)\\.(?<minor>\\d+)\\.(?<patch>\\d+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public void Generate(
        string directory,
        string title,
        string description,
        string indexFile = "index.md",
        bool includeIndexPages = false,
        bool separateServerChangelog = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(indexFile);

        var root = Path.GetFullPath(directory);
        Directory.CreateDirectory(root);
        var indexPath = Path.GetFullPath(Path.Combine(root, indexFile));
        var tocPath = Path.Combine(root, "toc.yml");
        if (File.Exists(indexPath) || Directory.Exists(indexPath) || File.Exists(tocPath) || Directory.Exists(tocPath))
        {
            throw new InvalidDataException($"Generated {title} catalogue collides with an existing staged file in '{root}'.");
        }

        var pages = Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Select(path => CreatePage(root, path))
            .Where(page => !string.Equals(page.FileName, "_template.md", StringComparison.OrdinalIgnoreCase) &&
                (includeIndexPages || !string.Equals(page.FileName, "index.md", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(static page => page.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static page => page.RelativePath, StringComparer.Ordinal)
            .ToArray();

        var groups = separateServerChangelog
            ? CreateChangelogGroups(pages)
            : [new CatalogGroup(title, pages)];
        var markdown = new List<string> { $"# {title}", string.Empty, description, string.Empty };
        var toc = new List<string> { $"- name: {ToYamlString(title)}", "  href: index.md", "  items:" };

        foreach (var group in groups)
        {
            if (group.Pages.Length == 0)
            {
                continue;
            }

            if (separateServerChangelog)
            {
                markdown.Add($"## {group.Name}");
                markdown.Add(string.Empty);
                toc.Add($"    - name: {ToYamlString(group.Name)}");
                toc.Add("      items:");
            }

            var releaseGroups = separateServerChangelog
                ? CreateReleaseGroups(group.Pages)
                : [new ReleaseGroup(string.Empty, default, group.Pages)];
            foreach (var releaseGroup in releaseGroups)
            {
                if (separateServerChangelog)
                {
                    markdown.Add($"### {releaseGroup.Name}");
                    markdown.Add(string.Empty);
                    toc.Add($"        - name: {ToYamlString(releaseGroup.Name)}");
                    toc.Add("          items:");
                }

                var orderedPages = string.Equals(title, "Changelog", StringComparison.Ordinal)
                    ? releaseGroup.Pages.OrderByDescending(static page => VersionOrder.Parse(page.FileName))
                        .ThenBy(static page => page.RelativePath, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static page => page.RelativePath, StringComparer.Ordinal)
                    : releaseGroup.Pages.OrderBy(static page => page.Title, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static page => page.RelativePath, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(static page => page.RelativePath, StringComparer.Ordinal);

                foreach (var page in orderedPages)
                {
                    markdown.Add($"- [{page.Title}]({page.EncodedPath})");
                    var indent = separateServerChangelog ? "            " : "    ";
                    toc.Add($"{indent}- name: {ToYamlString(page.Title)}");
                    toc.Add($"{indent}  href: {ToYamlString(page.RelativePath)}");
                }
            }
            markdown.Add(string.Empty);
        }

        WriteLines(indexPath, markdown);
        WriteLines(tocPath, toc);
    }

    public void GenerateSectionTocs(string contentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);
        var contentRoot = Path.GetFullPath(contentDirectory);
        if (!Directory.Exists(contentRoot))
        {
            throw new DirectoryNotFoundException($"Staged documentation directory not found: {contentRoot}");
        }

        var directories = Directory.EnumerateDirectories(contentRoot, "*", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (var directory in directories)
        {
            var relative = Path.GetRelativePath(contentRoot, directory).Replace('\\', '/');
            if (IsOwnedCatalogue(relative)) continue;

            var tocPath = Path.Combine(directory, "toc.yml");
            if (File.Exists(tocPath) || Directory.Exists(tocPath)) continue;
            if (!HasMarkdownContent(directory)) continue;

            WriteSectionToc(directory, tocPath);
        }
    }

    private static bool IsOwnedCatalogue(string relativeDirectory)
    {
        return relativeDirectory.Equals("api", StringComparison.OrdinalIgnoreCase) ||
            relativeDirectory.StartsWith("api/", StringComparison.OrdinalIgnoreCase) ||
            relativeDirectory.Equals("benchmarks", StringComparison.OrdinalIgnoreCase) ||
            relativeDirectory.StartsWith("benchmarks/", StringComparison.OrdinalIgnoreCase) ||
            relativeDirectory.Equals("changelog", StringComparison.OrdinalIgnoreCase) ||
            relativeDirectory.StartsWith("changelog/", StringComparison.OrdinalIgnoreCase) ||
            relativeDirectory.Equals("articles/ADRs", StringComparison.OrdinalIgnoreCase) ||
            relativeDirectory.StartsWith("articles/ADRs/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasMarkdownContent(string directory)
    {
        if (Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly).Any()) return true;
        return Directory.EnumerateDirectories(directory).Any(HasMarkdownContent);
    }

    private static void WriteSectionToc(string directory, string tocPath)
    {
        var indexPath = Path.Combine(directory, "index.md");
        var rootTitle = File.Exists(indexPath) ? ReadTitle(indexPath) : Humanise(Path.GetFileName(directory));
        var lines = new List<string>();
        if (File.Exists(indexPath))
        {
            lines.Add($"- name: {ToYamlString(rootTitle)}");
            lines.Add("  href: index.md");
            if (HasTocChildren(directory))
            {
                lines.Add("  items:");
                WriteTocChildren(lines, directory, directory, directory, 4);
            }
        }
        else
        {
            WriteTocChildren(lines, directory, directory, directory, 0);
        }

        if (lines.Count == 0) return;
        WriteLines(tocPath, lines);
    }

    private static bool HasTocChildren(string directory)
    {
        return Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
                .Any(path => !string.Equals(Path.GetFileName(path), "index.md", StringComparison.OrdinalIgnoreCase)) ||
            Directory.EnumerateDirectories(directory).Any(HasMarkdownContent);
    }

    private static void WriteTocChildren(List<string> lines, string contentRoot, string baseDirectory,
        string directory, int indentation)
    {
        var files = Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
            .Where(path => !string.Equals(Path.GetFileName(path), "index.md", StringComparison.OrdinalIgnoreCase))
            .Select(path => new TocPage(Path.GetRelativePath(baseDirectory, path).Replace('\\', '/'), ReadTitle(path), Path.GetFileName(path)))
            .OrderBy(static page => page, TocPageComparer.Instance)
            .ToArray();
        foreach (var page in files)
        {
            var prefix = new string(' ', indentation);
            lines.Add($"{prefix}- name: {ToYamlString(page.Title)}");
            lines.Add($"{prefix}  href: {ToYamlString(page.RelativePath)}");
        }

        foreach (var child in Directory.EnumerateDirectories(directory)
            .Where(HasMarkdownContent)
            .Where(path => !IsOwnedCatalogue(Path.GetRelativePath(contentRoot, path).Replace('\\', '/')))
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static path => path, StringComparer.Ordinal))
        {
            var relative = Path.GetRelativePath(baseDirectory, child).Replace('\\', '/');
            var childIndex = Path.Combine(child, "index.md");
            var childTitle = File.Exists(childIndex) ? ReadTitle(childIndex) : Humanise(Path.GetFileName(child));
            var prefix = new string(' ', indentation);
            lines.Add($"{prefix}- name: {ToYamlString(childTitle)}");
            if (File.Exists(childIndex)) lines.Add($"{prefix}  href: {ToYamlString(relative + "/index.md")}");

            if (HasTocChildren(child))
            {
                lines.Add($"{prefix}  items:");
                WriteTocChildren(lines, contentRoot, baseDirectory, child, indentation + 4);
            }
        }
    }

    private static string ReadTitle(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var match = HeadingPattern.Match(line.TrimStart('\uFEFF'));
            if (match.Success) return match.Groups[1].Value.Trim();
        }
        return Humanise(Path.GetFileNameWithoutExtension(path));
    }

    private static string Humanise(string value)
    {
        var words = Regex.Replace(value.Replace('-', ' ').Replace('_', ' '), "\\s+", " ", RegexOptions.CultureInvariant).Trim();
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words.ToLowerInvariant());
    }

    private static CatalogGroup[] CreateChangelogGroups(IReadOnlyList<CatalogPage> pages)
    {
        var leanCorpusPages = pages.Where(static page =>
            !page.RelativePath.StartsWith("server/", StringComparison.OrdinalIgnoreCase) &&
            !page.RelativePath.StartsWith("devops/", StringComparison.OrdinalIgnoreCase)).ToArray();
        var serverPages = pages.Where(static page => page.RelativePath.StartsWith("server/", StringComparison.OrdinalIgnoreCase)).ToArray();
        var devOpsPages = pages.Where(static page => page.RelativePath.StartsWith("devops/", StringComparison.OrdinalIgnoreCase)).ToArray();
        return
        [
            new CatalogGroup("LeanCorpus and Rowles.Text", leanCorpusPages),
            new CatalogGroup("Community Server", serverPages),
            new CatalogGroup("LeanCorpus DevOps", devOpsPages)
        ];
    }

    private static ReleaseGroup[] CreateReleaseGroups(IReadOnlyList<CatalogPage> pages)
    {
        return pages.GroupBy(static page => VersionOrder.Parse(page.FileName))
            .Select(static group => new ReleaseGroup(
                group.Key.Kind switch
                {
                    2 => "Unreleased",
                    1 => $"{group.Key.Major}.x",
                    _ => "Other"
                },
                group.Key,
                group.ToArray()))
            .OrderByDescending(static group => group.Order)
            .ThenBy(static group => group.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CatalogPage CreatePage(string root, string path)
    {
        var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
        var heading = File.ReadLines(path).Select(line => HeadingPattern.Match(line)).FirstOrDefault(static match => match.Success);
        var title = heading is { Success: true } ? heading.Groups[1].Value.Trim() : Path.GetFileNameWithoutExtension(path);
        var encodedPath = string.Join('/', relative.Split('/').Select(Uri.EscapeDataString));
        return new CatalogPage(relative, encodedPath, title, Path.GetFileName(path));
    }

    private static string ToYamlString(string value) => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    private static void WriteLines(string path, IEnumerable<string> lines)
        => File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private sealed record CatalogPage(string RelativePath, string EncodedPath, string Title, string FileName);

    private sealed record TocPage(string RelativePath, string Title, string FileName);

    private sealed class TocPageComparer : IComparer<TocPage>
    {
        public static TocPageComparer Instance { get; } = new();

        public int Compare(TocPage? left, TocPage? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;

            var leftOrder = ParseLeadingNumber(left.FileName);
            var rightOrder = ParseLeadingNumber(right.FileName);
            var comparison = leftOrder.CompareTo(rightOrder);
            if (comparison != 0) return comparison;
            comparison = StringComparer.OrdinalIgnoreCase.Compare(left.FileName, right.FileName);
            return comparison != 0 ? comparison : StringComparer.Ordinal.Compare(left.FileName, right.FileName);
        }

        private static int ParseLeadingNumber(string fileName)
        {
            var match = Regex.Match(fileName, "^(?<number>\\d+)", RegexOptions.CultureInvariant);
            return match.Success && int.TryParse(match.Groups["number"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var value) ? value : int.MaxValue;
        }
    }

    private sealed record CatalogGroup(string Name, CatalogPage[] Pages);

    private sealed record ReleaseGroup(string Name, VersionOrder Order, CatalogPage[] Pages);

    private readonly record struct VersionOrder(int Kind, int Major, int Minor, int Patch) : IComparable<VersionOrder>
    {
        public static VersionOrder Parse(string fileName)
        {
            if (string.Equals(fileName, "unreleased.md", StringComparison.OrdinalIgnoreCase))
            {
                return new VersionOrder(2, 0, 0, 0);
            }

            var match = VersionFilePattern.Match(fileName);
            return match.Success &&
                int.TryParse(match.Groups["major"].Value, CultureInfo.InvariantCulture, out var major) &&
                int.TryParse(match.Groups["minor"].Value, CultureInfo.InvariantCulture, out var minor) &&
                int.TryParse(match.Groups["patch"].Value, CultureInfo.InvariantCulture, out var patch)
                    ? new VersionOrder(1, major, minor, patch)
                    : default;
        }

        public int CompareTo(VersionOrder other)
        {
            var comparison = Kind.CompareTo(other.Kind);
            if (comparison != 0) return comparison;
            comparison = Major.CompareTo(other.Major);
            if (comparison != 0) return comparison;
            comparison = Minor.CompareTo(other.Minor);
            return comparison != 0 ? comparison : Patch.CompareTo(other.Patch);
        }
    }
}
