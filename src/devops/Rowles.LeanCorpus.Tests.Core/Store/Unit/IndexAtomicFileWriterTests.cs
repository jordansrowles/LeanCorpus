using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Store;

/// <summary>Tests atomic file publication and temporary-file isolation.</summary>
[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class IndexAtomicFileWriterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "leancorpus_atomic_" + Guid.NewGuid().ToString("N"));

    public IndexAtomicFileWriterTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => TestDirectoryFixture.TryDeleteDirectory(_directory);

    /// <summary>Verifies a legacy fixed-name temporary file is neither used nor overwritten.</summary>
    [Fact(DisplayName = "IndexAtomicFileWriter: Uses Unique Temporary File")]
    public void Write_UsesUniqueTemporaryFile()
    {
        var target = Path.Combine(_directory, "segments_1");
        var legacyTemp = target + ".tmp";
        File.WriteAllText(legacyTemp, "sentinel");

        IndexAtomicFileWriter.WriteText(target, "published", durable: false);

        Assert.Equal("published", File.ReadAllText(target));
        Assert.Equal("sentinel", File.ReadAllText(legacyTemp));
        Assert.Empty(Directory.GetFiles(_directory, "segments_1.*.tmp"));
        DirtyFileTracker.Forget(target);
    }

    /// <summary>Verifies an already-synchronised atomic publication does not remain dirty.</summary>
    [Fact(DisplayName = "IndexAtomicFileWriter: Durable Publication Clears Exact Dirty Version")]
    public void Write_DurablePublication_ClearsDirtyVersion()
    {
        var target = Path.Combine(_directory, "segments_2");
        var before = Rowles.LeanCorpus.Diagnostics.FileSystemDiagnostics.GetSnapshot();

        IndexAtomicFileWriter.WriteText(target, "published", durable: true);

        var after = Rowles.LeanCorpus.Diagnostics.FileSystemDiagnostics.GetSnapshot();
        Assert.Empty(DirtyFileTracker.Snapshot(_directory, static _ => true));
        Assert.True(after.SyncOperationCount - before.SyncOperationCount >= 2);
        Assert.True(after.SyncElapsedMilliseconds >= before.SyncElapsedMilliseconds);
    }
}
