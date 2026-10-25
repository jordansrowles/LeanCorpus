namespace Rowles.LeanCorpus.DevOps.Packaging;

internal sealed record ConsumerScenario(
    string Name,
    string PackageId,
    IReadOnlyList<string> PackageIds,
    IReadOnlyList<string> ExactInternalIds,
    string Kind,
    string Framework,
    string Sdk,
    string ExtraProperties,
    IReadOnlyList<string> ExternalReferences,
    string RuntimeKind,
    string CorePackageId,
    string CoreBaselineVersion,
    string? AnalyzerPackageId);

internal static class ConsumerScenarioCatalog
{
    private static readonly IReadOnlyDictionary<string, ScenarioImplementation> Implementations
        = new Dictionary<string, ScenarioImplementation>(StringComparer.Ordinal)
        {
            ["core"] = new("core", "core", "Microsoft.NET.Sdk", string.Empty, [], "core", false, false),
            ["compression"] = new("compression", "compression", "Microsoft.NET.Sdk", string.Empty, [], "compression", true, false),
            ["text"] = new("text", "text", "Microsoft.NET.Sdk", string.Empty, [], "single", false, false),
            ["sourcegen"] = new("sourcegen", "sourcegen", "Microsoft.NET.Sdk", string.Empty, [], "single", true, true),
            ["server-abstractions"] = new("server-abstractions", "abstractions", "Microsoft.NET.Sdk", string.Empty,
                [], "single", false, false),
            ["server-core"] = new("server-core", "server-core", "Microsoft.NET.Sdk", string.Empty,
                [], "single", false, false),
            ["server-aspnetcore"] = new("server-aspnetcore", "server-aspnetcore", "Microsoft.NET.Sdk.Web", string.Empty,
                [], "single", false, false),
            ["server-grpc"] = new("server-grpc", "grpc", "Microsoft.NET.Sdk.Web", string.Empty,
                [], "single", false, false),
            ["server-local"] = new("server-local", "server-local-launcher", "Microsoft.NET.Sdk", string.Empty, [], "server-local", false, false, false),
            ["studio"] = new("studio", "studio", "Microsoft.NET.Sdk.Web", "<AddRazorSupportForMvc>true</AddRazorSupportForMvc>",
                [], "single", false, false),
            ["orchard-search"] = new("orchard-search", "orchard", "Microsoft.NET.Sdk.Web", string.Empty,
                ["OrchardCore.Application.Cms.Targets"], "single", false, false)
        };

    public static bool IsSupportedRole(string? role)
        => role is not null && Implementations.ContainsKey(role);

    public static IReadOnlyList<ConsumerScenario> Create(PackagePolicy policy, string scope, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (scope is not ("all" or "compression"))
            throw new ArgumentException("Package consumer scope must be 'all' or 'compression'.", nameof(scope));

        var byId = policy.Packages.ToDictionary(static entry => entry.PackageId, StringComparer.Ordinal);
        if (!byId.TryGetValue(policy.CompressionContract.CorePackageId, out var core))
            throw new InvalidDataException($"Package policy is missing contract Core package '{policy.CompressionContract.CorePackageId}'.");
        if (string.IsNullOrWhiteSpace(core.BaselineVersion))
            throw new InvalidDataException($"Package policy Core package '{core.PackageId}' requires a baselineVersion for the compression compatibility consumer.");

        var scenarios = new List<ConsumerScenario>();
        foreach (var entry in policy.Packages)
        {
            if (scope == "compression" && !entry.ConsumerScenario.Equals("compression", StringComparison.Ordinal))
                continue;
            if (!Implementations.TryGetValue(entry.ConsumerScenario, out var implementation))
                throw new InvalidDataException($"Package {entry.PackageId} has unknown consumerScenario role '{entry.ConsumerScenario}'.");

            if (entry.ConsumerScenario == "compression" && !policy.CompressionContract.CodecPackageIds.Contains(entry.PackageId, StringComparer.Ordinal))
                throw new InvalidDataException($"Compression consumer package {entry.PackageId} is not listed in the compression contract.");

            var frameworks = entry.ExpectedFrameworks.Count > 0
                ? entry.ExpectedFrameworks
                : implementation.UseCoreFrameworks ? core.ExpectedFrameworks : [];
            if (frameworks.Count == 0)
                throw new InvalidDataException($"Package {entry.PackageId} has no consumer target frameworks in its package policy.");

            var packageIds = implementation.IncludeCorePackage && entry.PackageId != core.PackageId
                ? new[] { core.PackageId, entry.PackageId }
                : [entry.PackageId];
            var internalIds = implementation.VerifyInternalClosure
                ? ResolveInternalPackageClosure(repositoryRoot, byId, packageIds)
                : [];
            var name = entry.ConsumerScenario == "compression"
                ? $"compression-{entry.PackageId.Split('.').Last()}"
                : implementation.Name;
            var kind = entry.ConsumerScenario == "compression" ? name : implementation.Kind;

            foreach (var framework in frameworks)
            {
                scenarios.Add(new ConsumerScenario(name, entry.PackageId, packageIds, internalIds, kind, framework,
                    implementation.Sdk, implementation.ExtraProperties, implementation.ExternalReferences,
                    implementation.RuntimeKind, core.PackageId, core.BaselineVersion,
                    entry.Kind == "analyzer" ? entry.PackageId : null));
            }
        }

        return scenarios;
    }

    private static IReadOnlyList<string> ResolveInternalPackageClosure(
        string repositoryRoot,
        IReadOnlyDictionary<string, PackagePolicyEntry> packages,
        IReadOnlyList<string> roots)
    {
        var resolved = new HashSet<string>(StringComparer.Ordinal);
        foreach (var packageId in roots)
            Visit(packageId);
        return resolved.Order(StringComparer.Ordinal).ToArray();

        void Visit(string packageId)
        {
            if (!packages.TryGetValue(packageId, out var package) || !resolved.Add(packageId))
                return;

            var projectPath = Path.Combine(repositoryRoot, package.ProjectPath.Replace('/', Path.DirectorySeparatorChar));
            foreach (var dependencyId in PackageProjectInspector.ReadDependencyIds(projectPath, repositoryRoot))
            {
                if (packages.ContainsKey(dependencyId))
                    Visit(dependencyId);
            }
        }
    }

    private sealed record ScenarioImplementation(
        string Name,
        string Kind,
        string Sdk,
        string ExtraProperties,
        IReadOnlyList<string> ExternalReferences,
        string RuntimeKind,
        bool IncludeCorePackage,
        bool UseCoreFrameworks,
        bool VerifyInternalClosure = true);
}
