using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Store;

/// <summary>Validates platform error classification and versioned dirty-file tracking.</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class PlatformFileSystemTests
{
    [Theory(DisplayName = "Windows filesystem: retries only transient sharing errors")]
    [InlineData(unchecked((int)0x80070020), true)]
    [InlineData(unchecked((int)0x80070021), true)]
    [InlineData(unchecked((int)0x8007012F), true)]
    [InlineData(unchecked((int)0x80070005), false)]
    [InlineData(unchecked((int)0x80070002), false)]
    public void WindowsFileSystem_IsTransient_ClassifiesWin32Errors(int hResult, bool expected)
    {
        var exception = new IOException("Injected filesystem error.", hResult);

        Assert.Equal(expected, WindowsFileSystem.Instance.IsTransient(exception));
    }

    [Fact(DisplayName = "POSIX filesystem: does not retry BCL filesystem errors")]
    public void PosixFileSystem_IsTransient_IsAlwaysFalse()
    {
        Assert.False(PosixFileSystem.Instance.IsTransient(new IOException("Injected filesystem error.")));
    }

    [Fact(DisplayName = "Dirty files: an older sync cannot clear a concurrent rewrite")]
    public void DirtyFileTracker_MarkSynced_PreservesNewerVersion()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lc_dirty_{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "seg_0.pos");
        DirtyFileTracker.MarkWritten(path);
        var original = Assert.Single(DirtyFileTracker.Snapshot(directory, static _ => true));

        DirtyFileTracker.MarkWritten(path);
        DirtyFileTracker.MarkSynced(original);

        Assert.Single(DirtyFileTracker.Snapshot(directory, static _ => true));
        DirtyFileTracker.Delete(path);
    }

    [Fact(DisplayName = "Dirty files: move transfers dirtiness to the published path")]
    public void DirtyFileTracker_Move_TransfersDirtyState()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lc_dirty_{Guid.NewGuid():N}");
        string temporary = Path.Combine(directory, "seg_0.pos.tmp");
        string published = Path.Combine(directory, "seg_0.pos");
        DirtyFileTracker.MarkWritten(temporary);

        DirtyFileTracker.Move(temporary, published);

        var dirty = Assert.Single(DirtyFileTracker.Snapshot(directory, static _ => true));
        Assert.Equal(Path.GetFullPath(published), dirty.Path);
        DirtyFileTracker.Delete(published);
    }

    /// <summary>Verifies recursive directory cleanup cannot retain stale process-wide dirty entries.</summary>
    [Fact(DisplayName = "Dirty files: deleting a directory forgets nested files only")]
    public void DirtyFileTracker_ForgetDirectory_RemovesNestedFilesOnly()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lc_dirty_{Guid.NewGuid():N}");
        string nestedDirectory = Path.Combine(root, "nested");
        string siblingDirectory = root + "-sibling";
        string nestedFile = Path.Combine(nestedDirectory, "seg_0.pos");
        string siblingFile = Path.Combine(siblingDirectory, "seg_1.pos");
        DirtyFileTracker.MarkWritten(nestedFile);
        DirtyFileTracker.MarkWritten(siblingFile);

        DirtyFileTracker.ForgetDirectory(root);

        Assert.Empty(DirtyFileTracker.Snapshot(nestedDirectory, static _ => true));
        Assert.Single(DirtyFileTracker.Snapshot(siblingDirectory, static _ => true));
        DirtyFileTracker.Delete(siblingFile);
    }
}
