using System.Xml.Linq;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal static class PackageConsumerNuGetConfig
{
    private static readonly string[] InternalPatterns = ["LeanCorpus*", "Rowles.Text", "Rowles.LeanCorpus.*"];

    public static void Write(string path, string packageDirectory, IReadOnlyList<string> externalSources, bool allowExternalLeanCorpus = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentNullException.ThrowIfNull(externalSources);
        if (externalSources.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("NuGet consumer feeds must be non-empty.");
        }

        var fullPackageDirectory = Path.GetFullPath(packageDirectory);
        var packageSources = new XElement("packageSources", new XElement("clear"),
            new XElement("add", new XAttribute("key", "local"), new XAttribute("value", fullPackageDirectory)));
        var mappings = new XElement("packageSourceMapping");
        var localMapping = new XElement("packageSource", new XAttribute("key", "local"));
        if (allowExternalLeanCorpus)
        {
            localMapping.Add(new XElement("package", new XAttribute("pattern", "LeanCorpus.Compression.*")));
        }
        foreach (var pattern in InternalPatterns)
        {
            localMapping.Add(new XElement("package", new XAttribute("pattern", pattern)));
        }
        mappings.Add(localMapping);

        for (var index = 0; index < externalSources.Count; index++)
        {
            var key = $"feed{index}";
            packageSources.Add(new XElement("add", new XAttribute("key", key), new XAttribute("value", externalSources[index])));
            var mapping = new XElement("packageSource", new XAttribute("key", key),
                new XElement("package", new XAttribute("pattern", "*")));
            if (allowExternalLeanCorpus)
            {
                mapping.Add(new XElement("package", new XAttribute("pattern", "LeanCorpus")));
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
