namespace Rowles.LeanCorpus.DevOps.Artifacts;

internal sealed class ArtifactPaths
{
    public static IReadOnlyList<string> RunCategories { get; } = Array.AsReadOnly(new[]
    {
        "build", "test", "coverage", "benchmark", "diagnostics", "report", "package", "distribution", "docs", "data", "dataforge", "server"
    });

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
        if (!RunCategories.Contains(category, StringComparer.Ordinal))
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
