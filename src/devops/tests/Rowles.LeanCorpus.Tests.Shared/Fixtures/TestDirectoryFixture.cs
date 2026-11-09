namespace Rowles.LeanCorpus.Tests.Shared.Fixtures;

/// <summary>
/// Shared fixture that provisions a temporary directory for each test class
/// and tears it down afterwards.
/// </summary>
public sealed class TestDirectoryFixture : IDisposable
{
    public string Path { get; }

    public TestDirectoryFixture()
    {
        Path = RepositoryTemporaryStorage.CreateDirectoryPath("tests", "LeanCorpus_Tests_");
    }

    public void Dispose()
    {
        RepositoryTemporaryStorage.DeleteDirectory(Path);
    }

    /// <summary>
    /// Best-effort directory cleanup with a short retry loop for transient file locks.
    /// Does not throw — failures are silently ignored to avoid masking test assertions.
    /// </summary>
    public static void TryDeleteDirectory(string path)
    {
        if (!System.IO.Directory.Exists(path))
            return;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Rowles.LeanCorpus.Store.FileOpenRetry.DeleteDirectory(path, recursive: true);
                return;
            }
            catch
            {
                if (attempt < 2)
                    Thread.Sleep(50 * (attempt + 1));
            }
        }
    }
}
