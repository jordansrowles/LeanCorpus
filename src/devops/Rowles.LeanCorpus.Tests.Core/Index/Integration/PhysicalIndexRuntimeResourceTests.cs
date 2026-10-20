using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class PhysicalIndexRuntimeResourceTests
{
    [Fact]
    public async Task ResourceSnapshotTracksRetiredGenerationsAndReturnsToZeroAfterClose()
    {
        string path = NewPath();
        var runtime = Open(path);
        PhysicalIndexRuntime.PhysicalSearcherLease? old = null;
        PhysicalIndexRuntime.PhysicalSearcherLease? current = null;
        try
        {
            runtime.Writer.AddDocument(Document("first"));
            runtime.Commit(refresh: true);
            old = runtime.AcquireSearcher();
            runtime.Writer.AddDocument(Document("second"));
            runtime.Commit(refresh: true);
            current = runtime.AcquireSearcher();
            Assert.Equal(1, current.Searcher.Search(new TermQuery("body", "second"), 10, TestContext.Current.CancellationToken).TotalHits);
            var live = runtime.GetDiagnostics();
            Assert.True(live.CurrentGeneration > 0);
            Assert.Equal(1, live.RetiredGenerations);
            Assert.True(live.ActiveInputs > 0);
            Assert.True(live.ActiveMappedViews > 0);
            Assert.True(live.WriterLockOwned);
            Task closing = runtime.CloseAsync().AsTask();
            Assert.True(runtime.GetDiagnostics().PendingClose);
            Assert.False(closing.IsCompleted);
            old.Dispose();
            current.Dispose();
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var closed = runtime.GetDiagnostics();
            Assert.False(closed.PendingClose);
            Assert.False(closed.WriterLockOwned);
            Assert.Equal(0, closed.RetiredGenerations);
            Assert.Equal(0, closed.ActiveInputs);
            Assert.Equal(0, closed.ActiveMappedViews);
            Assert.Equal(0, closed.DirectoryFileLeases);
            Assert.Equal(0, closed.PendingDeferredDeletes);
            Assert.Equal(0, closed.ActiveMerges);
        }
        finally
        {
            old?.Dispose();
            current?.Dispose();
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task CloseWaitsForAnExecutingBackgroundRefreshWithoutBlockingItsCaller()
    {
        string path = NewPath();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var runtime = new PhysicalIndexRuntime(path, new IndexWriterConfig { DurableCommits = false }, new SearcherManagerConfig
        {
            RefreshInterval = TimeSpan.FromMilliseconds(1),
            RefreshStarting = () => { entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)); }
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task closing = runtime.CloseAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.True(runtime.GetDiagnostics().DirectoryOwned);
            Assert.Throws<ObjectDisposedException>(() => runtime.AcquireSearcher());
            release.Set();
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            release.Set();
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task CloseDrainsAnExecutingMergeAndItsSourceFileLeases()
    {
        string path = NewPath();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var runtime = Open(path, new IndexWriterConfig
        {
            DurableCommits = false, MaxBufferedDocs = 1, MergeThreshold = 2,
            MergeStarting = () => { entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)); }
        });
        try
        {
            runtime.Writer.AddDocument(Document("first"));
            runtime.Commit(refresh: true);
            runtime.Writer.AddDocument(Document("second"));
            runtime.Commit(refresh: true);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(runtime.GetDiagnostics().ActiveMerges > 0);
            Task closing = runtime.CloseAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.Throws<IOException>(() => File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None).Dispose());
            release.Set();
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(0, runtime.GetDiagnostics().ActiveMerges);
            Assert.Equal(0, runtime.GetDiagnostics().DirectoryFileLeases);
        }
        finally
        {
            release.Set();
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task UnresolvedDeferredDeletionPreventsTerminalSuccessAndANewOwnerCanRecoverAfterCleanup()
    {
        string path = NewPath();
        var runtime = Open(path);
        var state = FileLifetimeRegistry.ForDirectory(path);
        string retired = Path.Combine(path, "retired.bin");
        File.WriteAllText(retired, "retired input");
        using var lease = state.Acquire("retired.bin");
        try
        {
            state.Delete("retired.bin");
            File.Delete(retired);
            Directory.CreateDirectory(retired);
            lease.Dispose();
            await Assert.ThrowsAnyAsync<Exception>(() => runtime.CloseAsync().AsTask());
            Assert.Equal(PhysicalIndexRuntimeState.Faulted, runtime.GetDiagnostics().State);
            Assert.Equal(1, runtime.GetDiagnostics().PendingDeferredDeletes);
            Assert.False(runtime.GetDiagnostics().WriterLockOwned);
            Directory.Delete(retired);
            state.CompletePendingDeletes();
            await using var recovered = Open(path);
            Assert.Equal(PhysicalIndexRuntimeHealth.Healthy, recovered.GetDiagnostics().Health);
        }
        finally
        {
            if (Directory.Exists(retired)) Directory.Delete(retired);
            lease.Dispose();
            state.CompletePendingDeletes();
            try { await runtime.DisposeAsync(); } catch { /* Preserve the failed close result. */ }
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task CloseDrainsARealAdmittedWriteAtItsFlushSubmissionBarrier()
    {
        string path = NewPath();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var runtime = Open(path, new IndexWriterConfig
        {
            DurableCommits = false, MaxBufferedDocs = 1,
            FlushSubmissionReserved = () => { entered.TrySetResult(); Assert.True(release.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)); }
        });
        Task? writing = null;
        try
        {
            writing = Task.Run(() => { using var operation = runtime.EnterOperation(); runtime.Writer.AddDocument(Document("admitted")); }, TestContext.Current.CancellationToken);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Task closing = runtime.CloseAsync().AsTask();
            Assert.Equal(1, runtime.GetDiagnostics().ActiveOperations);
            Assert.False(closing.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => runtime.EnterOperation());
            release.Set();
            await writing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(runtime.GetDiagnostics().WriterLockOwned);
        }
        finally
        {
            release.Set();
            if (writing is not null) await writing;
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task FailedBackgroundMergePoisonsWritesAndRetainsTheCommittedReader()
    {
        string path = NewPath();
        var failure = new IOException("controlled background merge failure");
        var runtime = Open(path, new IndexWriterConfig
        {
            DurableCommits = false, MaxBufferedDocs = 1, MergeThreshold = 2,
            MergeStarting = () => throw failure
        });
        try
        {
            runtime.Writer.Config.CommitBeforePublication = _ => throw new IOException("earlier recoverable commit failure");
            runtime.Writer.AddDocument(Document("first"));
            Assert.Throws<IOException>(() => runtime.Commit(refresh: true));
            runtime.Writer.Config.CommitBeforePublication = null;
            runtime.Commit(refresh: true);
            runtime.Writer.AddDocument(Document("second"));
            runtime.Commit(refresh: true);
            Task? merging = runtime.Writer.MergeTask;
            Assert.NotNull(merging);
            await merging.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var failed = runtime.GetDiagnostics();
            Assert.Equal(PhysicalIndexRuntimeHealth.Faulted, failed.Health);
            Assert.True(failed.ReopenRequired);
            Assert.Same(failure, failed.LastFailure?.Exception);
            Assert.Throws<InvalidOperationException>(() => runtime.EnterOperation());
            using (var reader = runtime.AcquireSearcher())
                Assert.Equal(2, reader.Searcher.Search(new MatchAllDocsQuery(), 10, TestContext.Current.CancellationToken).TotalHits);
            await runtime.CloseAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    private static PhysicalIndexRuntime Open(string path, IndexWriterConfig? config = null) => new(path,
        config ?? new IndexWriterConfig { DurableCommits = false }, new SearcherManagerConfig { RefreshInterval = TimeSpan.FromHours(1) });
    private static LeanDocument Document(string value) { var document = new LeanDocument(); document.Add(new TextField("body", value)); return document; }
    private static string NewPath() => Path.Combine(Path.GetTempPath(), "leancorpus-runtime-resources", Guid.NewGuid().ToString("N"));
}
