using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed record PackagePolicy(
    int SchemaVersion,
    IReadOnlyList<PackagePolicyEntry> Packages,
    PackageReleaseContract ReleaseContract,
    CompressionPackageContract CompressionContract);

internal sealed record PackageReleaseContract(int Version, IReadOnlyList<string> RequiredPackageIds);

internal sealed record CompressionPackageContract(
    int Version,
    string PackageGroup,
    string CorePackageId,
    IReadOnlyList<string> RequiredPackageIds,
    string CoreDependencyVersionRange)
{
    public IReadOnlyList<string> CodecPackageIds
        => RequiredPackageIds.Where(packageId => !packageId.Equals(CorePackageId, StringComparison.Ordinal)).ToArray();
}

internal sealed record PackagePolicyEntry(
    string PackageId,
    string ProjectPath,
    string Kind,
    IReadOnlyList<string> ExpectedFrameworks,
    IReadOnlyList<string> ExpectedDependencyFrameworks,
    string? BaselineVersion,
    bool EnableApiCompatibility,
    bool StrictBaseline,
    bool RequireXmlDocs,
    bool RequireSymbols,
    IReadOnlyList<string> PackageGroups,
    IReadOnlyList<string> AllowedAssetCategories,
    string ConsumerScenario = "");

internal static class PackagePolicyReader
{
    private static readonly HashSet<string> ValidKinds = new(StringComparer.Ordinal)
    {
        "library", "analyzer", "razor-library", "application"
    };

    public static PackagePolicy Read(string policyPath, string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(policyPath);
        return Parse(File.ReadAllText(policyPath), repositoryRoot);
    }

    public static PackagePolicy Parse(string json, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        PolicyDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<PolicyDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Package policy is not valid JSON.", exception);
        }

        if (document is null || document.SchemaVersion != 2 || document.Packages is not { Count: > 0 } ||
            document.ReleaseContract is null || document.CompressionContract is null)
        {
            throw new InvalidDataException("Package policy must use schemaVersion 2 and contain packages and both release contracts.");
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<PackagePolicyEntry>(document.Packages.Count);
        foreach (var item in document.Packages)
        {
            if (string.IsNullOrWhiteSpace(item.PackageId) || string.IsNullOrWhiteSpace(item.ProjectPath))
            {
                throw new InvalidDataException("Every package policy entry requires packageId and projectPath.");
            }

            if (!ids.Add(item.PackageId))
            {
                throw new InvalidDataException($"Package policy contains duplicate package ID '{item.PackageId}'.");
            }

            if (item.Kind is null || !ValidKinds.Contains(item.Kind))
            {
                throw new InvalidDataException($"Unsupported package kind '{item.Kind}' for {item.PackageId}.");
            }

            if (string.IsNullOrWhiteSpace(item.ConsumerScenario))
            {
                throw new InvalidDataException($"Package {item.PackageId} requires a consumerScenario role.");
            }
            if (!ConsumerScenarioCatalog.IsSupportedRole(item.ConsumerScenario))
            {
                throw new InvalidDataException($"Package {item.PackageId} has unknown consumerScenario role '{item.ConsumerScenario}'.");
            }

            if (item.ExpectedFrameworks is null)
            {
                throw new InvalidDataException($"Package {item.PackageId} requires expectedFrameworks.");
            }

            var frameworks = item.ExpectedFrameworks.Where(static value => !string.IsNullOrWhiteSpace(value)).ToArray();
            if (item.Kind == "analyzer" && frameworks.Length != 0)
            {
                throw new InvalidDataException($"Analyzer package {item.PackageId} must declare no library TFMs.");
            }
            if (item.Kind != "analyzer" && frameworks.Length == 0)
            {
                throw new InvalidDataException($"Package {item.PackageId} must declare at least one target framework.");
            }

            if (item.EnableApiCompatibility is null || item.RequireXmlDocs is null || item.RequireSymbols is null)
            {
                throw new InvalidDataException($"Package {item.PackageId} requires Boolean enableApiCompatibility, requireXmlDocs and requireSymbols values.");
            }

            if (item.StrictBaseline == true && item.EnableApiCompatibility != true)
            {
                throw new InvalidDataException($"Package {item.PackageId} requires enableApiCompatibility for strictBaseline.");
            }
            if (item.StrictBaseline == true && string.IsNullOrWhiteSpace(item.BaselineVersion))
            {
                throw new InvalidDataException($"Package {item.PackageId} requires baselineVersion for strictBaseline.");
            }

            string fullProjectPath;
            try
            {
                fullProjectPath = Path.GetFullPath(Path.Combine(root, item.ProjectPath));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidDataException($"Package policy project path is invalid: '{item.ProjectPath}'.", exception);
            }

            if (!fullProjectPath.StartsWith(root + Path.DirectorySeparatorChar, pathComparison))
            {
                throw new InvalidDataException($"Package policy project resolves outside the selected checkout: '{item.ProjectPath}'.");
            }
            if (Directory.Exists(root) && !File.Exists(fullProjectPath))
            {
                throw new InvalidDataException($"Package policy project does not exist: '{item.ProjectPath}'.");
            }

            var packageGroups = item.PackageGroups ?? [];
            if (packageGroups.Any(static group => string.IsNullOrWhiteSpace(group)) ||
                packageGroups.Distinct(StringComparer.Ordinal).Count() != packageGroups.Count)
            {
                throw new InvalidDataException($"Package {item.PackageId} has an empty or duplicate package group.");
            }

            entries.Add(new PackagePolicyEntry(
                item.PackageId,
                item.ProjectPath,
                item.Kind,
                frameworks,
                item.ExpectedDependencyFrameworks ?? [],
                item.BaselineVersion,
                item.EnableApiCompatibility.Value,
                item.StrictBaseline ?? false,
                item.RequireXmlDocs.Value,
                item.RequireSymbols.Value,
                packageGroups,
                item.AllowedAssetCategories ?? [],
                item.ConsumerScenario));
        }

        var releaseContract = ReadReleaseContract(document.ReleaseContract);
        var compressionContract = ReadCompressionContract(document.CompressionContract, entries, root, pathComparison);
        ValidateReleasePackageSet(releaseContract, ids);
        ValidateCompressionGroup(compressionContract, entries);
        return new PackagePolicy(document.SchemaVersion, entries, releaseContract, compressionContract);
    }

