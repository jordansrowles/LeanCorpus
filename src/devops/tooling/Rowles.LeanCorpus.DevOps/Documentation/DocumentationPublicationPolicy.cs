namespace Rowles.LeanCorpus.DevOps.Documentation;

internal static class DocumentationPublicationPolicy
{
    private static readonly string[] MaintainerOnlyFiles =
    [
        "articles/ADRs/_template.md"
    ];

    private static readonly string[] MaintainerOnlyDirectories =
    [
        "contributors/devops",
        "server/evidence"
    ];

    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static bool IsMaintainerOnly(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var path = relativePath.Replace('\\', '/').TrimStart('/');
        if (MaintainerOnlyFiles.Any(file => string.Equals(path, file, PathComparison)))
        {
            return true;
        }

        return MaintainerOnlyDirectories.Any(directory =>
            string.Equals(path, directory, PathComparison) ||
            path.StartsWith(directory + "/", PathComparison));
    }

    public static void ValidateOutput(string contentRoot, string siteRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(siteRoot);
        var content = Path.GetFullPath(contentRoot);
        var site = Path.GetFullPath(siteRoot);

        foreach (var relativePath in MaintainerOnlyFiles)
        {
            RequireMissing(Path.Combine(content, relativePath.Replace('/', Path.DirectorySeparatorChar)), relativePath);
            var sitePage = Path.ChangeExtension(Path.Combine(site, relativePath.Replace('/', Path.DirectorySeparatorChar)), ".html");
            RequireMissing(sitePage, relativePath);
        }

        foreach (var relativePath in MaintainerOnlyDirectories)
        {
            RequireMissing(Path.Combine(content, relativePath.Replace('/', Path.DirectorySeparatorChar)), relativePath);
            RequireMissing(Path.Combine(site, relativePath.Replace('/', Path.DirectorySeparatorChar)), relativePath);
        }
    }

    private static void RequireMissing(string path, string relativePath)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new InvalidDataException($"Maintainer-only documentation was included in public output: {relativePath}");
        }
    }
}
