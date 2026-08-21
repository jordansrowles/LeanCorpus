using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Store;

[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class OperationDrainTests
{
    [Fact(DisplayName = "Operation Drain: Dispose Waits For Active Copied Lease")]
    public async Task BeginDisposeAndWait_ActiveCopiedLease_WaitsForSingleRelease()
    {
        var drain = new OperationDrain();
        var owner = new object();
        var lease = drain.Acquire(owner);
        var copy = lease;

        var dispose = Task.Run(drain.BeginDisposeAndWait);
        try
        {
            await WaitUntilRejectedAsync(() => drain.Acquire(owner));
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            copy.Dispose();
            lease.Dispose();
        }
        await dispose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Throws<ObjectDisposedException>(() => drain.Acquire(owner));
    }

    [Fact(DisplayName = "Operation Drain: Short Scope Blocks Disposal")]
    public async Task Enter_ActiveScope_BlocksDisposalUntilScopeEnds()
    {
        var drain = new OperationDrain();
        var scope = drain.Enter(new object());

        var dispose = Task.Run(drain.BeginDisposeAndWait);
        try
        {
            await WaitUntilRejectedAsync(() => drain.Acquire(new object()));
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            scope.Dispose();
        }
        await dispose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static async Task WaitUntilRejectedAsync(Func<IDisposable> acquire)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
            timeout.Token, TestContext.Current.CancellationToken);
        while (true)
        {
            try
            {
                acquire().Dispose();
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            await Task.Delay(1, cancellation.Token);
        }
    }
}
