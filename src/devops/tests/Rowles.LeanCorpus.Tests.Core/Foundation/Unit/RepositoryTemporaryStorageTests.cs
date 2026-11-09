using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Foundation.Unit;

[Category(TestCategory.Unit)]
[Area(TestArea.Foundation)]
public sealed class RepositoryTemporaryStorageTests
{
    [Fact]
    public void Directories_are_created_under_this_checkout_artifacts_tmp_area()
    {
        using var directory = RepositoryTemporaryStorage.CreateDirectory("tests", "probe_");

        string expectedRoot = Path.Combine(RepositoryTemporaryStorage.RepositoryRoot, "artifacts", "tmp", "tests") + Path.DirectorySeparatorChar;
        Assert.StartsWith(expectedRoot, directory.Path, StringComparison.Ordinal);
        Assert.StartsWith("probe_", Path.GetFileName(directory.Path), StringComparison.Ordinal);
        Assert.True(Directory.Exists(directory.Path));
        Assert.True(File.Exists(Path.Combine(RepositoryTemporaryStorage.RepositoryRoot, ".config", "devops", "test-suites.json")));
    }

    [Fact]
    public void Each_call_creates_a_different_directory()
    {
        using var first = RepositoryTemporaryStorage.CreateDirectory("tests");
        using var second = RepositoryTemporaryStorage.CreateDirectory("tests");

        Assert.NotEqual(first.Path, second.Path);
    }

    [Fact]
    public void Directories_are_owner_only_on_Unix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var directory = RepositoryTemporaryStorage.CreateDirectory("tests");

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            File.GetUnixFileMode(directory.Path));
    }

    [Fact]
    public void Dispose_removes_only_its_own_directory()
    {
        var owned = RepositoryTemporaryStorage.CreateDirectory("tests");
        using var sibling = RepositoryTemporaryStorage.CreateDirectory("tests");
        File.WriteAllText(Path.Combine(owned.Path, "data.txt"), "x");
        File.WriteAllText(Path.Combine(sibling.Path, "data.txt"), "y");

        owned.Dispose();

        Assert.False(Directory.Exists(owned.Path));
        Assert.True(File.Exists(Path.Combine(sibling.Path, "data.txt")));
        Assert.True(Directory.Exists(Path.GetDirectoryName(owned.Path)));
    }

    [Fact]
    public void Unknown_area_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => RepositoryTemporaryStorage.CreateDirectory("scratch"));
    }

    [Fact]
    public void Missing_repository_marker_throws_instead_of_using_system_temporary_storage()
    {
        string outside = Path.GetPathRoot(RepositoryTemporaryStorage.RepositoryRoot)!;

        var exception = Assert.Throws<InvalidOperationException>(() => RepositoryTemporaryStorage.ResolveRepositoryRoot(outside));
        Assert.Contains(RepositoryTemporaryStorage.RootMarker, exception.Message, StringComparison.Ordinal);
    }
}
