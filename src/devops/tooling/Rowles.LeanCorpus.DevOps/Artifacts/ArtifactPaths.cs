using System.Collections.Immutable;

namespace Rowles.LeanCorpus.DevOps.Artifacts;

internal enum ArtifactRunCategoryKind
{
    Build,
    Test,
    Coverage,
    Benchmark,
    Diagnostics,
    Report,
    Package,
    Distribution,
    Docs,
    Data,
    DataForge,
    Server
}

internal sealed record ArtifactRunCategoryDescriptor(ArtifactRunCategoryKind Kind, string Id);

internal sealed class ArtifactPaths
{
    private static readonly ImmutableArray<ArtifactRunCategoryDescriptor> CategoryCatalog =
    [
        new(ArtifactRunCategoryKind.Build, "build"),
        new(ArtifactRunCategoryKind.Test, "test"),
        new(ArtifactRunCategoryKind.Coverage, "coverage"),
        new(ArtifactRunCategoryKind.Benchmark, "benchmark"),
        new(ArtifactRunCategoryKind.Diagnostics, "diagnostics"),
        new(ArtifactRunCategoryKind.Report, "report"),
        new(ArtifactRunCategoryKind.Package, "package"),
        new(ArtifactRunCategoryKind.Distribution, "distribution"),
        new(ArtifactRunCategoryKind.Docs, "docs"),
        new(ArtifactRunCategoryKind.Data, "data"),
        new(ArtifactRunCategoryKind.DataForge, "dataforge"),
        new(ArtifactRunCategoryKind.Server, "server")
    ];

    private static readonly ImmutableDictionary<string, ArtifactRunCategoryDescriptor> CategoriesById
        = CategoryCatalog.ToImmutableDictionary(static category => category.Id, StringComparer.Ordinal);

    public static ImmutableArray<ArtifactRunCategoryDescriptor> RunCategories => CategoryCatalog;

    public ArtifactPaths(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        RepositoryRoot = Path.GetFullPath(repositoryRoot);
        ArtifactRoot = Path.Combine(RepositoryRoot, "artifacts");
        BuildRoot = Path.Combine(ArtifactRoot, "build");
        BuildRunRoot = Path.Combine(BuildRoot, "runs");
        DevOpsRoot = Path.Combine(ArtifactRoot, "devops");
        RootApphostPath = Path.Combine(RepositoryRoot, OperatingSystem.IsWindows() ? "devops.exe" : "devops");
    }

    public string RepositoryRoot { get; }

    public string ArtifactRoot { get; }

    public string BuildRoot { get; }

    public string BuildRunRoot { get; }

    public string DevOpsRoot { get; }

    public string RootApphostPath { get; }

    public string GetRunRoot(string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        if (!CategoriesById.ContainsKey(category))
        {
            throw new ArgumentException($"'{category}' is not a supported artefact run category.", nameof(category));
        }

        return Path.Combine(ArtifactRoot, category, "runs");
    }

    public string CreateRunDirectory(string category, Guid runId)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty run identity is required.", nameof(runId));
        }

        var path = Path.Combine(GetRunRoot(category), runId.ToString("D"));
        Directory.CreateDirectory(path);
        return path;
    }

    public string GetRunDirectory(string category, Guid runId)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty run identity is required.", nameof(runId));
        }

        return Path.Combine(GetRunRoot(category), runId.ToString("D"));
    }

    public string CreateBuildRunDirectory(Guid runId)
    {
        return CreateRunDirectory("build", runId);
    }

    public string GetBuildRunDirectory(Guid runId)
    {
        if (runId == Guid.Empty)
        {
            throw new ArgumentException("A non-empty run identity is required.", nameof(runId));
        }

        return Path.Combine(BuildRunRoot, runId.ToString("D"));
    }

    public bool IsBuildRunPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(BuildRunRoot));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return candidate.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }
}
