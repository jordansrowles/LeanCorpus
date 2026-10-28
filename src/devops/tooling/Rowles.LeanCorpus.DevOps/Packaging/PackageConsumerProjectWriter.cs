using System.Xml.Linq;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed record PackageConsumerProjectFiles(string ProjectPath, string ProgramPath);

internal static class PackageConsumerProjectWriter
{
    private const string AssetResourcePrefix = "Rowles.LeanCorpus.DevOps.Packaging.Assets.";

    public static PackageConsumerProjectFiles Write(
        string repositoryRoot,
        string scenarioDirectory,
        ConsumerScenario scenario,
        PackageManifestIndex packageIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(scenarioDirectory);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(packageIndex);

        var references = BuildPackageReferences(repositoryRoot, scenario, packageIndex);
        var projectXml = ReadEmbeddedAsset("Consumer.csproj.template")
            .Replace("SDK_NAME", scenario.Sdk, StringComparison.Ordinal)
            .Replace("FRAMEWORK", scenario.Framework, StringComparison.Ordinal)
            .Replace("EXTRA_PROPERTIES", scenario.ExtraProperties, StringComparison.Ordinal)
            .Replace("PACKAGE_REFERENCES", references, StringComparison.Ordinal);
        if (projectXml.Contains("<ProjectReference", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Generated consumer must not use ProjectReference.");
        }
        if (scenario.Kind == "sourcegen" &&
            (!projectXml.Contains("OutputItemType=\"Analyzer\"", StringComparison.Ordinal) ||
             !projectXml.Contains("ReferenceOutputAssembly=\"false\"", StringComparison.Ordinal)))
        {
            throw new InvalidDataException("SourceGen consumer must reference the generator as a private analyser dependency.");
        }

        var source = ReadScenarioSource(scenario, packageIndex);
        Directory.CreateDirectory(scenarioDirectory);
        var projectPath = Path.Combine(scenarioDirectory, "Consumer.csproj");
        var programPath = Path.Combine(scenarioDirectory, "Program.cs");
        File.WriteAllText(projectPath, projectXml, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.WriteAllText(programPath, source, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        if (scenario.Kind == "studio")
        {
            var pages = Path.Combine(scenarioDirectory, "Pages");
            Directory.CreateDirectory(pages);
            File.WriteAllText(Path.Combine(pages, "Consumer.cshtml"), "@page \"/razor-smoke\"\n<h1>Razor package consumer</h1>\n",
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return new PackageConsumerProjectFiles(projectPath, programPath);
    }

    private static string BuildPackageReferences(string repositoryRoot, ConsumerScenario scenario, PackageManifestIndex packageIndex)
    {
        var nodes = new List<string>();
        foreach (var id in scenario.PackageIds)
        {
            if (!packageIndex.Packages.TryGetValue(id, out var package))
            {
                throw new InvalidDataException($"Package '{id}' is absent from the generated package manifest.");
            }
            var escapedId = System.Security.SecurityElement.Escape(id);
            var escapedVersion = System.Security.SecurityElement.Escape(package.Version);
            if (id.Equals(scenario.AnalyzerPackageId, StringComparison.Ordinal))
            {
                nodes.Add($"    <PackageReference Include=\"{escapedId}\" Version=\"[{escapedVersion}]\" OutputItemType=\"Analyzer\" ReferenceOutputAssembly=\"false\" PrivateAssets=\"all\" />");
            }
            else
            {
                nodes.Add($"    <PackageReference Include=\"{escapedId}\" Version=\"[{escapedVersion}]\" />");
            }
        }
        foreach (var id in scenario.ExternalReferences)
        {
            var version = ReadCentralPackageVersion(repositoryRoot, id);
            nodes.Add($"    <PackageReference Include=\"{System.Security.SecurityElement.Escape(id)}\" Version=\"[{System.Security.SecurityElement.Escape(version)}]\" />");
        }
        return string.Join(Environment.NewLine, nodes);
    }

    private static string ReadCentralPackageVersion(string repositoryRoot, string packageId)
    {
        var path = Path.Combine(repositoryRoot, "Directory.Packages.props");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Central package versions are missing.", path);
        }
        var document = XDocument.Load(path);
        foreach (var element in document.Descendants().Where(static element => element.Name.LocalName == "PackageVersion"))
        {
            var include = (string?)element.Attribute("Include");
            if (!string.Equals(include, packageId, StringComparison.Ordinal))
            {
                continue;
            }
            var version = (string?)element.Attribute("Version") ?? element.Elements().FirstOrDefault(static child => child.Name.LocalName == "Version")?.Value;
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }
        throw new InvalidDataException($"No centrally managed external package version is declared for {packageId}.");
    }

    private static string ReadScenarioSource(ConsumerScenario scenario, PackageManifestIndex packageIndex)
    {
        if (scenario.RuntimeKind != "compression")
        {
            return ReadEmbeddedAsset($"ConsumerSources.{scenario.Kind}.cs.txt");
        }

        var codec = scenario.PackageId.Split('.').Last();
        if (!packageIndex.Packages.TryGetValue(scenario.CorePackageId, out var core) || !packageIndex.Packages.TryGetValue(scenario.PackageId, out var optional))
        {
            throw new InvalidDataException($"Compression consumer is missing package manifest metadata for Core or {scenario.PackageId}.");
        }
        var coreAssemblyVersion = GetAssemblyVersion(core.Version);
        var optionalAssemblyVersion = GetAssemblyVersion(optional.Version);
        var registration = codec == "LZ4" ? "Lz4Compression" : codec + "Compression";
        var policy = codec == "LZ4" ? "Lz4" : codec;
        return ReadEmbeddedAsset("Program.cs.template")
            .Replace("CORE_ASSEMBLY_VERSION", coreAssemblyVersion, StringComparison.Ordinal)
            .Replace("OPTIONAL_ASSEMBLY_VERSION", optionalAssemblyVersion, StringComparison.Ordinal)
            .Replace("CODEC", codec, StringComparison.Ordinal)
            .Replace("REGISTRATION", registration, StringComparison.Ordinal)
            .Replace("POLICY", policy, StringComparison.Ordinal);
    }

    private static string ReadEmbeddedAsset(string assetName)
    {
        var resourceName = AssetResourcePrefix + assetName;
        using var stream = typeof(PackageConsumerProjectWriter).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Managed package consumer asset '{assetName}' is missing from the DevOps host.", resourceName);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string GetAssemblyVersion(string packageVersion)
    {
        if (!Version.TryParse(packageVersion, out var version))
        {
            throw new InvalidDataException($"Consumer package version '{packageVersion}' is not a numeric assembly version.");
        }
        return version + ".0";
    }
}
