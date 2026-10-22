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

            var orderedPages = string.Equals(title, "Changelog", StringComparison.Ordinal)
                ? group.Pages.OrderByDescending(static page => VersionOrder.Parse(page.FileName))
                    .ThenBy(static page => page.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static page => page.RelativePath, StringComparer.Ordinal)
                : group.Pages.OrderBy(static page => page.Title, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(static page => page.RelativePath, StringComparer.CurrentCultureIgnoreCase)
                    .ThenBy(static page => page.RelativePath, StringComparer.Ordinal);

            foreach (var page in orderedPages)
            {
                markdown.Add($"- [{page.Title}]({page.EncodedPath})");
                var indent = separateServerChangelog ? "        " : "    ";
                toc.Add($"{indent}- name: {ToYamlString(page.Title)}");
                toc.Add($"{indent}  href: {ToYamlString(page.RelativePath)}");
            }
            markdown.Add(string.Empty);
        }

        WriteLines(indexPath, markdown);
        WriteLines(tocPath, toc);
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

    private sealed record CatalogGroup(string Name, CatalogPage[] Pages);

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
