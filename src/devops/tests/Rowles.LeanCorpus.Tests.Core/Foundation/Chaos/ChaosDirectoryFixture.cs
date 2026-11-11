namespace Rowles.LeanCorpus.Tests.Core.Infrastructure;

[Category(TestCategory.Chaos)]
[Area(TestArea.Foundation)]
public sealed class ChaosDirectoryFixture : IDisposable
{
    public string Path { get; }

    public ChaosDirectoryFixture()
    {
        Path = Rowles.LeanCorpus.Tests.Shared.Fixtures.RepositoryTemporaryStorage.CreateDirectoryPath("chaos", "LeanCorpus_Chaos_");
    }

    public void Dispose()
    {
        if (!Directory.Exists(Path))
            return;

        TestDirectoryFixture.TryDeleteDirectory(Path);
    }
}
