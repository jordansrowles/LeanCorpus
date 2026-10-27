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

    [Fact(DisplayName = "Operation Drain: Waiter Observes The Attempt It Joined Across Retry")]
    public void BeginDisposeAndWaitForCompletion_WaiterKeepsJoinedFailureWhenRetryCompletesFirst()
    {
        var drain = new OperationDrain();
        using var waiterJoined = new ManualResetEventSlim();
        using var allowWaiter = new ManualResetEventSlim();
        drain.BeforeDisposalAttemptWaitTestHook = () =>
        {
            waiterJoined.Set();
            if (!allowWaiter.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The joined disposal waiter was not released.");
        };

        OperationDrain.DisposalAttempt firstAttempt = Assert.IsType<OperationDrain.DisposalAttempt>(
            drain.BeginDisposeAndWaitForCompletion());
        var injectedFailure = new IOException("first cleanup attempt failed");
        Exception? waiterFailure = null;
        var waiter = new Thread(() =>
        {
            try { drain.BeginDisposeAndWaitForCompletion(); }
            catch (Exception exception) { waiterFailure = exception; }
        }) { IsBackground = true, Name = "operation-drain-waiter" };
        OperationDrain.DisposalAttempt? retryAttempt = null;

        try
        {
            waiter.Start();
            Assert.True(waiterJoined.Wait(TimeSpan.FromSeconds(5)), "The waiter did not capture the first cleanup attempt.");

            drain.CompleteDisposalWithFailure(firstAttempt, injectedFailure);
            retryAttempt = Assert.IsType<OperationDrain.DisposalAttempt>(drain.BeginDisposeAndWaitForCompletion());
            drain.CompleteDisposalSuccessfully(retryAttempt);
            allowWaiter.Set();

            Assert.True(waiter.Join(TimeSpan.FromSeconds(5)), "The waiter did not finish after its joined attempt completed.");
            Assert.Same(injectedFailure, waiterFailure);
            Assert.Null(drain.BeginDisposeAndWaitForCompletion());
        }
        finally
        {
            if (!firstAttempt.IsCompleted)
                drain.CompleteDisposalWithFailure(firstAttempt, injectedFailure);
            if (retryAttempt is { IsCompleted: false })
                drain.CompleteDisposalSuccessfully(retryAttempt);
            allowWaiter.Set();
            if (waiter.IsAlive)
                waiter.Join(TimeSpan.FromSeconds(10));
            drain.BeforeDisposalAttemptWaitTestHook = null;
        }
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
