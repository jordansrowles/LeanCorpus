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

    [Fact(DisplayName = "Operation Drain: Invocation Admitted Before Attempt Selection Joins The Original Attempt")]
    public void BeginDisposeAndWaitForCompletion_InvocationAdmittedBeforeSelectionKeepsOriginalFailureAcrossRetry()
    {
        var drain = new OperationDrain();
        OperationDrain.DisposalAttempt firstAttempt = Assert.IsType<OperationDrain.DisposalAttempt>(
            drain.BeginDisposeAndWaitForCompletion());
        var injectedFailure = new IOException("first cleanup attempt failed");
        using var waiterAdmitted = new ManualResetEventSlim();
        using var continueWaiter = new ManualResetEventSlim();
        int selectionHookEntries = 0;
        drain.BeforeDisposalAttemptSelectionTestHook = () =>
        {
            if (Interlocked.Exchange(ref selectionHookEntries, 1) != 0)
                return;
            waiterAdmitted.Set();
            if (!continueWaiter.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The admitted disposal caller was not released.");
        };

        Exception? waiterFailure = null;
        var waiter = new Thread(() =>
        {
            try { drain.BeginDisposeAndWaitForCompletion(); }
            catch (Exception exception) { waiterFailure = exception; }
        }) { IsBackground = true, Name = "operation-drain-admitted-caller" };
        OperationDrain.DisposalAttempt? retryAttempt = null;

        try
        {
            waiter.Start();
            Assert.True(waiterAdmitted.Wait(TimeSpan.FromSeconds(5)),
                "The second caller did not reach the admission barrier before attempt selection.");

            drain.CompleteDisposalWithFailure(firstAttempt, injectedFailure);
            retryAttempt = Assert.IsType<OperationDrain.DisposalAttempt>(drain.BeginDisposeAndWaitForCompletion());
            drain.CompleteDisposalSuccessfully(retryAttempt);
            continueWaiter.Set();

            Assert.True(waiter.Join(TimeSpan.FromSeconds(5)), "The admitted caller did not finish.");
            Assert.Same(injectedFailure, waiterFailure);
            Assert.Null(drain.BeginDisposeAndWaitForCompletion());
        }
        finally
        {
            if (!firstAttempt.IsCompleted)
                drain.CompleteDisposalWithFailure(firstAttempt, injectedFailure);
            if (retryAttempt is { IsCompleted: false })
                drain.CompleteDisposalSuccessfully(retryAttempt);
            continueWaiter.Set();
            if (waiter.IsAlive)
                waiter.Join(TimeSpan.FromSeconds(10));
            drain.BeforeDisposalAttemptSelectionTestHook = null;
        }
    }

    [Fact(DisplayName = "Operation Drain: Call After Failed Attempt Owns The Retry")]
    public void BeginDisposeAndWaitForCompletion_CallAfterFailureStartsRetry()
    {
        var drain = new OperationDrain();
        OperationDrain.DisposalAttempt firstAttempt = Assert.IsType<OperationDrain.DisposalAttempt>(
            drain.BeginDisposeAndWaitForCompletion());
        var injectedFailure = new IOException("first cleanup attempt failed");

        drain.CompleteDisposalWithFailure(firstAttempt, injectedFailure);

        OperationDrain.DisposalAttempt retryAttempt = Assert.IsType<OperationDrain.DisposalAttempt>(
            drain.BeginDisposeAndWaitForCompletion());
        Assert.NotSame(firstAttempt, retryAttempt);
        drain.CompleteDisposalSuccessfully(retryAttempt);

        Assert.Null(drain.BeginDisposeAndWaitForCompletion());
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
