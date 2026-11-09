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

        string temporaryRoot = RepositoryTemporaryStorage.ResolveTemporaryRoot(
            RepositoryTemporaryStorage.RepositoryRoot, Environment.GetEnvironmentVariable(RepositoryTemporaryStorage.RootOverrideVariable));
        string expectedRoot = Path.Combine(temporaryRoot, "tests") + Path.DirectorySeparatorChar;
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
    public void Override_root_is_used_only_when_it_is_an_existing_owner_only_directory()
    {
        using var parent = RepositoryTemporaryStorage.CreateDirectory("tests", "override_");
        string repository = RepositoryTemporaryStorage.RepositoryRoot;

        Assert.Equal(Path.Combine(repository, "artifacts", "tmp"), RepositoryTemporaryStorage.ResolveTemporaryRoot(repository, null));
        Assert.Equal(parent.Path, RepositoryTemporaryStorage.ResolveTemporaryRoot(repository, parent.Path));

        Assert.Throws<InvalidOperationException>(() =>
            RepositoryTemporaryStorage.ResolveTemporaryRoot(repository, Path.Combine(parent.Path, "missing")));

        if (OperatingSystem.IsWindows())
            return;

        string open = Path.Combine(parent.Path, "open");
        Directory.CreateDirectory(open);
        File.SetUnixFileMode(open, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        Assert.Throws<InvalidOperationException>(() => RepositoryTemporaryStorage.ResolveTemporaryRoot(repository, open));

        string link = Path.Combine(parent.Path, "link");
        Directory.CreateSymbolicLink(link, parent.Path);
        Assert.Throws<InvalidOperationException>(() => RepositoryTemporaryStorage.ResolveTemporaryRoot(repository, link));
    }

    [Fact]
    public void Missing_repository_marker_throws_instead_of_using_system_temporary_storage()
    {
        string outside = Path.GetPathRoot(RepositoryTemporaryStorage.RepositoryRoot)!;

        var exception = Assert.Throws<InvalidOperationException>(() => RepositoryTemporaryStorage.ResolveRepositoryRoot(outside));
        Assert.Contains(RepositoryTemporaryStorage.RootMarker, exception.Message, StringComparison.Ordinal);
    }
}
