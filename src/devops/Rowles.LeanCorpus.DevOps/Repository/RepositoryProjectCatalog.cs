namespace Rowles.LeanCorpus.DevOps.Repository;

internal enum RepositoryProjectRole
{
    Core,
    DevOpsHost,
    ServerLocal,
    DataForgeTool,
    BenchmarkWorkloads
}

internal static class RepositoryProjectCatalog
{
    public static string GetRelativePath(RepositoryProjectRole role)
        => role switch
        {
            RepositoryProjectRole.Core => "src/core/Rowles.LeanCorpus/Rowles.LeanCorpus.csproj",
            RepositoryProjectRole.DevOpsHost => "src/devops/Rowles.LeanCorpus.DevOps/Rowles.LeanCorpus.DevOps.csproj",
            RepositoryProjectRole.ServerLocal => "src/server/Rowles.LeanCorpus.Server.Local/Rowles.LeanCorpus.Server.Local.csproj",
            RepositoryProjectRole.DataForgeTool => "src/devops/Rowles.DataForge.Tool/Rowles.DataForge.Tool.csproj",
            RepositoryProjectRole.BenchmarkWorkloads => "src/devops/Rowles.LeanCorpus.Benchmark.Workloads/Rowles.LeanCorpus.Benchmark.Workloads.csproj",
            _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Unknown repository project role.")
        };

    public static string GetProjectPath(string repositoryRoot, RepositoryProjectRole role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Path.GetFullPath(Path.Combine(repositoryRoot, GetRelativePath(role).Replace('/', Path.DirectorySeparatorChar)));
    }

    public static string GetExistingProjectPath(string repositoryRoot, RepositoryProjectRole role)
    {
        var projectPath = GetProjectPath(repositoryRoot, role);
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException(
                $"The {role} project is missing from the selected checkout: '{projectPath}'.", projectPath);
        }

        return projectPath;
    }
}
