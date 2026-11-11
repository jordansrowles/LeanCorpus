using Rowles.LeanCorpus.DevOps.Artifacts;

namespace Rowles.LeanCorpus.DevOps.Tests.Artifacts;

public sealed class RepositoryTemporaryStorageTests
{
    [Fact]
    public void LinkedHelperCreatesUniqueOwnerOnlyDirectoriesUnderTheCheckout()
    {
        using var first = RepositoryTemporaryStorage.CreateDirectory("devops-tests", "link_");
        using var second = RepositoryTemporaryStorage.CreateDirectory("devops-tests", "link_");

        var areaRoot = Path.Combine(RepositoryTemporaryStorage.ResolveTemporaryRoot(RepositoryTemporaryStorage.RepositoryRoot,
            Environment.GetEnvironmentVariable(RepositoryTemporaryStorage.RootOverrideVariable)), "devops-tests") + Path.DirectorySeparatorChar;
        Assert.StartsWith(areaRoot, first.Path, StringComparison.Ordinal);
        Assert.NotEqual(first.Path, second.Path);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(first.Path));
    }

    [Fact]
    public void ArtifactPathsCreatesUniqueOwnerOnlyOperationDirectories()
    {
        var paths = new ArtifactPaths(RepositoryTemporaryStorage.RepositoryRoot);
        var first = paths.CreateTemporaryDirectory("probe-");
        var second = paths.CreateTemporaryDirectory("probe-");
        try
        {
            var overrideRoot = Environment.GetEnvironmentVariable(RepositoryTemporaryStorage.RootOverrideVariable);
            Assert.Equal(string.IsNullOrWhiteSpace(overrideRoot)
                ? Path.Combine(paths.ArtifactRoot, "tmp", "devops")
                : Path.Combine(Path.GetFullPath(overrideRoot), "devops"), paths.TemporaryRoot);
            Assert.StartsWith(paths.TemporaryRoot + Path.DirectorySeparatorChar, first, StringComparison.Ordinal);
            Assert.NotEqual(first, second);
            Assert.True(Directory.Exists(first));
            if (!OperatingSystem.IsWindows())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(first));
        }
        finally
        {
            RepositoryTemporaryStorage.DeleteDirectory(first);
            RepositoryTemporaryStorage.DeleteDirectory(second);
        }
    }
}
