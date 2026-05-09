namespace Rowles.LeanLucene.Tests.Fixtures;

/// <summary>
/// Shared fixture that provisions a temporary directory for each test class
/// and tears it down afterwards.
/// </summary>
public sealed class TestDirectoryFixture : IDisposable
{
    public string Path { get; }

    public TestDirectoryFixture()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "LeanLucene_Tests",
            Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(Path))
        {
            // Force GC to release memory-mapped file handles before deletion
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            System.IO.Directory.Delete(Path, recursive: true);
        }
    }
}
