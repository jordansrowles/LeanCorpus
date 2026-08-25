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

    [Fact(DisplayName = "Tracked index write: close automatically registers the file")]
    public void CreateTrackedIndexFile_Dispose_RegistersDirtyFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lc_tracked_{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "seg_0.pos");
        Directory.CreateDirectory(directory);
        try
        {
            using (var stream = FileOpenRetry.CreateTrackedIndexFile(path))
                stream.WriteByte(1);

            var dirty = Assert.Single(DirtyFileTracker.Snapshot(directory, static _ => true));
            Assert.Equal(Path.GetFullPath(path), dirty.Path);
        }
        finally
        {
            FileOpenRetry.DeleteDirectory(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "Untracked diagnostic write: does not enter commit durability state")]
    public void OpenAppendText_DiagnosticWrite_DoesNotRegisterDirtyFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"lc_untracked_{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "slow-query.log");
        Directory.CreateDirectory(directory);
        try
        {
            using (var writer = FileOpenRetry.OpenAppendText(path))
                writer.WriteLine("diagnostic");

            Assert.Empty(DirtyFileTracker.Snapshot(directory, static _ => true));
        }
        finally
        {
            FileOpenRetry.DeleteDirectory(directory, recursive: true);
        }
    }

    [Fact(DisplayName = "File sync: retries a transient failure then succeeds")]
    public void PlatformFileSystem_SyncFile_RetriesTransientFailure()
    {
        var fileSystem = new ScriptedFileSystem(transientFailures: 1);
        var delays = new List<int>();

        PlatformFileSystem.SyncFile(fileSystem, "index-file", delays.Add);

        Assert.Equal(2, fileSystem.FileSyncAttempts);
        Assert.Equal([200], delays);
    }

    [Fact(DisplayName = "File sync: stops after the bounded transient retry window")]
    public void PlatformFileSystem_SyncFile_StopsAfterRetryBound()
    {
        var fileSystem = new ScriptedFileSystem(transientFailures: int.MaxValue);
        var delays = new List<int>();

        Assert.Throws<IOException>(() =>
            PlatformFileSystem.SyncFile(fileSystem, "index-file", delays.Add));

        Assert.Equal(6, fileSystem.FileSyncAttempts);
        Assert.Equal(5, delays.Count);
        Assert.All(delays, delay => Assert.Equal(200, delay));
    }

    [Fact(DisplayName = "File sync: permanent failures propagate without retry")]
    public void PlatformFileSystem_SyncFile_DoesNotRetryPermanentFailure()
    {
        var fileSystem = new ScriptedFileSystem(permanentFailure: true);
        var delays = new List<int>();

        Assert.Throws<IOException>(() =>
            PlatformFileSystem.SyncFile(fileSystem, "index-file", delays.Add));

        Assert.Equal(1, fileSystem.FileSyncAttempts);
        Assert.Empty(delays);
    }

    [Fact(DisplayName = "Windows filesystem: caches unsupported directory flush by volume")]
    public void WindowsFileSystem_SyncDirectory_CachesUnsupportedResultByVolume()
    {
        int nativeCalls = 0;
        var fileSystem = new WindowsFileSystem((_, isDirectory) =>
        {
            Assert.True(isDirectory);
            nativeCalls++;
            return DirectorySyncResult.Unsupported;
        });
        string root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;

        var first = fileSystem.SyncDirectory(Path.Combine(root, "one"));
        var second = fileSystem.SyncDirectory(Path.Combine(root, "two"));

        Assert.Equal(DirectorySyncResult.Unsupported, first);
        Assert.Equal(DirectorySyncResult.SkippedUnsupported, second);
        Assert.Equal(1, nativeCalls);
    }

    private sealed class ScriptedFileSystem(int transientFailures = 0, bool permanentFailure = false)
        : IPlatformFileSystem
    {
        private int _transientFailures = transientFailures;

        internal int FileSyncAttempts { get; private set; }

        public void SyncFile(string path)
        {
            FileSyncAttempts++;
            if (permanentFailure)
                throw new IOException("Permanent injected failure.");
            if (_transientFailures-- > 0)
                throw new IOException("Transient injected failure.", unchecked((int)0x80070020));
        }

        public DirectorySyncResult SyncDirectory(string path) => DirectorySyncResult.Succeeded;

        public bool IsTransient(Exception exception) =>
            (exception.HResult & 0xFFFF) == 32;
    }
}
