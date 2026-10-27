using System.Reflection;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Core.Foundation;

namespace Rowles.LeanCorpus.Tests.Core.Store;

[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
public sealed class MMapLifetimeTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "ll_mmap_lifetime_" + Guid.NewGuid().ToString("N"));

    [Fact(DisplayName = "IndexInput: Dispose Waits For Active Mapping Lease")]
    public void Dispose_ActiveMappingLease_WaitsBeforeUnmapping()
    {
        Directory.CreateDirectory(_path);
        string filePath = Path.Combine(_path, "input.bin");
        File.WriteAllBytes(filePath, [42]);
        var input = new IndexInput(filePath);
        var lease = input.AcquireLifetimeLease();

        using var dispose = new DedicatedThreadOperation(input.Dispose, "index-input-dispose");
        try
        {
            WaitUntilRejected(() => input.AcquireLifetimeLease());
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            lease.Dispose();
        }
        dispose.Join();
        Assert.Throws<ObjectDisposedException>(() => input.ReadByte());
    }

    [Fact(DisplayName = "MMap Directory: Dispose Waits For Active Input Mapping Lease")]
    public void Dispose_ActiveInputLease_WaitsBeforeClosingTrackedInput()
    {
        Directory.CreateDirectory(_path);
        using var directory = new MMapDirectory(_path);
        using (var output = directory.CreateOutput("input.bin"))
            output.WriteByte(42);
        var input = directory.OpenInput("input.bin");
        var lease = input.AcquireLifetimeLease();

        using var dispose = new DedicatedThreadOperation(directory.Dispose, "mmap-directory-dispose");
        try
        {
            WaitUntilRejected(() => directory.AcquireOperationLease());
            Assert.False(dispose.IsCompleted);
        }
        finally
        {
            lease.Dispose();
        }
        dispose.Join();
        Assert.Throws<ObjectDisposedException>(() => input.ReadByte());
        input.Dispose();
    }

    [Fact(DisplayName = "MMap Directory: Concurrent Input Disposal Joins Cleanup")]
    public void Dispose_ConcurrentInputAndDirectoryDisposal_WaitsForInputCleanup()
    {
        Directory.CreateDirectory(_path);
        var directory = new MMapDirectory(_path);
        using (var output = directory.CreateOutput("input.bin"))
            output.WriteByte(42);

        IndexInput input = directory.OpenInput("input.bin");
        var lease = input.AcquireLifetimeLease();
        DedicatedThreadOperation? inputDisposal = null;
        DedicatedThreadOperation? directoryDisposal = null;
        try
        {
            inputDisposal = new DedicatedThreadOperation(input.Dispose, "index-input-dispose");
            WaitUntilRejected(() => input.AcquireLifetimeLease());
            directoryDisposal = new DedicatedThreadOperation(directory.Dispose, "mmap-directory-dispose");
            WaitUntilRejected(() => directory.AcquireOperationLease());

            bool directoryReturnedEarly = SpinWait.SpinUntil(
                () => directoryDisposal.IsCompleted,
                TimeSpan.FromSeconds(1));
            Assert.False(directoryReturnedEarly,
                "Directory disposal returned while the competing input cleanup still held its mapping lease.");

            lease.Dispose();
            inputDisposal.Join();
            directoryDisposal.Join();

            Assert.True(input.ResourcesReleased);
            Assert.Equal(0, FileLifetimeRegistry.GetDiagnostics(_path).DirectoryOwners);
        }
        finally
        {
            lease.Dispose();
            JoinIgnoringFailure(inputDisposal);
            JoinIgnoringFailure(directoryDisposal);
            try { directory.Dispose(); }
            catch (Exception) { }
        }
    }

    [Fact(DisplayName = "MMap Directory: Concurrent Disposal Calls Share One Completion")]
    public void Dispose_ConcurrentDirectoryDisposals_ReleaseOwnerOnce()
    {
        Directory.CreateDirectory(_path);
        var directory = new MMapDirectory(_path);
        using (var output = directory.CreateOutput("input.bin"))
            output.WriteByte(42);
        IndexInput input = directory.OpenInput("input.bin");
        var lease = input.AcquireLifetimeLease();
        DedicatedThreadOperation? firstDisposal = null;
        DedicatedThreadOperation? secondDisposal = null;
        try
        {
            firstDisposal = new DedicatedThreadOperation(directory.Dispose, "mmap-directory-dispose-one");
            WaitUntilRejected(() => directory.AcquireOperationLease());
            secondDisposal = new DedicatedThreadOperation(directory.Dispose, "mmap-directory-dispose-two");

            Assert.False(SpinWait.SpinUntil(() => firstDisposal.IsCompleted, TimeSpan.FromSeconds(1)));
            Assert.False(SpinWait.SpinUntil(() => secondDisposal.IsCompleted, TimeSpan.FromMilliseconds(100)));

            lease.Dispose();
            firstDisposal.Join();
            secondDisposal.Join();

            Assert.True(input.ResourcesReleased);
            Assert.Equal(0, FileLifetimeRegistry.GetDiagnostics(_path).DirectoryOwners);
        }
        finally
        {
            lease.Dispose();
            JoinIgnoringFailure(firstDisposal);
            JoinIgnoringFailure(secondDisposal);
            try { directory.Dispose(); }
            catch (Exception) { }
        }
    }

    [Fact(DisplayName = "MMap Directory: Failed Input Cleanup Retains Ownership And Can Retry")]
    public void Dispose_FailedInputCleanup_RetainsOwnershipAndRetriesUnreleasedStages()
    {
        Directory.CreateDirectory(_path);
        var directory = new MMapDirectory(_path);
        using (var output = directory.CreateOutput("input.bin"))
            output.WriteByte(42);
        IndexInput input = directory.OpenInput("input.bin");
        var injectedFailure = new IOException("injected file-lease cleanup failure");
        using var cleanupEntered = new ManualResetEventSlim();
        using var continueCleanup = new ManualResetEventSlim();
        using var firstCompleted = new ManualResetEventSlim();
        using var secondCompleted = new ManualResetEventSlim();
        int injectFailure = 1;
        input.CleanupStageTestHook = stage =>
        {
            if (stage == IndexInputCleanupStage.ReleaseFileLease
                && Interlocked.Exchange(ref injectFailure, 0) == 1)
            {
                cleanupEntered.Set();
                if (!continueCleanup.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
                    throw new TimeoutException("Timed out waiting to publish the injected cleanup failure.");
                throw injectedFailure;
            }
        };

        Exception? firstFailure = null;
        Exception? secondFailure = null;
        Thread? firstThread = null;
        Thread? secondThread = null;
        try
        {
            firstThread = new Thread(() =>
            {
                try { directory.Dispose(); }
                catch (Exception exception) { firstFailure = exception; }
                finally { firstCompleted.Set(); }
            }) { IsBackground = true, Name = "mmap-directory-dispose-failing" };
            firstThread.Start();
            Assert.True(cleanupEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                "The first disposer did not reach injected cleanup.");

            secondThread = new Thread(() =>
            {
                try { directory.Dispose(); }
                catch (Exception exception) { secondFailure = exception; }
                finally { secondCompleted.Set(); }
            }) { IsBackground = true, Name = "mmap-directory-dispose-waiter" };
            secondThread.Start();
            bool secondWaitedForFirst = SpinWait.SpinUntil(
                () => (secondThread.ThreadState & ThreadState.WaitSleepJoin) != 0,
                TimeSpan.FromSeconds(5));
            Assert.True(secondWaitedForFirst, "The second disposer did not join the active cleanup attempt.");

            continueCleanup.Set();
            Assert.True(firstCompleted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                "The first disposer did not publish its failure.");
            Assert.True(secondCompleted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                "The waiting disposer did not observe the failure.");
            Assert.True(firstThread.Join(TimeSpan.FromSeconds(5)), "The first disposer thread did not join.");
            Assert.True(secondThread.Join(TimeSpan.FromSeconds(5)), "The waiting disposer thread did not join.");
            Assert.Same(injectedFailure, firstFailure);
            Assert.Same(injectedFailure, secondFailure);
            Assert.False(input.ResourcesReleased);
            Assert.Equal((1, 0), directory.GetOwnedResourceSnapshot());
            Assert.Equal(1, FileLifetimeRegistry.GetDiagnostics(_path).DirectoryOwners);
            Assert.Equal(1, FileLifetimeRegistry.GetDiagnostics(_path).FileLeases);

            input.CleanupStageTestHook = null;
            directory.Dispose();

            Assert.True(input.ResourcesReleased);
            Assert.Equal(0, FileLifetimeRegistry.GetDiagnostics(_path).DirectoryOwners);
            Assert.Equal(0, FileLifetimeRegistry.GetDiagnostics(_path).FileLeases);
        }
        finally
        {
            continueCleanup.Set();
            if (firstThread is { IsAlive: true }) firstThread.Join(TimeSpan.FromSeconds(10));
            if (secondThread is { IsAlive: true }) secondThread.Join(TimeSpan.FromSeconds(10));
            input.CleanupStageTestHook = null;
            try { directory.Dispose(); }
            catch (Exception) { }
        }
    }

    [Theory(DisplayName = "IndexInput: Failed Native Cleanup Retries Only Incomplete Stages")]
    [InlineData((int)IndexInputCleanupStage.ReleasePointer)]
    [InlineData((int)IndexInputCleanupStage.DisposeAccessor)]
    [InlineData((int)IndexInputCleanupStage.DisposeMemoryMappedFile)]
    public void Dispose_FailedNativeCleanupStage_RetryRepeatsOnlyIncompleteStages(int failingStageValue)
    {
        IndexInputCleanupStage failingStage = (IndexInputCleanupStage)failingStageValue;
        Directory.CreateDirectory(_path);
        string filePath = Path.Combine(_path, "native-cleanup.bin");
        File.WriteAllBytes(filePath, [42]);
        var input = new IndexInput(filePath);
        var injectedFailure = new IOException($"injected {failingStage} failure");
        var observedStages = new List<IndexInputCleanupStage>();
        int injectFailure = 1;
        input.CleanupStageTestHook = stage =>
        {
            observedStages.Add(stage);
            if (stage == failingStage && Interlocked.Exchange(ref injectFailure, 0) == 1)
                throw injectedFailure;
        };

        try
        {
            Assert.Same(injectedFailure, Assert.Throws<IOException>(input.Dispose));
            Assert.False(input.ResourcesReleased);

            input.Dispose();

            Assert.True(input.ResourcesReleased);
            IndexInputCleanupStage[] expectedStages = failingStage switch
            {
                IndexInputCleanupStage.ReleasePointer =>
                [IndexInputCleanupStage.ReleasePointer,
                 IndexInputCleanupStage.ReleasePointer,
                 IndexInputCleanupStage.DisposeAccessor,
                 IndexInputCleanupStage.DisposeMemoryMappedFile],
                IndexInputCleanupStage.DisposeAccessor =>
                [IndexInputCleanupStage.ReleasePointer,
                 IndexInputCleanupStage.DisposeAccessor,
                 IndexInputCleanupStage.DisposeAccessor,
                 IndexInputCleanupStage.DisposeMemoryMappedFile],
                IndexInputCleanupStage.DisposeMemoryMappedFile =>
                [IndexInputCleanupStage.ReleasePointer,
                 IndexInputCleanupStage.DisposeAccessor,
                 IndexInputCleanupStage.DisposeMemoryMappedFile,
                 IndexInputCleanupStage.DisposeMemoryMappedFile],
                _ => throw new ArgumentOutOfRangeException(nameof(failingStage))
            };
            Assert.Equal(expectedStages, observedStages);
        }
        finally
        {
            input.CleanupStageTestHook = null;
            try { input.Dispose(); }
            catch (Exception) { }
        }
    }

    [Fact(DisplayName = "IndexInput: Failed Owner Lease Release Retries Only The Owner Lease")]
    public void Dispose_FailedOwnerLeaseRelease_RetryDoesNotRepeatCompletedStages()
    {
        Directory.CreateDirectory(_path);
        string filePath = Path.Combine(_path, "owner-lease-cleanup.bin");
        File.WriteAllBytes(filePath, [42]);
        var owner = new IndexInput(filePath);
        IndexInput child = owner.OpenSharedSlice(0, 1);
        var injectedFailure = new IOException("injected owner-lease cleanup failure");
        var observedStages = new List<IndexInputCleanupStage>();
        int injectFailure = 1;
        child.CleanupStageTestHook = stage =>
        {
            observedStages.Add(stage);
            if (stage == IndexInputCleanupStage.ReleaseOwnerLease
                && Interlocked.Exchange(ref injectFailure, 0) == 1)
                throw injectedFailure;
        };

        try
        {
            Assert.Same(injectedFailure, Assert.Throws<IOException>(child.Dispose));
            Assert.False(child.ResourcesReleased);
            Assert.Equal([IndexInputCleanupStage.ReleaseOwnerLease], observedStages);
            Assert.Equal((byte)42, owner.ReadByte());

            child.Dispose();

            Assert.True(child.ResourcesReleased);
            Assert.Equal(
                [IndexInputCleanupStage.ReleaseOwnerLease, IndexInputCleanupStage.ReleaseOwnerLease],
                observedStages);
            owner.Dispose();
        }
        finally
        {
            child.CleanupStageTestHook = null;
            try { child.Dispose(); }
            catch (Exception) { }
            try { owner.Dispose(); }
            catch (Exception) { }
        }
    }

    [Fact(DisplayName = "IndexInput: Finaliser And Explicit Disposal Share Cleanup")]
    public async Task Dispose_FinaliserAndExplicitDisposal_JoinSameCleanup()
    {
        Directory.CreateDirectory(_path);
        string filePath = Path.Combine(_path, "input.bin");
        File.WriteAllBytes(filePath, [42]);
        var input = new IndexInput(filePath);
        var lease = input.AcquireLifetimeLease();
        using var cleanupEntered = new ManualResetEventSlim();
        using var continueCleanup = new ManualResetEventSlim();
        using var disposeStarted = new ManualResetEventSlim();
        int blockOnce = 1;
        input.CleanupStageTestHook = stage =>
        {
            if (stage == IndexInputCleanupStage.ReleasePointer && Interlocked.Exchange(ref blockOnce, 0) == 1)
            {
                cleanupEntered.Set();
                if (!continueCleanup.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Timed out waiting to resume finaliser cleanup.");
            }
        };

        MethodInfo finaliser = typeof(IndexInput).GetMethod("Finalize", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Exception? finaliserFailure = null;
        var finaliserThread = new Thread(() =>
        {
            try { finaliser.Invoke(input, null); }
            catch (Exception exception) { finaliserFailure = exception; }
        }) { IsBackground = true, Name = "index-input-finaliser-test" };
        Task? explicitDisposal = null;
        try
        {
            finaliserThread.Start();
            WaitUntilRejected(() => input.AcquireLifetimeLease());
            Assert.False(finaliserThread.Join(TimeSpan.FromMilliseconds(100)),
                "The finaliser returned while an admitted mapping lease was still active.");
            explicitDisposal = Task.Factory.StartNew(
                () =>
                {
                    disposeStarted.Set();
                    input.Dispose();
                },
                TestContext.Current.CancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
            Assert.True(disposeStarted.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                "Explicit disposal did not start.");
            await Assert.ThrowsAsync<TimeoutException>(async () =>
                await explicitDisposal.WaitAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));

            lease.Dispose();
            Assert.True(cleanupEntered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken),
                "The finaliser did not begin resource cleanup.");
            Assert.False(explicitDisposal.IsCompleted,
                "Explicit disposal returned while finaliser cleanup was incomplete.");
            continueCleanup.Set();
            Assert.True(finaliserThread.Join(TimeSpan.FromSeconds(5)), "The finaliser cleanup thread did not finish.");
            await explicitDisposal.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Null(finaliserFailure);
            Assert.True(input.ResourcesReleased);
        }
        finally
        {
            lease.Dispose();
            continueCleanup.Set();
            if (finaliserThread.IsAlive)
                finaliserThread.Join(TimeSpan.FromSeconds(10));
            if (finaliserThread.IsAlive)
                throw new TimeoutException("The finaliser cleanup thread did not terminate during cleanup.");
            if (explicitDisposal is not null)
                await explicitDisposal.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            input.CleanupStageTestHook = null;
            input.Dispose();
        }
    }

    [Fact(DisplayName = "IndexInput: Finaliser Joiner Cannot Corrupt A Retried Explicit Disposal")]
    public void Dispose_FinaliserJoinsFailedExplicitCleanup_RetryRemainsSuccessful()
    {
        Directory.CreateDirectory(_path);
        string filePath = Path.Combine(_path, "finaliser-retry.bin");
        File.WriteAllBytes(filePath, [42]);
        var input = new IndexInput(filePath);
        var operations = GetOperationDrain(input);
        var injectedFailure = new IOException("injected explicit pointer-release failure");
        using var cleanupEntered = new ManualResetEventSlim();
        using var continueCleanup = new ManualResetEventSlim();
        using var finaliserJoined = new ManualResetEventSlim();
        using var continueFinaliserWait = new ManualResetEventSlim();
        int injectFailure = 1;
        input.CleanupStageTestHook = stage =>
        {
            if (stage == IndexInputCleanupStage.ReleasePointer
                && Interlocked.Exchange(ref injectFailure, 0) == 1)
            {
                cleanupEntered.Set();
                if (!continueCleanup.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("Timed out before publishing the explicit cleanup failure.");
                throw injectedFailure;
            }
        };
        operations.BeforeDisposalAttemptWaitTestHook = () =>
        {
            finaliserJoined.Set();
            if (!continueFinaliserWait.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("Timed out before the finaliser observed its joined attempt.");
        };

        Exception? explicitFailure = null;
        Exception? finaliserFailure = null;
        var explicitThread = new Thread(() =>
        {
            try { input.Dispose(); }
            catch (Exception exception) { explicitFailure = exception; }
        }) { IsBackground = true, Name = "index-input-explicit-failing-dispose" };
        MethodInfo finaliser = typeof(IndexInput).GetMethod("Finalize", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var finaliserThread = new Thread(() =>
        {
            try { finaliser.Invoke(input, null); }
            catch (Exception exception) { finaliserFailure = exception; }
        }) { IsBackground = true, Name = "index-input-finaliser-joined-dispose" };

        try
        {
            explicitThread.Start();
            Assert.True(cleanupEntered.Wait(TimeSpan.FromSeconds(5)), "Explicit cleanup did not reach the injected failure.");
            finaliserThread.Start();
            Assert.True(finaliserJoined.Wait(TimeSpan.FromSeconds(5)), "The finaliser did not capture the active explicit attempt.");

            continueCleanup.Set();
            Assert.True(explicitThread.Join(TimeSpan.FromSeconds(5)), "The explicit cleanup owner did not finish.");
            Assert.Same(injectedFailure, explicitFailure);

            input.CleanupStageTestHook = null;
            input.Dispose();
            Assert.True(input.ResourcesReleased);

            continueFinaliserWait.Set();
            Assert.True(finaliserThread.Join(TimeSpan.FromSeconds(5)), "The finaliser did not finish observing its joined attempt.");
            Assert.Null(finaliserFailure);
            Assert.Null(operations.BeginDisposeAndWaitForCompletion());
            input.Dispose();
        }
        finally
        {
            continueCleanup.Set();
            continueFinaliserWait.Set();
            if (explicitThread.IsAlive)
                explicitThread.Join(TimeSpan.FromSeconds(10));
            if (finaliserThread.IsAlive)
                finaliserThread.Join(TimeSpan.FromSeconds(10));
            input.CleanupStageTestHook = null;
            operations.BeforeDisposalAttemptWaitTestHook = null;
            try { input.Dispose(); }
            catch (Exception) { }
        }
    }

    [Fact(DisplayName = "IndexInput: Public ReadSpan Remains Valid After Disposal")]
    public void ReadSpan_DisposedInput_ReturnedDataRemainsValid()
    {
        Directory.CreateDirectory(_path);
        string filePath = Path.Combine(_path, "span.bin");
        File.WriteAllBytes(filePath, [1, 2, 3]);
        var input = new IndexInput(filePath);

        ReadOnlySpan<byte> bytes = input.ReadSpan(3);
        input.Dispose();

        Assert.Equal([1, 2, 3], bytes.ToArray());
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

        Assert.True(rejected, "Disposal did not reject new mapping leases.");
    }

    private static void JoinIgnoringFailure(DedicatedThreadOperation? operation)
    {
        if (operation is null)
            return;

        try { operation.Dispose(); }
        catch (Exception) { }
    }

    private static OperationDrain GetOperationDrain(IndexInput input)
        => (OperationDrain)typeof(IndexInput)
            .GetField("_operations", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(input)!;

    public void Dispose()
    {
        try { Directory.Delete(_path, recursive: true); }
        catch { }
    }
}
