using Rowles.LeanLucene.Store;
using System.Runtime.InteropServices;
using System.Text;

namespace Rowles.LeanLucene.Tests.Store;

/// <summary>
/// Unit tests for <see cref="DirectoryFsync"/> covering the Windows-observable paths:
/// null/empty short-circuits, the Windows no-op in Sync, SyncFile with a
/// non-existent path (swallowed), and SyncFile with a real file.
/// The Unix fsync path is not exercised on Windows.
/// DirectoryFsync is internal but accessible via InternalsVisibleTo.
/// </summary>
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
        try { Directory.Delete(_dir, true); } catch { }
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

    /// <summary>Verifies Sync with a valid directory path is a no-op on Windows.</summary>
    [Fact(DisplayName = "DirectoryFsync: Sync Valid Path Is No-Op On Windows")]
    public void DirectoryFsync_Sync_ValidPath_IsNoOpOnWindows()
    {
        var ex = Record.Exception(() => DirectoryFsync.Sync(_dir));
        Assert.Null(ex);
    }

    /// <summary>Verifies Sync strict mode with a valid directory path does not throw on Windows.</summary>
    [Fact(DisplayName = "DirectoryFsync: Sync Strict Valid Path Does Not Throw On Windows")]
    public void DirectoryFsync_Sync_Strict_ValidPath_DoesNotThrowOnWindows()
    {
        var ex = Record.Exception(() => DirectoryFsync.Sync(_dir, strict: true));
        Assert.Null(ex);
    }

    [Fact(DisplayName = "DirectoryFsync: SyncCore Non Windows Encodes Null Terminated Path")]
    public void DirectoryFsync_SyncCore_NonWindows_EncodesNullTerminatedPath()
    {
        byte[]? capturedPath = null;
        int capturedLength = -1;
        var closed = false;

        DirectoryFsync.SyncCore(
            "café",
            strict: true,
            isWindows: false,
            openDirectory: (pathBytes, byteCount) =>
            {
                capturedPath = pathBytes[..(byteCount + 1)];
                capturedLength = byteCount;
                return 42;
            },
            syncDirectory: fileDescriptor =>
            {
                Assert.Equal(42, fileDescriptor);
                return 0;
            },
            closeDirectory: fileDescriptor =>
            {
                Assert.Equal(42, fileDescriptor);
                closed = true;
                return 0;
            },
            getLastError: () => 0);

        Assert.True(closed);
        Assert.NotNull(capturedPath);
        Assert.Equal(Encoding.UTF8.GetByteCount("café"), capturedLength);
        Assert.Equal(0, capturedPath[^1]);
        Assert.Equal("café", Encoding.UTF8.GetString(capturedPath.AsSpan(0, capturedLength)));
    }

    [Fact(DisplayName = "DirectoryFsync: SyncCore Non Windows Non Strict Open Failure Is No-Op")]
    public void DirectoryFsync_SyncCore_NonWindows_NonStrictOpenFailure_IsNoOp()
    {
        var synced = false;
        var closed = false;

        var ex = Record.Exception(() => DirectoryFsync.SyncCore(
            _dir,
            strict: false,
            isWindows: false,
            openDirectory: (_, _) => -1,
            syncDirectory: _ =>
            {
                synced = true;
                return 0;
            },
            closeDirectory: _ =>
            {
                closed = true;
                return 0;
            },
            getLastError: () => 123));

        Assert.Null(ex);
        Assert.False(synced);
        Assert.False(closed);
    }

    [Fact(DisplayName = "DirectoryFsync: SyncCore Non Windows Strict Open Failure Throws")]
    public void DirectoryFsync_SyncCore_NonWindows_StrictOpenFailure_Throws()
    {
        var ex = Assert.Throws<IOException>(() => DirectoryFsync.SyncCore(
            _dir,
            strict: true,
            isWindows: false,
            openDirectory: (_, _) => -1,
            syncDirectory: _ => 0,
            closeDirectory: _ => 0,
            getLastError: () => 123));

        Assert.Contains("errno 123", ex.Message, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "DirectoryFsync: SyncCore Non Windows Non Strict Fsync Failure Is No-Op")]
    public void DirectoryFsync_SyncCore_NonWindows_NonStrictFsyncFailure_IsNoOp()
    {
        var closed = false;

        var ex = Record.Exception(() => DirectoryFsync.SyncCore(
            _dir,
            strict: false,
            isWindows: false,
            openDirectory: (_, _) => 7,
            syncDirectory: _ => -1,
            closeDirectory: fileDescriptor =>
            {
                Assert.Equal(7, fileDescriptor);
                closed = true;
                return 0;
            },
            getLastError: () => 456));

        Assert.Null(ex);
        Assert.True(closed);
    }

    [Fact(DisplayName = "DirectoryFsync: SyncCore Non Windows Strict Fsync Failure Throws After Close")]
    public void DirectoryFsync_SyncCore_NonWindows_StrictFsyncFailure_ThrowsAfterClose()
    {
        var closed = false;

        var ex = Assert.Throws<IOException>(() => DirectoryFsync.SyncCore(
            _dir,
            strict: true,
            isWindows: false,
            openDirectory: (_, _) => 8,
            syncDirectory: _ => -1,
            closeDirectory: fileDescriptor =>
            {
                Assert.Equal(8, fileDescriptor);
                closed = true;
                return 0;
            },
            getLastError: () => 456));

        Assert.True(closed);
        Assert.Contains("errno 456", ex.Message, StringComparison.Ordinal);
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

    /// <summary>Verifies SyncFile strict falls back to a read-only handle on Windows when write access is blocked.</summary>
    [Fact(DisplayName = "DirectoryFsync: SyncFile Strict Read Only Open Completes On Windows")]
    public void DirectoryFsync_SyncFile_Strict_ReadOnlyOpen_CompletesOnWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return;

        using var readHandle = new FileStream(_file, FileMode.Open, FileAccess.Read, FileShare.Read);

        var ex = Record.Exception(() => DirectoryFsync.SyncFile(_file, strict: true));

        Assert.Null(ex);
    }
}
