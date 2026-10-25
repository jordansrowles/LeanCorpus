using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Store;

[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class DeferredDeletionFailureTests
{
    [Fact]
    public void FailedDeferredDeleteRetainsItsPendingOwnershipUntilCleanupSucceeds()
    {
        string root = Path.Combine(Path.GetTempPath(), "leancorpus-deferred-failure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "retired.bin");
        File.WriteAllText(path, "retired input");
        var state = FileLifetimeRegistry.ForDirectory(root);
        using var lease = state.Acquire("retired.bin");
        try
        {
            state.Delete("retired.bin");
            File.Delete(path);
            Directory.CreateDirectory(path);
            lease.Dispose();
            Assert.Equal(["retired.bin"], state.GetPendingDeletionFiles([path]));
            Directory.Delete(path);
            File.WriteAllText(path, "delete is now possible");
            state.Delete("retired.bin");
            Assert.Empty(state.GetPendingDeletionFiles([path]));
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path);
            lease.Dispose();
            state.Delete("retired.bin");
            Directory.Delete(root, recursive: true);
        }
    }
}
