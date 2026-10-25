using System.Xml.Linq;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal static class PackageConsumerNuGetConfig
{
    public static void Write(
        string path,
        string packageDirectory,
        IReadOnlyList<string> externalSources,
        PackagePolicy policy,
        bool allowExternalCore = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentNullException.ThrowIfNull(externalSources);
        ArgumentNullException.ThrowIfNull(policy);
        if (externalSources.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("NuGet consumer feeds must be non-empty.");
        }
        if (!policy.Packages.Any(entry => entry.PackageId.Equals(policy.CompressionContract.CorePackageId, StringComparison.Ordinal)))
        {
            throw new InvalidDataException($"Package policy is missing contract Core package '{policy.CompressionContract.CorePackageId}'.");
        }

        var fullPackageDirectory = Path.GetFullPath(packageDirectory);
        var packageSources = new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "local"), new XAttribute("value", fullPackageDirectory)));
        var mappings = new XElement("packageSourceMapping");
        var localMapping = new XElement("packageSource", new XAttribute("key", "local"));
        foreach (var entry in policy.Packages)
        {
            localMapping.Add(new XElement("package", new XAttribute("pattern", entry.PackageId)));
        }
        mappings.Add(localMapping);

        for (var index = 0; index < externalSources.Count; index++)
        {
            var key = $"feed{index}";
            packageSources.Add(new XElement("add", new XAttribute("key", key), new XAttribute("value", externalSources[index])));
            var mapping = new XElement("packageSource", new XAttribute("key", key),
                new XElement("package", new XAttribute("pattern", "*")));
            if (allowExternalCore)
            {
                mapping.Add(new XElement("package", new XAttribute("pattern", policy.CompressionContract.CorePackageId)));
            }
            mappings.Add(mapping);
        }

        var document = new XDocument(new XElement("configuration", packageSources, mappings));
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        using var writer = new StreamWriter(path, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        document.Save(writer, SaveOptions.None);
    }
}
