using Rowles.LeanCorpus.DevOps.Repository;

namespace Rowles.LeanCorpus.DevOps.Tests.Repository;

public sealed class RepositoryPathsTests
{
    [Fact]
    public void ResolvesCheckoutWithGitDirectoryFromNestedCallerDirectory()
    {
        using var checkout = TemporaryCheckout.Create(gitFile: false);
        var nested = Directory.CreateDirectory(Path.Combine(checkout.Root, "src", "devops")).FullName;

        var result = RepositoryPaths.Resolve(explicitRepositoryPath: null, nested, executablePath: null);

        Assert.Equal(Path.GetFullPath(checkout.Root), result.RepositoryRoot);
        Assert.Equal(CheckoutGitMarkerKind.Directory, result.GitMarkerKind);
        Assert.Equal(Path.Combine(checkout.Root, ".git"), result.GitMetadataPath);
    }

    [Fact]
    public void ResolvesWorktreeWithGitFileMarker()
    {
        using var checkout = TemporaryCheckout.Create(gitFile: true);

        var result = RepositoryPaths.Resolve(explicitRepositoryPath: null, checkout.Root, executablePath: null);

        Assert.Equal(Path.GetFullPath(checkout.Root), result.RepositoryRoot);
        Assert.Equal(CheckoutGitMarkerKind.File, result.GitMarkerKind);
    }

    [Fact]
    public void RejectsWrongCheckoutMarkersInsteadOfAcceptingOnlyGitMetadata()
    {
        using var directory = TemporaryDirectory.Create("DevOps wrong checkout ");
        Directory.CreateDirectory(Path.Combine(directory.Path, ".git"));
        File.WriteAllText(Path.Combine(directory.Path, "global.json"), "{}");

        var error = Assert.Throws<InvalidOperationException>(() =>
            RepositoryPaths.Resolve(directory.Path, workingDirectory: null, executablePath: null));

        Assert.Contains("Rowles.LeanCorpus.slnx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitSelectionOverridesCallerAndExecutableLocations()
    {
        using var explicitCheckout = TemporaryCheckout.Create(gitFile: false);
        using var callerCheckout = TemporaryCheckout.Create(gitFile: false);
        var executable = Path.Combine(callerCheckout.Root, "devops");

        var result = RepositoryPaths.Resolve(explicitCheckout.Root, callerCheckout.Root, executable);

        Assert.Equal(Path.GetFullPath(explicitCheckout.Root), result.RepositoryRoot);
    }

    [Fact]
    public void RejectsCallerAndExecutableThatResolveToDifferentCheckouts()
    {
        using var callerCheckout = TemporaryCheckout.Create(gitFile: false);
        using var executableCheckout = TemporaryCheckout.Create(gitFile: true);
        var executable = Path.Combine(executableCheckout.Root, "devops");

        var error = Assert.Throws<InvalidOperationException>(() =>
            RepositoryPaths.Resolve(explicitRepositoryPath: null, callerCheckout.Root, executable));

        Assert.Contains("explicit repository", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(callerCheckout.Root), error.Message, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(executableCheckout.Root), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UsesExecutableCheckoutWhenCallerIsOutsideAnyCheckout()
    {
        using var executableCheckout = TemporaryCheckout.Create(gitFile: false);
        using var outside = TemporaryDirectory.Create("DevOps outside checkout ");

        var result = RepositoryPaths.Resolve(explicitRepositoryPath: null, outside.Path, Path.Combine(executableCheckout.Root, "devops"));

        Assert.Equal(Path.GetFullPath(executableCheckout.Root), result.RepositoryRoot);
    }

    private sealed class TemporaryCheckout : IDisposable
    {
        private TemporaryCheckout(string root) => Root = root;

        public string Root { get; }

        public static TemporaryCheckout Create(bool gitFile)
        {
            var root = Path.Combine(Path.GetTempPath(), "DevOps checkout " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            if (gitFile)
            {
                File.WriteAllText(Path.Combine(root, ".git"), "gitdir: ../.git/worktrees/test\n");
            }
            else
            {
                Directory.CreateDirectory(Path.Combine(root, ".git"));
            }

            File.WriteAllText(Path.Combine(root, "Rowles.LeanCorpus.slnx"), "<Solution />");
            File.WriteAllText(Path.Combine(root, "global.json"), "{\"sdk\":{\"version\":\"10.0.100\"}}\n");
            return new TemporaryCheckout(root);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
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
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
