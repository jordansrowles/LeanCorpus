using Rowles.LeanCorpus.Store;
using System.Runtime.InteropServices;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Store;

/// <summary>
/// Unit tests for <see cref="DirectoryFsync"/> covering short-circuits, strict and
/// best-effort error handling, directory sync and file sync with shared readers.
/// DirectoryFsync is internal but accessible via InternalsVisibleTo.
/// </summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class DirectoryFsyncTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public DirectoryFsyncTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ll_fsync_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "test.dat");
        File.WriteAllText(_file, "hello");
    }

    public void Dispose()
    {
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    // Sync

    /// <summary>Verifies Sync with null path returns without throwing.</summary>
    [Fact(DisplayName = "DirectoryFsync: Sync Null Path Is No-Op")]
    public void DirectoryFsync_Sync_NullPath_IsNoOp()
    {
        var ex = Record.Exception(() => DirectoryFsync.Sync(null!));
        Assert.Null(ex);
    }

    /// <summary>Verifies Sync with empty string returns without throwing.</summary>
    [Fact(DisplayName = "DirectoryFsync: Sync Empty Path Is No-Op")]
    public void DirectoryFsync_Sync_EmptyPath_IsNoOp()
    {
        var ex = Record.Exception(() => DirectoryFsync.Sync(string.Empty));
        Assert.Null(ex);
    }

    /// <summary>Verifies best-effort sync with a valid directory completes.</summary>
    [Fact(DisplayName = "DirectoryFsync: Sync Valid Path Completes")]
    public void DirectoryFsync_Sync_ValidPath_Completes()
    {
        var ex = Record.Exception(() => DirectoryFsync.Sync(_dir));
        Assert.Null(ex);
    }

    /// <summary>Verifies strict sync with a valid directory completes.</summary>
    [Fact(DisplayName = "DirectoryFsync: Sync Strict Valid Path Completes")]
    public void DirectoryFsync_Sync_Strict_ValidPath_Completes()
    {
        var ex = Record.Exception(() => DirectoryFsync.Sync(_dir, strict: true));
        Assert.Null(ex);
    }

    /// <summary>Verifies native path encoding handles non-ASCII directory names.</summary>
    [Fact(DisplayName = "DirectoryFsync: Sync Strict Unicode Path Completes")]
    public void DirectoryFsync_Sync_StrictUnicodePath_Completes()
    {
        string unicodeDirectory = Path.Combine(_dir, "café");
        Directory.CreateDirectory(unicodeDirectory);

        var ex = Record.Exception(() => DirectoryFsync.Sync(unicodeDirectory, strict: true));

        Assert.Null(ex);
    }

    // SyncFile

    /// <summary>Verifies SyncFile with null path returns without throwing.</summary>
    [Fact(DisplayName = "DirectoryFsync: SyncFile Null Path Is No-Op")]
    public void DirectoryFsync_SyncFile_NullPath_IsNoOp()
    {
        var ex = Record.Exception(() => DirectoryFsync.SyncFile(null!));
        Assert.Null(ex);
    }

    /// <summary>Verifies SyncFile with empty string returns without throwing.</summary>
    [Fact(DisplayName = "DirectoryFsync: SyncFile Empty Path Is No-Op")]
    public void DirectoryFsync_SyncFile_EmptyPath_IsNoOp()
    {
        var ex = Record.Exception(() => DirectoryFsync.SyncFile(string.Empty));
        Assert.Null(ex);
    }

    /// <summary>Verifies SyncFile with a non-existent path swallows the exception.</summary>
    [Fact(DisplayName = "DirectoryFsync: SyncFile Nonexistent Path Swallows Exception")]
    public void DirectoryFsync_SyncFile_NonexistentPath_SwallowsException()
    {
        var missing = Path.Combine(_dir, "does_not_exist.dat");
        var ex = Record.Exception(() => DirectoryFsync.SyncFile(missing));
        Assert.Null(ex);
    }

    /// <summary>Verifies SyncFile with a real file completes without throwing.</summary>
    [Fact(DisplayName = "DirectoryFsync: SyncFile Real File Completes")]
    public void DirectoryFsync_SyncFile_RealFile_Completes()
    {
        var ex = Record.Exception(() => DirectoryFsync.SyncFile(_file));
        Assert.Null(ex);
    }

    /// <summary>Verifies SyncFile strict with a real file completes without throwing.</summary>
    [Fact(DisplayName = "DirectoryFsync: SyncFile Strict Real File Completes")]
    public void DirectoryFsync_SyncFile_Strict_RealFile_Completes()
    {
        var ex = Record.Exception(() => DirectoryFsync.SyncFile(_file, strict: true));
        Assert.Null(ex);
    }

    /// <summary>Verifies SyncFile strict with a non-existent path propagates the exception.</summary>
    [Fact(DisplayName = "DirectoryFsync: SyncFile Strict Nonexistent Path Throws")]
    public void DirectoryFsync_SyncFile_Strict_NonexistentPath_Throws()
    {
        var missing = Path.Combine(_dir, "ghost.dat");
        Assert.ThrowsAny<IOException>(() => DirectoryFsync.SyncFile(missing, strict: true));
    }

    /// <summary>Verifies a Windows sync handle can coexist with a facade reader.</summary>
    [Fact(DisplayName = "DirectoryFsync: SyncFile Strict Completes With Shared Windows Reader")]
    public void DirectoryFsync_SyncFile_Strict_CompletesWithSharedWindowsReader()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        using var readHandle = FileOpenRetry.OpenReadDelete(_file);

        var ex = Record.Exception(() => DirectoryFsync.SyncFile(_file, strict: true));

        Assert.Null(ex);
    }
}
