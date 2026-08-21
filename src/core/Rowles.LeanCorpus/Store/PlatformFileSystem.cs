namespace Rowles.LeanCorpus.Store;

/// <summary>Platform-specific durability and transient-error behaviour.</summary>
internal interface IPlatformFileSystem
{
    void SyncFile(string path);

    void SyncDirectory(string path);

    bool IsTransient(Exception exception);
}

/// <summary>Selects the filesystem implementation once for the current process.</summary>
internal static class PlatformFileSystem
{
    internal static IPlatformFileSystem Current { get; } = OperatingSystem.IsWindows()
        ? WindowsFileSystem.Instance
        : PosixFileSystem.Instance;
}
