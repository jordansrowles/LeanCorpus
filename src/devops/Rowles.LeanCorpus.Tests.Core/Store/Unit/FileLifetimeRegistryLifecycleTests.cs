using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Store;

[Category(TestCategory.Unit)]
[Area(TestArea.Store)]
[Collection(MMapDirectoryCurrentDirectoryCollection.Name)]
public sealed class FileLifetimeRegistryLifecycleTests
{
    [Fact]
    public void DistinctDirectoryChurnReturnsRegistryToItsMeasuredBaseline()
    {
        int baseline = FileLifetimeRegistry.RegisteredDirectoryCount;
        string root = Path.Combine(Path.GetTempPath(), "leancorpus-directory-churn", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            for (int index = 0; index < 256; index++)
            {
                string path = Path.Combine(root, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using (new MMapDirectory(path)) { }
                Assert.False(FileLifetimeRegistry.IsRegistered(path));
                Directory.Delete(path);
            }
            Assert.Equal(baseline, FileLifetimeRegistry.RegisteredDirectoryCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LastDirectoryOwnerWaitsForRetainedSnapshotAndSettlesDeferredDelete()
    {
        string path = Path.Combine(Path.GetTempPath(), "leancorpus-directory-snapshot", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        string filePath = Path.Combine(path, "retained.bin");
        File.WriteAllBytes(filePath, [1, 2, 3]);
        var directory = new MMapDirectory(path);
        var secondDirectory = new MMapDirectory(path);
        var state = directory.FileLifetimeStateForTesting;
        Assert.Same(state, secondDirectory.FileLifetimeStateForTesting);
        FileSnapshotLease snapshot = state.AcquireSnapshot(["retained.bin"]);
        try
        {
            state.Delete("retained.bin");
            directory.Dispose();
            Assert.Equal(1, FileLifetimeRegistry.GetDiagnostics(path).DirectoryOwners);
            secondDirectory.Dispose();
            Assert.True(FileLifetimeRegistry.IsRegistered(path));
            Assert.Equal(0, FileLifetimeRegistry.GetDiagnostics(path).DirectoryOwners);
            Assert.Equal(1, FileLifetimeRegistry.GetDiagnostics(path).FileLeases);

            snapshot.Dispose();
            Assert.False(File.Exists(filePath));
            Assert.False(FileLifetimeRegistry.IsRegistered(path));
        }
        finally
        {
            snapshot.Dispose();
            directory.Dispose();
            secondDirectory.Dispose();
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void FailedDeferredDeleteRemainsRegisteredUntilLaterCleanupSucceeds()
    {
        string path = Path.Combine(Path.GetTempPath(), "leancorpus-directory-delete-failure", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        string filePath = Path.Combine(path, "retired.bin");
        File.WriteAllText(filePath, "old input");
        var directory = new MMapDirectory(path);
        var state = directory.FileLifetimeStateForTesting;
        FileLease lease = state.Acquire("retired.bin");
        try
        {
            state.Delete("retired.bin");
            File.Delete(filePath);
            Directory.CreateDirectory(filePath);
            lease.Dispose();
            directory.Dispose();
            Assert.True(FileLifetimeRegistry.IsRegistered(path));
            Assert.Equal(1, FileLifetimeRegistry.GetDiagnostics(path).PendingDeletes);

            Directory.Delete(filePath);
            File.WriteAllText(filePath, "replacement");
            state.CompletePendingDeletes();
            Assert.False(File.Exists(filePath));
            Assert.False(FileLifetimeRegistry.IsRegistered(path));
        }
        finally
        {
            lease.Dispose();
            directory.Dispose();
            if (Directory.Exists(filePath)) Directory.Delete(filePath);
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task RegistrationDuringLastOwnerEvictionUsesOneReplacementState()
    {
        string path = Path.Combine(Path.GetTempPath(), "leancorpus-directory-eviction-race", Guid.NewGuid().ToString("N"));
        var first = new MMapDirectory(path);
        var oldState = first.FileLifetimeStateForTesting;
        var evictionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseEviction = new ManualResetEventSlim();
        int hookCalls = 0;
        oldState.BeforeEvictionForTesting = () =>
        {
            if (Interlocked.Increment(ref hookCalls) == 1)
            {
                evictionEntered.TrySetResult();
                Assert.True(releaseEviction.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
            }
        };
        Task? disposing = null;
        MMapDirectory? second = null;
        try
        {
            disposing = Task.Run(first.Dispose, TestContext.Current.CancellationToken);
            await evictionEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task<MMapDirectory> opening = Task.Run(() => new MMapDirectory(path), TestContext.Current.CancellationToken);
            releaseEviction.Set();
            await disposing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            second = await opening.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.NotSame(oldState, second.FileLifetimeStateForTesting);
            Assert.Same(second.FileLifetimeStateForTesting, FileLifetimeRegistry.ForDirectory(path));
            Assert.Equal(1, FileLifetimeRegistry.GetDiagnostics(path).DirectoryOwners);
        }
        finally
        {
            releaseEviction.Set();
            if (disposing is not null) await disposing;
            first.Dispose();
            second?.Dispose();
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task QueuedFinaliserDeleteCannotRemoveARecreatedFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "leancorpus-finaliser-delete-aba", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        const string name = "segment.tmp";
        string filePath = Path.Combine(path, name);
        File.WriteAllText(filePath, "retired generation");
        var directory = new MMapDirectory(path);
        var state = directory.FileLifetimeStateForTesting;
        FileLease lease = state.Acquire(name);
        var callbackEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseCallback = new ManualResetEventSlim();
        state.BeforeFinaliserDeleteCallbackForTesting = () =>
        {
            callbackEntered.TrySetResult();
            Assert.True(releaseCallback.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        };
        state.AfterFinaliserDeleteCallbackForTesting = () => callbackFinished.TrySetResult();
        try
        {
            state.Delete(name);
            lease.ReleaseFromFinaliser();
            await callbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            state.PrepareForWrite(name);
            File.WriteAllText(filePath, "new generation");
            releaseCallback.Set();
            await callbackFinished.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal("new generation", File.ReadAllText(filePath));
        }
        finally
        {
            releaseCallback.Set();
            lease.Dispose();
            directory.Dispose();
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }
}
