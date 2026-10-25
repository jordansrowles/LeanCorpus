using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Core.Foundation;

namespace Rowles.LeanCorpus.Tests.Core.Store;

[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class OperationDrainTests
{
    [Fact(DisplayName = "Operation Drain: Dispose Waits For Active Copied Lease")]
    public void BeginDisposeAndWait_ActiveCopiedLease_WaitsForSingleRelease()
    {
        var drain = new OperationDrain();
        var owner = new object();
        var lease = drain.Acquire(owner);
        var copy = lease;

        using var dispose = new DedicatedThreadOperation(drain.BeginDisposeAndWait, "operation-drain-dispose");
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
        dispose.Join();

        Assert.Throws<ObjectDisposedException>(() => drain.Acquire(owner));
    }

    [Fact(DisplayName = "Operation Drain: Short Scope Blocks Disposal")]
    public void Enter_ActiveScope_BlocksDisposalUntilScopeEnds()
    {
        var drain = new OperationDrain();
        var scope = drain.Enter(new object());

        using var dispose = new DedicatedThreadOperation(drain.BeginDisposeAndWait, "operation-scope-dispose");
        try
        {
            WaitUntilRejected(() => drain.Acquire(new object()));
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            scope.Dispose();
        }
        dispose.Join();
    }

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
