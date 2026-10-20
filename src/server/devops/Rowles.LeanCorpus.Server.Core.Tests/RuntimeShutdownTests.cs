using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Core.Execution;
using Rowles.LeanCorpus.Server.Core.Runtime;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class RuntimeShutdownTests
{
    [Fact]
    public async Task RuntimeDisposalWaitsForARetainedSearcherBeforeReleasingItsWriter()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        var runtime = handle.Runtime;
        var lease = runtime.Searchers.AcquireLease();
        Task? closing = null;
        try
        {
            closing = handle.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.Throws<IOException>(() => File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None).Dispose());
            lease.Dispose();
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            using (File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally
        {
            lease.Dispose();
            if (closing is not null) await closing;
            await handle.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task NewDurabilityWaiterAfterCoordinatorDisposalFailsInsteadOfHanging()
    {
        string path = NewPath();
        using var runtime = Open(path);
        try
        {
            runtime.Commits.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
                await runtime.Commits.WaitUntilCommittedAsync(1, TestContext.Current.CancellationToken)
                    .AsTask().WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
        }
        finally
        {
            runtime.Dispose();
            Directory.Delete(path, recursive: true);
        }
    }

    private static IndexRuntime Open(string path) => new(path,
        CompiledIndexSchema.Create(
            new IndexSchema([new IndexFieldDefinition("body", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
            new IndexTopologySettings(1, 0),
            new MutableIndexSettings(null, null, "body", null)),
        TimeSpan.FromHours(1), TimeSpan.FromHours(1));

    [Fact]
    public async Task HeldCommitSnapshotPinsThePhysicalRuntimeUntilReleased()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        handle.Runtime.Writer.Commit();
        CommitSnapshotLease snapshot = await handle.AcquireCommitSnapshotAsync(TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(1, handle.Health.ActiveSnapshotLeases);
            var ownership = handle.GetLifecycleDiagnostics();
            Assert.Equal(handle.Descriptor.Id.Value, ownership.PhysicalIndexId);
            Assert.Equal(ownership.RuntimeId, ownership.Physical.RuntimeId);
            Assert.Equal(1, ownership.ActiveSnapshotLeases);
            Task closing = handle.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.Throws<IOException>(() => File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None).Dispose());
            snapshot.Dispose();
            snapshot.Dispose();
            Assert.Equal(0, handle.Health.ActiveSnapshotLeases);
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            snapshot.Dispose();
            await handle.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task ClosingHandleRejectsQueuedOperationsAndDrainsTheAdmittedOperation()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        IDisposable admitted = await handle.EnterOperationAsync(TestContext.Current.CancellationToken);
        try
        {
            Task<IDisposable> queued = handle.EnterOperationAsync(TestContext.Current.CancellationToken).AsTask();
            Task closing = handle.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await handle.EnterOperationAsync(TestContext.Current.CancellationToken));
            admitted.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(async () => await queued);
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            admitted.Dispose();
            await handle.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task RuntimeCloseJoinsAnExecutingCommitTimerCallback()
    {
        string path = NewPath();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var runtime = new IndexRuntime(path,
            CompiledIndexSchema.Create(
                new IndexSchema([new IndexFieldDefinition("body", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                new IndexTopologySettings(1, 0), new MutableIndexSettings(null, null, "body", null)),
            TimeSpan.FromMilliseconds(1), TimeSpan.FromHours(1),
            onCommitted: _ =>
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken))
                    throw new TimeoutException("The controlled commit callback was not released.");
                return ValueTask.CompletedTask;
            });
        try
        {
            runtime.MarkWrite();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task closing = runtime.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            release.Set();
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            using (File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally
        {
            release.Set();
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotReadStreamRemainsPinnedAfterItsParentLeaseIsDisposed()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        handle.Runtime.Writer.Commit();
        var snapshot = await handle.AcquireCommitSnapshotAsync(TestContext.Current.CancellationToken);
        Stream? stream = null;
        try
        {
            string file = snapshot.Manifest.Files.First().FileName;
            stream = snapshot.OpenRead(file);
            snapshot.Dispose();
            Assert.Equal(1, handle.Health.ActiveSnapshotLeases);
            var ownership = handle.GetLifecycleDiagnostics();
            Assert.Equal(handle.Descriptor.Id.Value, ownership.PhysicalIndexId);
            Assert.Equal(ownership.RuntimeId, ownership.Physical.RuntimeId);
            Assert.Equal(1, ownership.ActiveSnapshotLeases);
            Task closing = handle.DisposeAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.True(await stream.ReadAsync(new byte[1], TestContext.Current.CancellationToken) > 0);
            await stream.DisposeAsync();
            stream.Dispose();
            Assert.Equal(0, handle.Health.ActiveSnapshotLeases);
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            using (File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
        }
        finally
        {
            if (stream is not null) await stream.DisposeAsync();
            snapshot.Dispose();
            await handle.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task SnapshotReadStreamCannotBeOpenedAfterItsLeaseIsDisposed()
    {
        string path = NewPath();
        var handle = OpenHandle(path);
        handle.Runtime.Writer.Commit();
        var snapshot = await handle.AcquireCommitSnapshotAsync(TestContext.Current.CancellationToken);
        try
        {
            string file = snapshot.Manifest.Files.First().FileName;
            snapshot.Dispose();
            Assert.Throws<ObjectDisposedException>(() => { using Stream unexpected = snapshot.OpenRead(file); });
        }
        finally
        {
            snapshot.Dispose();
            await handle.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    private static LocalIndexHandle OpenHandle(string path) => new(new LocalIndexDescriptor(PhysicalIndexId.New(),
        new IndexSchema([new IndexFieldDefinition("body", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
        "lifecycle-schema", new MutableIndexSettings(null, null, "body", null)),
        path, TimeSpan.FromHours(1), TimeSpan.FromHours(1), LocalIndexOpenMode.ReadWrite);

    private static string NewPath() => Path.Combine(Path.GetTempPath(), "leancorpus-server-runtime-shutdown", Guid.NewGuid().ToString("N"));
}
