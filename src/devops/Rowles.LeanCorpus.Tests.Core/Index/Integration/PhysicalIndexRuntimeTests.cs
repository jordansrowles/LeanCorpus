namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class PhysicalIndexRuntimeTests
{
    [Fact]
    public async Task TerminalCloseAllowsImmediateLockRenameDeleteAndReopen()
    {
        string path = NewPath();
        try
        {
            for (int iteration = 0; iteration < 16; iteration++)
            {
                await using var runtime = Open(path);
                using (runtime.EnterOperation())
                    runtime.Writer.AddDocument(Document("orchard"));
                runtime.Commit(refresh: true);
                using (var lease = runtime.AcquireSearcher())
                    Assert.Equal(iteration + 1, lease.Searcher.Search(new TermQuery("body", "orchard"), 10, TestContext.Current.CancellationToken).TotalHits);

                await runtime.CloseAsync();
                await runtime.DisposeAsync();
                var closed = runtime.GetDiagnostics();
                Assert.Equal(PhysicalIndexRuntimeState.Closed, closed.State);
                Assert.Equal(0, closed.ActiveOperations);
                Assert.Equal(0, closed.ActiveSearcherLeases);
                Assert.False(closed.WriterOwned);
                Assert.False(closed.DirectoryOwned);
                Assert.Equal(PhysicalIndexWriterState.Closed, closed.WriterState);
                Assert.False(closed.WriterLockOwned);
                Assert.Equal(0, closed.ActiveInputs);
                Assert.Equal(0, closed.ActiveMappedViews);
                Assert.Equal(0, closed.DirectoryFileLeases);
                Assert.Equal(0, closed.PendingDeferredDeletes);
                Assert.Equal(0, closed.RetiredGenerations);
                Assert.Equal(0, closed.ActiveMerges);
                using (File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
                Directory.Move(path, path + "-closed");
                Directory.Move(path + "-closed", path);
            }
            Directory.Delete(path, recursive: true);
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task CloseDrainsBothCurrentAndRetiredSearcherLeases()
    {
        string path = NewPath();
        var runtime = Open(path);
        runtime.Writer.AddDocument(Document("first"));
        runtime.Commit(refresh: true);
        var oldLease = runtime.AcquireSearcher();
        runtime.Writer.AddDocument(Document("second"));
        runtime.Commit(refresh: true);
        var currentLease = runtime.AcquireSearcher();
        try
        {
            Task closing = runtime.CloseAsync().AsTask();
            Assert.Equal(PhysicalIndexRuntimeState.Quiescing, runtime.GetDiagnostics().State);
            Assert.Same(closing, runtime.CloseAsync().AsTask());
            Assert.False(closing.IsCompleted);
            Assert.Throws<ObjectDisposedException>(() => runtime.AcquireSearcher());
            Assert.Throws<ObjectDisposedException>(() => runtime.EnterOperation());
            Assert.Equal(1, oldLease.Searcher.Search(new MatchAllDocsQuery(), 10).TotalHits);
            Assert.Equal(2, currentLease.Searcher.Search(new MatchAllDocsQuery(), 10).TotalHits);
            Assert.Equal(2, runtime.GetDiagnostics().ActiveSearcherLeases);
            oldLease.Dispose();
            oldLease.Dispose();
            Assert.False(closing.IsCompleted);
            Assert.Equal(1, runtime.GetDiagnostics().ActiveSearcherLeases);
            currentLease.Dispose();
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(PhysicalIndexRuntimeState.Closed, runtime.GetDiagnostics().State);
        }
        finally
        {
            oldLease.Dispose();
            currentLease.Dispose();
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task CloseWaitsForAnAdmittedWholeOperation()
    {
        string path = NewPath();
        var runtime = Open(path);
        var operation = runtime.EnterOperation();
        try
        {
            Task closing = runtime.CloseAsync().AsTask();
            Assert.False(closing.IsCompleted);
            Assert.Equal(1, runtime.GetDiagnostics().ActiveOperations);
            Assert.Throws<ObjectDisposedException>(() => runtime.EnterOperation());
            runtime.Writer.AddDocument(Document("admitted"));
            operation.Dispose();
            operation.Dispose();
            await closing.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Equal(0, runtime.GetDiagnostics().ActiveOperations);
        }
        finally
        {
            operation.Dispose();
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void FailedSearcherOpenReleasesAnAlreadyAcquiredWriterLock()
    {
        string path = NewPath();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicalIndexRuntime(path,
                new IndexWriterConfig { DurableCommits = false },
                new SearcherManagerConfig { RefreshInterval = TimeSpan.Zero }));
            using (File.Open(Path.Combine(path, "write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)) { }
            Directory.Delete(path, recursive: true);
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task IndependentOwnersUseWriterExclusionAndCanReopenAfterClose()
    {
        string path = NewPath();
        var first = Open(path);
        try
        {
            Assert.Throws<WriteLockException>(() => Open(path));
            await first.CloseAsync();
            await using var reopened = Open(path);
            Assert.Equal(PhysicalIndexRuntimeState.Open, reopened.GetDiagnostics().State);
        }
        finally
        {
            await first.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task CancellingACloseWaitDoesNotReopenAdmissionOrAbandonCleanup()
    {
        string path = NewPath();
        var runtime = Open(path);
        var lease = runtime.AcquireSearcher();
        using var cancellation = new CancellationTokenSource();
        try
        {
            Task closing = runtime.CloseAsync(cancellation.Token).AsTask();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => closing);
            Assert.Equal(PhysicalIndexRuntimeState.Quiescing, runtime.GetDiagnostics().State);
            Assert.Throws<ObjectDisposedException>(() => runtime.EnterOperation());
            lease.Dispose();
            await runtime.CloseAsync();
            Assert.Equal(PhysicalIndexRuntimeState.Closed, runtime.GetDiagnostics().State);
        }
        finally
        {
            lease.Dispose();
            await runtime.DisposeAsync();
            Directory.Delete(path, recursive: true);
        }
    }

    private static PhysicalIndexRuntime Open(string path) => new(path,
        new IndexWriterConfig { DurableCommits = false },
        new SearcherManagerConfig { RefreshInterval = TimeSpan.FromHours(1) });

    private static string NewPath() => Path.Combine(Path.GetTempPath(), "leancorpus-physical-runtime", Guid.NewGuid().ToString("N"));

    private static LeanDocument Document(string value)
    {
        var document = new LeanDocument();
        document.Add(new TextField("body", value));
        return document;
    }
}
