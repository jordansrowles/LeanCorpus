namespace Rowles.LeanCorpus.Tests.Chaos.Infrastructure;

public sealed class ChaosDirectoryFixture : IDisposable
{
    public string Path { get; }

    public ChaosDirectoryFixture()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "LeanCorpus_Chaos",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (!Directory.Exists(Path))
            return;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Directory.Delete(Path, recursive: true);
    }
}
