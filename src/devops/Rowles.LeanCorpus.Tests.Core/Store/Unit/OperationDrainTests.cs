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

        var dispose = StartDedicatedDispose(drain.BeginDisposeAndWait);
        try
        {
            WaitUntilRejected(() => drain.Acquire(owner));
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

        var dispose = StartDedicatedDispose(drain.BeginDisposeAndWait);
        try
        {
            WaitUntilRejected(() => drain.Acquire(new object()));
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            scope.Dispose();
        }
        await dispose.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }

    private static Task StartDedicatedDispose(Action dispose) =>
        Task.Factory.StartNew(
            dispose,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private static void WaitUntilRejected(Func<IDisposable> acquire)
    {
        bool rejected = SpinWait.SpinUntil(
            () =>
            {
                try
                {
                    acquire().Dispose();
                    return false;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
            },
            TimeSpan.FromSeconds(5));

        Assert.True(rejected, "Disposal did not reject new operation leases.");
    }
}