    private static PackageReleaseContract ReadReleaseContract(ReleaseContractDocument contract)
    {
        if (contract.Version != 1 || contract.RequiredPackageIds is not { Count: > 0 })
            throw new InvalidDataException("The release contract must use version 1 and contain requiredPackageIds.");
        var required = contract.RequiredPackageIds;
        if (required.Any(string.IsNullOrWhiteSpace) || required.Distinct(StringComparer.Ordinal).Count() != required.Count)
            throw new InvalidDataException("The release contract contains an empty or duplicate required package ID.");
        return new PackageReleaseContract(contract.Version, required.ToArray());
    }

    private static CompressionPackageContract ReadCompressionContract(
        CompressionContractDocument contract,
        IReadOnlyList<PackagePolicyEntry> entries,
        string repositoryRoot,
        StringComparison pathComparison)
    {
        if (contract.Version != 1 || string.IsNullOrWhiteSpace(contract.PackageGroup) ||
            string.IsNullOrWhiteSpace(contract.CorePackageId) || contract.RequiredPackageIds is not { Count: > 1 } ||
            string.IsNullOrWhiteSpace(contract.CoreDependencyVersionRange))
        {
            throw new InvalidDataException("The compression contract must use version 1 and define its group, Core package, package IDs and Core dependency range.");
        }
        if (contract.RequiredPackageIds.Any(string.IsNullOrWhiteSpace) ||
            contract.RequiredPackageIds.Distinct(StringComparer.Ordinal).Count() != contract.RequiredPackageIds.Count)
        {
            throw new InvalidDataException("The compression contract contains an empty or duplicate required package ID.");
        }
        if (!contract.RequiredPackageIds.Contains(contract.CorePackageId, StringComparer.Ordinal))
            throw new InvalidDataException("The compression contract must include its Core package in requiredPackageIds.");

        var (minimum, maximum) = ParseCoreVersionRange(contract.CoreDependencyVersionRange);
        var core = entries.SingleOrDefault(entry => entry.PackageId.Equals(contract.CorePackageId, StringComparison.Ordinal));
        if (core is null)
            throw new InvalidDataException($"The compression contract refers to unknown Core package '{contract.CorePackageId}'.");

        if (Directory.Exists(repositoryRoot))
        {
            var corePath = Path.GetFullPath(Path.Combine(repositoryRoot, core.ProjectPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!corePath.StartsWith(repositoryRoot + Path.DirectorySeparatorChar, pathComparison))
                throw new InvalidDataException($"The compression contract Core project resolves outside the selected checkout: '{core.ProjectPath}'.");
            var coreVersionText = PackageProjectInspector.ReadMetadata(corePath).Version;
            if (!Version.TryParse(coreVersionText, out var coreVersion) || coreVersion.Major != minimum.Major ||
                coreVersion < minimum || coreVersion >= maximum)
            {
                throw new InvalidDataException($"Compression contract range '{contract.CoreDependencyVersionRange}' does not cover the current Core package version '{coreVersionText}'. Update the versioned compression contract when Core changes major version.");
            }
        }

        return new CompressionPackageContract(contract.Version, contract.PackageGroup, contract.CorePackageId,
            contract.RequiredPackageIds.ToArray(), contract.CoreDependencyVersionRange);
    }

    private static (Version Minimum, Version Maximum) ParseCoreVersionRange(string range)
    {
        const string prefix = "[";
        const string separator = ",";
        const string suffix = ")";
        var parts = range.StartsWith(prefix, StringComparison.Ordinal) && range.EndsWith(suffix, StringComparison.Ordinal)
            ? range[1..^1].Split(separator, StringSplitOptions.None)
            : [];
        if (parts.Length != 2 || !Version.TryParse(parts[0], out var minimum) || !Version.TryParse(parts[1], out var maximum) ||
            minimum.Major < 1 || minimum.Minor != 0 || minimum.Build != 0 || minimum.Revision != -1 ||
            maximum.Minor != 0 || maximum.Build != 0 || maximum.Revision != -1 || maximum.Major != minimum.Major + 1)
        {
            throw new InvalidDataException($"Compression Core dependency range '{range}' must be a closed lower bound and open next-major upper bound such as [4.0.0,5.0.0).");
        }

        return (minimum, maximum);
    }

    private static void ValidateReleasePackageSet(PackageReleaseContract contract, IReadOnlySet<string> packageIds)
    {
        var missing = contract.RequiredPackageIds.Where(id => !packageIds.Contains(id)).Order(StringComparer.Ordinal).ToArray();
        if (missing.Length > 0)
            throw new InvalidDataException($"Package policy is missing required release package '{missing[0]}'.");
        var unexpected = packageIds.Where(id => !contract.RequiredPackageIds.Contains(id, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToArray();
        if (unexpected.Length > 0)
            throw new InvalidDataException($"Package policy contains package '{unexpected[0]}' outside the required release contract.");
    }

    private static void ValidateCompressionGroup(CompressionPackageContract contract, IReadOnlyList<PackagePolicyEntry> entries)
    {
        var actual = entries.Where(entry => entry.PackageGroups.Contains(contract.PackageGroup, StringComparer.Ordinal))
            .Select(static entry => entry.PackageId).Order(StringComparer.Ordinal).ToArray();
        var expected = contract.RequiredPackageIds.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException("Package compression group membership does not match its contract.");
    }

    private sealed class PolicyDocument
    {
        public int SchemaVersion { get; init; }

        public List<PolicyEntry>? Packages { get; init; }

        public ReleaseContractDocument? ReleaseContract { get; init; }

        public CompressionContractDocument? CompressionContract { get; init; }
    }

    private sealed class ReleaseContractDocument
    {
        public int Version { get; init; }

        public List<string>? RequiredPackageIds { get; init; }
    }

    private sealed class CompressionContractDocument
    {
        public int Version { get; init; }

        public string? PackageGroup { get; init; }

        public string? CorePackageId { get; init; }

        public List<string>? RequiredPackageIds { get; init; }

        public string? CoreDependencyVersionRange { get; init; }
    }

    private sealed class PolicyEntry
    {
        public string? PackageId { get; init; }

        public string? ProjectPath { get; init; }

        public string? Kind { get; init; }

        public List<string>? ExpectedFrameworks { get; init; }

        public List<string>? ExpectedDependencyFrameworks { get; init; }

        public string? BaselineVersion { get; init; }

        public bool? EnableApiCompatibility { get; init; }

        public bool? StrictBaseline { get; init; }

        public bool? RequireXmlDocs { get; init; }

        public bool? RequireSymbols { get; init; }

        public List<string>? PackageGroups { get; init; }

        public List<string>? AllowedAssetCategories { get; init; }

        public string? ConsumerScenario { get; init; }
    }
}
