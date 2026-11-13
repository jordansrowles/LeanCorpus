using Rowles.LeanCorpus.DevOps.Artifacts;

namespace Rowles.LeanCorpus.DevOps.Tests.Artifacts;

public sealed class ExecutionLeaseTests
{
    [Fact]
    public async Task AllowsIndependentCommandsToHoldSharedLeasesTogether()
    {
        using var root = TemporaryRoot.Create();

        await using var first = await ExecutionLease.AcquireSharedAsync(root.Path, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await using var second = await ExecutionLease.AcquireSharedAsync(root.Path, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        Assert.Equal(ExecutionLeaseKind.Shared, first.Kind);
        Assert.Equal(ExecutionLeaseKind.Shared, second.Kind);
    }

    [Fact]
    public async Task ExclusivePromotionWaitsForExistingReadersAndBlocksNewReaders()
    {
        using var root = TemporaryRoot.Create();
        await using var reader = await ExecutionLease.AcquireSharedAsync(root.Path, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        using var promotionCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var promotion = ExecutionLease.AcquireExclusiveAsync(root.Path, TimeSpan.FromSeconds(2), promotionCancellation.Token);

        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.False(promotion.IsCompleted);
        await reader.DisposeAsync();
        await using var exclusive = await promotion;

        var blockedReader = ExecutionLease.AcquireSharedAsync(root.Path, TimeSpan.FromMilliseconds(150), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<TimeoutException>(() => blockedReader);
        Assert.Equal(ExecutionLeaseKind.Exclusive, exclusive.Kind);
    }

    [Fact]
    public async Task RemovesExclusiveGateAfterTimeoutAndHonoursCancellation()
    {
        using var root = TemporaryRoot.Create();
        await using var reader = await ExecutionLease.AcquireSharedAsync(root.Path, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ExecutionLease.AcquireExclusiveAsync(root.Path, TimeSpan.FromSeconds(2), cancellation.Token));

        var acquired = await ExecutionLease.AcquireSharedAsync(root.Path, TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        await acquired.DisposeAsync();
    }

    [Fact]
    public async Task CancelledMarkerCreationLeavesNoMarkerBehind()
    {
        // A cancelled write after the marker file exists must not leave an orphaned marker: an
        // orphaned promotion gate blocks every later shared lease until it times out.
        using var root = TemporaryRoot.Create();
        string path = Path.Combine(root.Path, "promotion.lock");
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExecutionLease.CreateMarkerAsync(path, cancellation.Token));
        Assert.False(File.Exists(path));

        await ExecutionLease.CreateMarkerAsync(path, TestContext.Current.CancellationToken);
        Assert.True(File.Exists(path));
    }

    private sealed class TemporaryRoot : IDisposable
    {
        internal TemporaryRoot(string path) => Path = path;

        public string Path { get; }

        public static TemporaryRoot Create()
        {
            var path = RepositoryTemporaryStorage.NewPath("devops-tests", "DevOps lease ");
            Directory.CreateDirectory(path);
            return new TemporaryRoot(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
