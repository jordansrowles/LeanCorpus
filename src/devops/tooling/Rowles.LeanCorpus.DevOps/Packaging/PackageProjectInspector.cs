using System.Xml.Linq;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal static class PackageProjectInspector
{
    public static PackageProjectMetadata ReadMetadata(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var fullPath = Path.GetFullPath(projectPath);
        var project = LoadProject(fullPath);
        var properties = project.Root?.Elements().Where(static element => element.Name.LocalName == "PropertyGroup")
            .SelectMany(static group => group.Elements())
            .GroupBy(static element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Last().Value, StringComparer.Ordinal)
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

        properties.TryGetValue("PackageId", out var packageId);
        properties.TryGetValue("Version", out var version);
        properties.TryGetValue("AssemblyName", out var assemblyName);
        properties.TryGetValue("PackageReadmeFile", out var readme);
        properties.TryGetValue("PackageLicenseFile", out var licenseFile);
        properties.TryGetValue("RepositoryUrl", out var repositoryUrl);

        return new PackageProjectMetadata(
            packageId ?? string.Empty,
            version ?? string.Empty,
            string.IsNullOrWhiteSpace(assemblyName) ? Path.GetFileNameWithoutExtension(fullPath) : assemblyName,
            NullIfWhiteSpace(readme),
            NullIfWhiteSpace(licenseFile),
            NullIfWhiteSpace(repositoryUrl));
    }

    public static IReadOnlyList<string> ReadDependencyIds(string projectPath, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var projectPathFull = Path.GetFullPath(projectPath);
        EnsureWithinRepository(projectPathFull, root);
        var project = LoadProject(projectPathFull);
        var dependencies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reference in project.Descendants().Where(static element => element.Name.LocalName == "PackageReference"))
        {
            var include = Attribute(reference, "Include");
            if (string.IsNullOrWhiteSpace(include))
            {
                include = Attribute(reference, "Update");
            }
            var privateAssets = Attribute(reference, "PrivateAssets") ?? ChildValue(reference, "PrivateAssets");
            var excludeAssets = Attribute(reference, "ExcludeAssets") ?? ChildValue(reference, "ExcludeAssets");
            if (string.IsNullOrWhiteSpace(include) || ContainsAll(privateAssets) || ContainsAll(excludeAssets))
            {
                continue;
            }
            foreach (var id in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                dependencies.Add(id);
            }
        }

        var projectDirectory = Path.GetDirectoryName(projectPathFull)!;
        foreach (var reference in project.Descendants().Where(static element => element.Name.LocalName == "ProjectReference"))
        {
            var include = Attribute(reference, "Include");
            if (string.IsNullOrWhiteSpace(include))
            {
                continue;
            }
            foreach (var relativePath in include.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var referencedPath = Path.GetFullPath(Path.Combine(projectDirectory, relativePath.Replace('\\', Path.DirectorySeparatorChar)));
                EnsureWithinRepository(referencedPath, root);
                if (!File.Exists(referencedPath))
                {
                    throw new InvalidDataException($"Project reference is missing while validating dependencies: {referencedPath}");
                }
                var referenced = ReadMetadata(referencedPath);
                dependencies.Add(string.IsNullOrWhiteSpace(referenced.PackageId)
                    ? Path.GetFileNameWithoutExtension(referencedPath)
                    : referenced.PackageId);
            }
        }

        return dependencies.Order(StringComparer.Ordinal).ToArray();
    }

    private static XDocument LoadProject(string projectPath)
    {
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("Package project was not found.", projectPath);
        }
        try
        {
            return XDocument.Load(projectPath, LoadOptions.None);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new InvalidDataException($"Package project is not valid XML: {projectPath}", exception);
        }
    }

    private static void EnsureWithinRepository(string path, string repositoryRoot)
    {
        var relative = Path.GetRelativePath(repositoryRoot, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Package project reference resolves outside the selected checkout: '{path}'.");
        }
    }

    private static string? Attribute(XElement element, string name)
        => element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value;

    private static string? ChildValue(XElement element, string name)
        => element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value;

    private static bool ContainsAll(string? value)
        => value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(static token => token.Equals("all", StringComparison.OrdinalIgnoreCase)) == true;

    private static string? NullIfWhiteSpace(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}
