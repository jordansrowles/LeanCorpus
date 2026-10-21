using System.IO.Compression;
using System.Xml.Linq;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed record PackageProjectMetadata(
    string PackageId,
    string Version,
    string AssemblyName,
    string? Readme,
    string? LicenseFile,
    string? RepositoryUrl);

internal static class PackageArchiveInspector
{
    private const string CompressionCoreVersionRange = "[4.0.0,5.0.0)";
    private static readonly HashSet<string> CompressionPackageIds = new(StringComparer.Ordinal)
    {
        "LeanCorpus.Compression.LZ4", "LeanCorpus.Compression.Snappy", "LeanCorpus.Compression.Zstandard"
    };
    private static readonly HashSet<string> StandardEntries = new(StringComparer.Ordinal)
    {
        "[Content_Types].xml", "README.md", "LICENCE", "LICENSE"
    };

    public static void Validate(
        string packagePath,
        string? symbolPath,
        PackagePolicyEntry policy,
        PackageProjectMetadata project,
        IReadOnlyCollection<string> projectDependencyIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(project);

        var archive = ReadArchive(packagePath);
        var packageId = policy.PackageId;
        if (!string.Equals(archive.Id, packageId, StringComparison.Ordinal) ||
            !string.Equals(archive.Version, project.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected package identity/version in {packagePath}; expected {packageId} {project.Version}.");
        }
        if (!string.Equals(project.PackageId, packageId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Package policy ID '{packageId}' does not match project ID '{project.PackageId}'.");
        }

        ValidateDependencies(archive, policy, projectDependencyIds, packagePath);
        ValidateAssets(archive, policy, project, packagePath);
        ValidateSymbols(symbolPath, policy, project, archive, packagePath);
    }

    public static void ValidateCompressionDependencyRange(string packagePath, PackagePolicyEntry policy)
    {
        if (!CompressionPackageIds.Contains(policy.PackageId))
        {
            throw new InvalidDataException($"Package {policy.PackageId} is not covered by the compression dependency contract.");
        }

        var archive = ReadArchive(packagePath);
        if (!string.Equals(archive.Id, policy.PackageId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Compression package archive ID '{archive.Id}' does not match {policy.PackageId} in {packagePath}.");
        }

        var expectedFrameworks = (policy.ExpectedDependencyFrameworks.Count > 0
                ? policy.ExpectedDependencyFrameworks
                : policy.ExpectedFrameworks)
            .Select(static framework => framework.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (expectedFrameworks.Length == 0)
        {
            throw new InvalidDataException($"Compression package {policy.PackageId} has no expected dependency frameworks.");
        }

        if (archive.DependencyGroups.Count != expectedFrameworks.Length)
        {
            throw new InvalidDataException($"Compression package {policy.PackageId} must declare dependency groups for '{string.Join(',', expectedFrameworks)}'; found {archive.DependencyGroups.Count} groups in {packagePath}.");
        }

        foreach (var framework in expectedFrameworks)
        {
            var groups = archive.DependencyGroups.Where(group => string.Equals(ToShortFrameworkName(group.TargetFramework), framework, StringComparison.Ordinal)).ToArray();
            if (groups.Length != 1)
            {
                throw new InvalidDataException($"Compression package {policy.PackageId} must declare exactly one '{framework}' dependency group in {packagePath}.");
            }

            var coreDependencies = groups[0].Dependencies.Where(static dependency => dependency.Id == "LeanCorpus").ToArray();
            if (coreDependencies.Length != 1 || !string.Equals(StripWhitespace(coreDependencies[0].Version), CompressionCoreVersionRange, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Compression package {policy.PackageId} must declare LeanCorpus {CompressionCoreVersionRange} in its '{framework}' dependency group.");
            }
        }
    }

    private static void ValidateDependencies(
        ArchiveInfo archive,
        PackagePolicyEntry policy,
        IReadOnlyCollection<string> projectDependencyIds,
        string packagePath)
    {
        if (archive.DependencyGroups.Count == 0)
        {
            if (projectDependencyIds.Count > 0)
            {
                throw new InvalidDataException($"Project declares dependencies but its nuspec has no dependency groups in {packagePath}.");
            }
            return;
        }

        var expectedFrameworks = policy.ExpectedDependencyFrameworks.Count > 0
            ? policy.ExpectedDependencyFrameworks
            : policy.ExpectedFrameworks;
        var expected = expectedFrameworks.Select(static value => value.ToLowerInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var actual = archive.DependencyGroups.Select(group => ToShortFrameworkName(group.TargetFramework)).Order(StringComparer.Ordinal).ToArray();
        if (archive.DependencyGroups.Any(static group => string.IsNullOrWhiteSpace(group.TargetFramework)) ||
            !actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"Unexpected dependency framework groups in {packagePath}. Expected '{string.Join(',', expected)}', found '{string.Join(',', actual)}'.");
        }

        var expectedIds = projectDependencyIds.Order(StringComparer.Ordinal).ToArray();
        foreach (var group in archive.DependencyGroups)
        {
            if (group.Dependencies.Any(static dependency => string.IsNullOrWhiteSpace(dependency.Id) || string.IsNullOrWhiteSpace(dependency.Version)))
            {
                throw new InvalidDataException($"Dependency is missing an ID or version in {packagePath}.");
            }
            var actualIds = group.Dependencies.Select(static dependency => dependency.Id).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (!actualIds.SequenceEqual(expectedIds, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Unexpected dependency IDs for '{group.TargetFramework}' in {packagePath}. Expected '{string.Join(',', expectedIds)}', found '{string.Join(',', actualIds)}'.");
            }
        }
    }

    private static void ValidateAssets(ArchiveInfo archive, PackagePolicyEntry policy, PackageProjectMetadata project, string packagePath)
    {
        var entries = archive.Entries;
        var nuspecPath = policy.PackageId + ".nuspec";
        if (!entries.Contains(nuspecPath, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"Expected package nuspec '{nuspecPath}' is missing from {packagePath}.");
        }

        var allowedRootFiles = new HashSet<string>(StandardEntries, StringComparer.Ordinal);
        AddDeclaredAsset(project.Readme, "README", entries, allowedRootFiles, packagePath);
        AddDeclaredAsset(project.LicenseFile, "licence", entries, allowedRootFiles, packagePath);

        var frameworks = policy.ExpectedFrameworks;
        var applicationAssets = new HashSet<string>(StringComparer.Ordinal);
        if (policy.Kind is "library" or "razor-library")
        {
            foreach (var framework in frameworks)
            {
                Require(entries, $"lib/{framework}/{project.AssemblyName}.dll", "implementation assembly", packagePath);
                if (policy.RequireXmlDocs)
                {
                    Require(entries, $"lib/{framework}/{project.AssemblyName}.xml", "XML documentation", packagePath);
                }
            }

            var actualFrameworks = entries
                .Select(GetLibraryFramework)
                .Where(static value => value is not null)
                .Select(static value => value!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var expectedFrameworks = frameworks.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if (!actualFrameworks.SequenceEqual(expectedFrameworks, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Unexpected library TFM folders in {packagePath}: '{string.Join(',', actualFrameworks)}'.");
            }

            foreach (var entry in entries.Where(static entry => entry.StartsWith("lib/", StringComparison.Ordinal)))
            {
                var parts = entry.Split('/');
                var expected = new HashSet<string>(StringComparer.Ordinal) { $"lib/{parts[1]}/{project.AssemblyName}.dll" };
                if (policy.RequireXmlDocs)
                {
                    expected.Add($"lib/{parts[1]}/{project.AssemblyName}.xml");
                }
                if (!expected.Contains(entry))
                {
                    throw new InvalidDataException($"Unexpected library asset '{entry}' in {packagePath}.");
                }
            }
        }
        else if (policy.Kind == "analyzer")
        {
            Require(entries, $"analyzers/dotnet/cs/{project.AssemblyName}.dll", "analyser assembly", packagePath);
            if (entries.Any(static entry => entry.StartsWith("lib/", StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"Analyzer package must not contain a lib implementation assembly: {packagePath}.");
            }
            if (archive.Dependencies.Any(static dependency => dependency.Id.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"Roslyn implementation dependencies leaked into {packagePath}.");
            }
            if (entries.Count(static entry => entry.StartsWith("analyzers/dotnet/cs/", StringComparison.Ordinal) && entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) != 1)
            {
                throw new InvalidDataException($"Expected exactly one C# analyser assembly in {packagePath}.");
            }
            if (!string.Equals(archive.LicenseType, "file", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(project.LicenseFile) ||
                !string.Equals(archive.LicenseValue, NormalisePackagePath(project.LicenseFile), StringComparison.Ordinal))
            {
                throw new InvalidDataException($"SourceGen package must declare its licence file in {packagePath}.");
            }
        }
        else if (policy.Kind == "application")
        {
            foreach (var framework in frameworks)
            {
                applicationAssets.Add($"lib/{framework}/{project.AssemblyName}.dll");
                applicationAssets.Add($"lib/{framework}/{project.AssemblyName}.runtimeconfig.json");
            }
            foreach (var asset in applicationAssets)
            {
                Require(entries, asset, "application asset", packagePath);
            }
            var applicationFrameworks = entries
                .Select(GetLibraryFramework)
                .Where(static value => value is not null)
                .Select(static value => value!)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            if (!applicationFrameworks.SequenceEqual(frameworks.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal), StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Unexpected application TFM folders in {packagePath}: '{string.Join(',', applicationFrameworks)}'.");
            }
            foreach (var entry in entries.Where(static entry => entry.StartsWith("lib/", StringComparison.Ordinal)))
            {
                if (!applicationAssets.Contains(entry))
                {
                    throw new InvalidDataException($"Unexpected application asset '{entry}' in {packagePath}.");
                }
            }
        }

        if (string.IsNullOrWhiteSpace(project.RepositoryUrl) || !string.Equals(archive.RepositoryUrl, project.RepositoryUrl, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Repository metadata does not match the project in {packagePath}.");
        }

        foreach (var entry in entries)
        {
            if (entry.Split('/').Any(static part => part is "bin" or "obj"))
            {
                throw new InvalidDataException($"Build output '{entry}' leaked into {packagePath}.");
            }
            var category = entry.Split('/', 2)[0];
            if (allowedRootFiles.Contains(entry) || entry == nuspecPath || entry.StartsWith("_rels/", StringComparison.Ordinal) ||
                entry.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal) && entry.EndsWith(".psmdcp", StringComparison.Ordinal))
            {
                continue;
            }
            if (policy.Kind is "library" or "razor-library" && category == "lib")
            {
                continue;
            }
            if (policy.Kind == "application" && applicationAssets.Contains(entry))
            {
                continue;
            }
            if (policy.Kind == "analyzer" && category == "analyzers")
            {
                continue;
            }
            if (policy.AllowedAssetCategories.Contains(category, StringComparer.Ordinal))
            {
                continue;
            }
            throw new InvalidDataException($"Unexpected package asset category/content '{entry}' in {packagePath}.");
        }

        foreach (var entry in entries.Where(IsBinary))
        {
            if (entry.Contains("test", StringComparison.OrdinalIgnoreCase) || entry.Contains("benchmark", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Test or benchmark binary '{entry}' leaked into {packagePath}.");
            }
            if (policy.Kind is "library" or "razor-library" && entry.StartsWith("lib/", StringComparison.Ordinal))
            {
                var expected = $"lib/{entry.Split('/')[1]}/{project.AssemblyName}.dll";
                if (!string.Equals(entry, expected, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Unexpected package binary '{entry}' in {packagePath}.");
                }
            }
            else if (policy.Kind == "analyzer" && entry == $"analyzers/dotnet/cs/{project.AssemblyName}.dll")
            {
                continue;
            }
            else if (policy.Kind == "application" && applicationAssets.Contains(entry))
            {
                continue;
            }
            else if (policy.AllowedAssetCategories.Contains(entry.Split('/', 2)[0], StringComparer.Ordinal))
            {
                continue;
            }
            else
            {
                throw new InvalidDataException($"Unexpected package binary '{entry}' in {packagePath}.");
            }
        }
    }

    private static void ValidateSymbols(string? symbolPath, PackagePolicyEntry policy, PackageProjectMetadata project, ArchiveInfo package, string packagePath)
    {
        if (!policy.RequireSymbols)
        {
            if (!string.IsNullOrWhiteSpace(symbolPath) && File.Exists(symbolPath))
            {
                throw new InvalidDataException($"Policy does not require symbols, but a symbol package was produced for {packagePath}.");
            }
            return;
        }
        if (string.IsNullOrWhiteSpace(symbolPath) || !File.Exists(symbolPath))
        {
            throw new InvalidDataException($"Policy requires a symbol package for {packagePath}.");
        }

        var symbols = ReadArchive(symbolPath);
        if (!string.Equals(symbols.Id, package.Id, StringComparison.Ordinal) || !string.Equals(symbols.Version, package.Version, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Symbol package identity/version does not match {packagePath}.");
        }
        foreach (var framework in policy.ExpectedFrameworks)
        {
            var pdbPath = $"lib/{framework}/{project.AssemblyName}.pdb";
            Require(symbols.Entries, pdbPath, "symbol PDB", symbolPath);
            using var archive = ZipFile.OpenRead(symbolPath);
            using var stream = archive.GetEntry(pdbPath)!.Open();
            var signature = new byte[4];
            if (stream.Read(signature.AsSpan()) != signature.Length || !signature.AsSpan().SequenceEqual("BSJB"u8))
            {
                throw new InvalidDataException($"Expected a portable PDB at '{pdbPath}' in {symbolPath}.");
            }
        }
        foreach (var entry in symbols.Entries)
        {
            if (entry.Split('/').Any(static part => part is "bin" or "obj"))
            {
                throw new InvalidDataException($"Build output '{entry}' leaked into {symbolPath}.");
            }
            if (entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) &&
                (entry.Contains("test", StringComparison.OrdinalIgnoreCase) || entry.Contains("benchmark", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException($"Test or benchmark symbols '{entry}' leaked into {symbolPath}.");
            }
            if (entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && !entry.StartsWith("lib/", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unexpected PDB location '{entry}' in {symbolPath}.");
            }
            if (entry != "[Content_Types].xml" && entry != policy.PackageId + ".nuspec" &&
                !entry.StartsWith("_rels/", StringComparison.Ordinal) &&
                !entry.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal) &&
                !(entry.StartsWith("lib/", StringComparison.Ordinal) && entry.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && entry.Split('/').Length == 3))
            {
                throw new InvalidDataException($"Unexpected symbol package content '{entry}' in {symbolPath}.");
            }
        }
    }

    private static ArchiveInfo ReadArchive(string path)
    {
        try
        {
            using var archive = ZipFile.OpenRead(path);
            var entries = archive.Entries.Select(static entry => entry.FullName.Replace('\\', '/')).ToArray();
            var nuspecs = archive.Entries.Where(static entry =>
                entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Contains('/')).ToArray();
            if (nuspecs.Length != 1)
            {
                throw new InvalidDataException($"Expected exactly one root nuspec in {path}; found {nuspecs.Length}.");
            }

            using var stream = nuspecs[0].Open();
            var document = XDocument.Load(stream);
            var metadata = document.Descendants().FirstOrDefault(static element => element.Name.LocalName == "metadata")
                ?? throw new InvalidDataException($"The nuspec has no metadata element: {path}");
            var dependencies = metadata.Elements().FirstOrDefault(static element => element.Name.LocalName == "dependencies");
            var groups = new List<DependencyGroup>();
            var allDependencies = new List<PackageDependency>();
            if (dependencies is not null)
            {
                foreach (var group in dependencies.Elements().Where(static element => element.Name.LocalName == "group"))
                {
                    var items = group.Elements().Where(static element => element.Name.LocalName == "dependency")
                        .Select(static element => new PackageDependency((string?)element.Attribute("id") ?? string.Empty, (string?)element.Attribute("version") ?? string.Empty))
                        .ToArray();
                    groups.Add(new DependencyGroup((string?)group.Attribute("targetFramework") ?? string.Empty, items));
                    allDependencies.AddRange(items);
                }
                if (groups.Count == 0)
                {
                    var items = dependencies.Elements().Where(static element => element.Name.LocalName == "dependency")
                        .Select(static element => new PackageDependency((string?)element.Attribute("id") ?? string.Empty, (string?)element.Attribute("version") ?? string.Empty))
                        .ToArray();
                    groups.Add(new DependencyGroup(string.Empty, items));
                    allDependencies.AddRange(items);
                }
            }

            var repository = metadata.Elements().FirstOrDefault(static element => element.Name.LocalName == "repository");
            var license = metadata.Elements().FirstOrDefault(static element => element.Name.LocalName == "license");
            var id = metadata.Elements().FirstOrDefault(static element => element.Name.LocalName == "id")?.Value ?? string.Empty;
            var version = metadata.Elements().FirstOrDefault(static element => element.Name.LocalName == "version")?.Value ?? string.Empty;
            return new ArchiveInfo(id, version, (string?)repository?.Attribute("url") ?? string.Empty,
                license?.Value ?? string.Empty, (string?)license?.Attribute("type") ?? string.Empty, entries, groups, allDependencies);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or System.Xml.XmlException)
        {
            throw new InvalidDataException($"Could not inspect package archive '{path}'.", exception);
        }
    }

    private static void AddDeclaredAsset(string? asset, string label, IReadOnlyCollection<string> entries, HashSet<string> allowed, string packagePath)
    {
        if (string.IsNullOrWhiteSpace(asset))
        {
            return;
        }
        var normalised = NormalisePackagePath(asset);
        Require(entries, normalised, $"declared package {label}", packagePath);
        allowed.Add(normalised);
    }

    private static string NormalisePackagePath(string path) => path.Replace('\\', '/').TrimStart('/');

    private static string StripWhitespace(string value) => string.Concat(value.Where(static character => !char.IsWhiteSpace(character)));

    private static void Require(IReadOnlyCollection<string> entries, string expected, string label, string packagePath)
    {
        if (!entries.Contains(expected, StringComparer.Ordinal))
        {
            throw new InvalidDataException($"Missing {label} '{expected}' in {packagePath}.");
        }
    }

    private static string? GetLibraryFramework(string entry)
    {
        var parts = entry.Split('/');
        return parts.Length >= 3 && parts[0] == "lib" ? parts[1] : null;
    }

    private static bool IsBinary(string entry)
    {
        var extension = Path.GetExtension(entry);
        return extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".so", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".dylib", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".a", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".lib", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToShortFrameworkName(string framework)
    {
        var value = framework.Replace(",Version=v", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().TrimStart('.');
        if (value.StartsWith("NETCoreApp", StringComparison.OrdinalIgnoreCase))
        {
            return "net" + value["NETCoreApp".Length..].ToLowerInvariant();
        }
        if (value.StartsWith("NETStandard", StringComparison.OrdinalIgnoreCase))
        {
            return "netstandard" + value["NETStandard".Length..].ToLowerInvariant();
        }
        if (value.StartsWith("NETFramework", StringComparison.OrdinalIgnoreCase))
        {
            return "net" + value["NETFramework".Length..].ToLowerInvariant();
        }
        return value.ToLowerInvariant();
    }

    private sealed record PackageDependency(string Id, string Version);

    private sealed record DependencyGroup(string TargetFramework, IReadOnlyList<PackageDependency> Dependencies);

    private sealed record ArchiveInfo(
        string Id,
        string Version,
        string RepositoryUrl,
        string LicenseValue,
        string LicenseType,
        IReadOnlyList<string> Entries,
        IReadOnlyList<DependencyGroup> DependencyGroups,
        IReadOnlyList<PackageDependency> Dependencies);
}
