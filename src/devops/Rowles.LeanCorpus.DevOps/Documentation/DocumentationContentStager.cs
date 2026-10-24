using System.Text;
using System.Text.RegularExpressions;

namespace Rowles.LeanCorpus.DevOps.Documentation;

internal sealed class DocumentationContentStager
{
    private static readonly Regex FrontMatterPattern = new("\\A---\\r?\\n(?<yaml>[\\s\\S]*?)\\r?\\n---(?=\\r?\\n|$)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex DocUrlPattern = new("(?m)^docurl\\s*:.*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public void CopyCanonicalContent(string sourceDirectory, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        var source = Path.GetFullPath(sourceDirectory);
        var destination = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"Canonical documentation directory not found: {source}");
        }

        ResetDirectory(destination);
        CopyDirectoryContents(source, source, destination);
        DocumentationPublicationPolicy.ValidateOutput(destination, destination);
    }

    public void CopyChangelog(string repositoryRoot, string contentDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);
        var sourceRoot = Path.Combine(Path.GetFullPath(repositoryRoot), "changelog");
        if (!Directory.Exists(sourceRoot))
        {
            return;
        }

        var destinationRoot = Path.Combine(Path.GetFullPath(contentDirectory), "changelog");
        Directory.CreateDirectory(destinationRoot);
        foreach (var existing in Directory.EnumerateFiles(destinationRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => !string.Equals(Path.GetFileName(path), "index.md", StringComparison.OrdinalIgnoreCase)))
        {
            File.Delete(existing);
        }

        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is not ("_template.md" or "_vnext.md")))
        {
            var relative = Path.GetRelativePath(sourceRoot, source);
            var destination = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }
    }

    public void SetContributionMetadata(string repositoryRoot, string contentDirectory, string manifestPath, string commit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(commit);
        if (!Regex.IsMatch(commit, "^[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant))
        {
            throw new InvalidDataException("Documentation contribution metadata requires a full 40-character source commit.");
        }

        var repository = Path.GetFullPath(repositoryRoot);
        var contentRoot = Path.GetFullPath(contentDirectory);
        var manifest = RepositoryDocumentationImporter.ReadImports(manifestPath);
        var importsByDestination = manifest.ToDictionary(
            static entry => entry.Destination.Replace('\\', '/'),
            static entry => entry.Source.Replace('\\', '/'),
            PathComparer);
        var commitLower = commit.ToLowerInvariant();

        foreach (var path in Directory.EnumerateFiles(contentRoot, "*.md", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(contentRoot, path).Replace('\\', '/');
            var sourceRelative = MapSourcePath(relative, importsByDestination);
            var sourcePath = Path.GetFullPath(Path.Combine(repository, sourceRelative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsContained(repository, sourcePath) || !File.Exists(sourcePath))
            {
                throw new InvalidDataException($"Cannot map staged documentation page '{relative}' to a repository source file: {sourceRelative}");
            }

            var encodedPath = string.Join('/', sourceRelative.Split('/').Select(Uri.EscapeDataString));
            var docUrl = RepositoryWebLinks.BlobUrl(commitLower, encodedPath);
            var content = File.ReadAllText(path);
            var frontMatter = FrontMatterPattern.Match(content);
            string rewritten;
            if (frontMatter.Success)
            {
                var yaml = frontMatter.Groups["yaml"].Value;
                yaml = DocUrlPattern.IsMatch(yaml)
                    ? DocUrlPattern.Replace(yaml, $"docurl: '{docUrl}'")
                    : $"{yaml}\ndocurl: '{docUrl}'";
                var replacement = $"---\n{yaml}\n---";
                rewritten = content[..frontMatter.Index] + replacement + content[(frontMatter.Index + frontMatter.Length)..];
            }
            else
            {
                rewritten = $"---\ndocurl: '{docUrl}'\n---\n\n{content}";
            }

            File.WriteAllText(path, rewritten, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static string MapSourcePath(string relative, IReadOnlyDictionary<string, string> importsByDestination)
    {
        if (importsByDestination.TryGetValue(relative, out var importedSource))
        {
            return importedSource;
        }
        if (string.Equals(relative, "articles/ADRs/index.md", StringComparison.Ordinal))
        {
            return "src/devops/Rowles.LeanCorpus.DevOps/Documentation/AdrIndexGenerator.cs";
        }
        if (string.Equals(relative, "changelog/index.md", StringComparison.Ordinal))
        {
            return "src/devops/Rowles.LeanCorpus.DevOps/Documentation/DocumentationSectionCatalogWriter.cs";
        }
        if (relative.StartsWith("changelog/", StringComparison.OrdinalIgnoreCase))
        {
            return "changelog/" + relative["changelog/".Length..];
        }
        if (string.Equals(relative, "benchmarks/qualification-audit.md", StringComparison.OrdinalIgnoreCase))
        {
            return "docs/content/benchmarks/qualification-audit.md";
        }
        if (relative.StartsWith("benchmarks/", StringComparison.OrdinalIgnoreCase))
        {
            return "src/devops/Rowles.LeanCorpus.DevOps/Documentation/BenchmarkDocumentationGenerator.cs";
        }
        return "docs/content/" + relative;
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Documentation staging refuses to reset a linked directory: {path}");
            }
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"Documentation staging refuses to delete a linked path: {entry}");
                }
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }
        else
        {
            Directory.CreateDirectory(path);
        }
    }

    private static void CopyDirectoryContents(string sourceRoot, string source, string destination)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(source))
        {
            var relativePath = Path.GetRelativePath(sourceRoot, entry);
            if (DocumentationPublicationPolicy.IsMaintainerOnly(relativePath))
            {
                continue;
            }

            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException($"Documentation source contains a linked path that cannot be staged safely: {entry}");
            }

            var target = Path.Combine(destination, Path.GetFileName(entry));
            if ((attributes & FileAttributes.Directory) != 0)
            {
                Directory.CreateDirectory(target);
                CopyDirectoryContents(sourceRoot, entry, target);
            }
            else
            {
                File.Copy(entry, target, overwrite: false);
            }
        }
    }

    private static bool IsContained(string root, string candidate)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var fullCandidate = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, comparison);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
