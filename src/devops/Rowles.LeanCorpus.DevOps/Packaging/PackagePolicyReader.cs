using System.Text.Json;

namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed record PackagePolicy(int SchemaVersion, IReadOnlyList<PackagePolicyEntry> Packages);

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
    IReadOnlyList<string> AllowedAssetCategories);

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

        if (document is null || document.SchemaVersion != 1 || document.Packages is not { Count: > 0 })
        {
            throw new InvalidDataException("Package policy must use schemaVersion 1 and contain packages.");
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
                item.PackageGroups ?? [],
                item.AllowedAssetCategories ?? []));
        }

        return new PackagePolicy(document.SchemaVersion, entries);
    }

    private sealed class PolicyDocument
    {
        public int SchemaVersion { get; init; }

        public List<PolicyEntry>? Packages { get; init; }
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
    }
}
