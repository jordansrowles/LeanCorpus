namespace Rowles.LeanCorpus.Tests.Core.Infrastructure;

/// <summary>
/// Owns an isolated temporary directory for one state-machine execution.
/// </summary>
[Category(TestCategory.Chaos)]
[Area(TestArea.Foundation)]
internal sealed class StateMachineTestDirectory : IDisposable
{
    public StateMachineTestDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "LeanCorpus_StateMachine",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string CreateChildPath(string name)
    {
        string childPath = System.IO.Path.Combine(Path, $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(childPath);
        return childPath;
    }

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(Path);
}
