using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Repository;

public sealed class RepositoryProjectCatalogTests
{
    [Fact]
    public void ResolvesKnownProjectRolesFromExplicitRepositoryRelativePaths()
    {
        var checkout = RepositoryPaths.Resolve(explicitRepositoryPath: null, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);

        Assert.Equal(Path.Combine(checkout.RepositoryRoot, "src", "core", "Rowles.LeanCorpus", "Rowles.LeanCorpus.csproj"),
            RepositoryProjectCatalog.GetProjectPath(checkout.RepositoryRoot, RepositoryProjectRole.Core));
        Assert.Equal(Path.Combine(checkout.RepositoryRoot, "src", "devops", "tooling", "Rowles.LeanCorpus.DevOps", "Rowles.LeanCorpus.DevOps.csproj"),
            RepositoryProjectCatalog.GetProjectPath(checkout.RepositoryRoot, RepositoryProjectRole.DevOpsHost));
        Assert.Equal(Path.Combine(checkout.RepositoryRoot, "src", "server", "Rowles.LeanCorpus.Server.Local", "Rowles.LeanCorpus.Server.Local.csproj"),
            RepositoryProjectCatalog.GetProjectPath(checkout.RepositoryRoot, RepositoryProjectRole.ServerLocal));
        Assert.Equal(Path.Combine(checkout.RepositoryRoot, "src", "devops", "dataforge", "Rowles.DataForge.Tool", "Rowles.DataForge.Tool.csproj"),
            RepositoryProjectCatalog.GetProjectPath(checkout.RepositoryRoot, RepositoryProjectRole.DataForgeTool));
        Assert.True(File.Exists(RepositoryPaths.GetSolutionPath(checkout.RepositoryRoot)));
        Assert.All(Enum.GetValues<RepositoryProjectRole>(), role =>
            Assert.True(File.Exists(RepositoryProjectCatalog.GetExistingProjectPath(checkout.RepositoryRoot, role))));
    }

    [Fact]
    public void RequiresTheSelectedRoleProjectToExistInTheSelectedCheckout()
    {
        using var directory = TemporaryDirectory.Create("DevOps missing project role ");

        var error = Assert.Throws<FileNotFoundException>(() =>
            RepositoryProjectCatalog.GetExistingProjectPath(directory.Path, RepositoryProjectRole.DataForgeTool));

        Assert.Contains("Rowles.DataForge.Tool.csproj", error.Message, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(directory.Path), error.Message, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;

        public string Path { get; }

        public static TemporaryDirectory Create(string prefix)
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
