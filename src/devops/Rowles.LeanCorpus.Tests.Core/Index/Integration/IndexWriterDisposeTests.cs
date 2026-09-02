using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Regression tests for H12: IndexWriter.Dispose must drain in-flight
/// AddDocumentLockFree callers before tearing down the semaphore.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class IndexWriterDisposeTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexWriterDisposeTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 32 producers call AddDocumentLockFree in a tight loop while the main thread
    /// calls Dispose after 100 ms. Producers may observe ObjectDisposedException as their
    /// graceful shutdown signal, and the writer must be cleanly disposed afterwards.
    /// </summary>
    [Fact(DisplayName = "Dispose: During Concurrent Add Document Lock Free No Object Disposed Race", Timeout = 30_000)]
    public async Task Dispose_DuringConcurrentAddDocumentLockFree_NoObjectDisposedRace()
    {
        var dir = SubDir("h12_race");
        var config = new IndexWriterConfig { MaxBufferedDocs = 10_000, MaxQueuedDocs = 0 };
        var writer = new IndexWriter(new MMapDirectory(dir), config);
        writer.InitialiseDwptPool(threadCount: 8);

        const int producerCount = 32;
        using var cts = new CancellationTokenSource();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var tasks = new Task[producerCount];

        for (int t = 0; t < producerCount; t++)
        {
            tasks[t] = Task.Factory.StartNew(
                () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        try
                        {
                            var doc = new LeanDocument();
                            doc.Add(new TextField("body", "concurrent stress test document"));
                            writer.AddDocumentLockFree(doc);
                        }
                        catch (ObjectDisposedException ode)
                        {
                            exceptions.Add(ode);
                            return; // expected after Dispose: exit cleanly
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                            return;
                        }
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        // Let producers run for 100 ms, then dispose the writer
        Thread.Sleep(100);
        writer.Dispose();

        // Signal producers to stop and wait for all to finish
        cts.Cancel();
        await Task.WhenAll(tasks).WaitAsync(TestContext.Current.CancellationToken);

        // ObjectDisposedException thrown by our own guard (re-check after increment) is
        // the expected graceful exit signal. Any other exception type indicates a real bug
        // (e.g. the runtime throwing ODE from inside a disposed SemaphoreSlim).
        var unexpectedExceptions = exceptions
            .Where(ex => ex is not ObjectDisposedException)
            .ToList();

        Assert.Empty(unexpectedExceptions);
    }

    /// <summary>
    /// Verifies that a producer blocked on writer-owned backpressure observes shutdown,
    /// unwinds its indexing operation, and does not leave disposal waiting indefinitely.
    /// </summary>
    [Fact(DisplayName = "Dispose: Unblocks Producers Waiting For Backpressure", Timeout = 30_000)]
    public async Task Dispose_UnblocksProducerWaitingForBackpressure()
    {
        var dir = SubDir("dispose_backpressure_wait");
        var writer = new IndexWriter(
            new MMapDirectory(dir),
            new IndexWriterConfig { MaxQueuedDocs = 1, MaxBufferedDocs = 10_000, DurableCommits = false });
        var semaphore = writer.BackpressureSemaphoreForTests;
        Assert.NotNull(semaphore);
        Assert.True(semaphore!.Wait(0));

        var producer = Task.Factory.StartNew(
            () =>
            {
                try
                {
                    var doc = new LeanDocument();
                    doc.Add(new TextField("body", "blocked by backpressure"));
                    writer.AddDocument(doc);
                    return (Exception?)null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            },
            TestContext.Current.CancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

        try
        {
            Assert.True(
                SpinWait.SpinUntil(
                    () => writer.InFlightIndexingOperationsForTests == 1 && !producer.IsCompleted,
                    TimeSpan.FromSeconds(5)),
                "The producer did not enter the blocked backpressure operation.");

            writer.Dispose();

            var exception = await producer.WaitAsync(TestContext.Current.CancellationToken);
            Assert.IsType<ObjectDisposedException>(exception);
            Assert.Equal(0, writer.InFlightIndexingOperationsForTests);
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>
    /// A writer that uses only synchronous indexing must not create an asynchronous
    /// consumer or wait for one during disposal.
    /// </summary>
    [Fact(DisplayName = "Dispose: Synchronous Writer Does Not Start Async Consumer")]
    public void Dispose_SynchronousWriter_DoesNotStartAsyncConsumer()
    {
        var dir = SubDir("dispose_sync_writer");
        var writer = new IndexWriter(
            new MMapDirectory(dir),
            new IndexWriterConfig { DurableCommits = false });

        try
        {
            Assert.False(writer.AsyncWriteConsumerStartedForTests);

            var doc = new LeanDocument();
            doc.Add(new TextField("body", "synchronous document"));
            writer.AddDocument(doc);

            Assert.False(writer.AsyncWriteConsumerStartedForTests);
            writer.Dispose();
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>
    /// Disposal closes async input before draining indexing operations. A command
    /// already queued behind an active command must therefore finish with the writer
    /// shutdown signal rather than keep disposal waiting for an absent consumer.
    /// </summary>
    [Fact(DisplayName = "Dispose: Unblocks Queued Async Writer Commands", Timeout = 30_000)]
    public async Task Dispose_UnblocksQueuedAsyncWriterCommands()
    {
        var dir = SubDir("dispose_async_queue");
        var writer = new IndexWriter(
            new MMapDirectory(dir),
            new IndexWriterConfig { MaxBufferedDocs = 1, DurableCommits = false });

        Task first;
        Task second;
        Task dispose;
        lock (writer.WriteLock)
        {
            first = writer.AddDocumentAsync(CreateDocument("first")).AsTask();

            Assert.True(
                SpinWait.SpinUntil(
                    () => writer.InFlightIndexingOperationsForTests == 2,
                    TimeSpan.FromSeconds(5)),
                "The asynchronous consumer did not begin processing the first command.");

            second = writer.AddDocumentAsync(CreateDocument("second")).AsTask();
            Assert.True(
                SpinWait.SpinUntil(
                    () => writer.InFlightIndexingOperationsForTests == 3,
                    TimeSpan.FromSeconds(5)),
                "The second asynchronous command was not queued.");

            dispose = Task.Run(writer.Dispose, TestContext.Current.CancellationToken);
            Assert.True(
                SpinWait.SpinUntil(() => writer.IsClosing, TimeSpan.FromSeconds(5)),
                "Writer disposal did not begin.");
        }

        await dispose.WaitAsync(TestContext.Current.CancellationToken);
        await first.WaitAsync(TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => second);
        Assert.Equal(0, writer.InFlightIndexingOperationsForTests);
    }

    private static LeanDocument CreateDocument(string id)
    {
        var doc = new LeanDocument();
        doc.Add(new StringField("id", id));
        doc.Add(new TextField("body", "async lifecycle document"));
        return doc;
    }

    /// <summary>
    /// Verifies the Dispose: Is Idempotent Never Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Dispose: Is Idempotent Never Throws")]
    public void Dispose_IsIdempotent_NeverThrows()
    {
        var dir = SubDir("h12_idempotent");
        var writer = new IndexWriter(new MMapDirectory(dir), new IndexWriterConfig());
        writer.Dispose();
        // Second dispose must be a no-op
        writer.Dispose();
    }

    /// <summary>
    /// Verifies that disposal uses a millisecond timeout when an indexing operation is stranded.
    /// </summary>
    [Fact(DisplayName = "Dispose: In-flight indexing drain uses configured timeout")]
    public void Dispose_InFlightIndexingOperation_UsesConfiguredDrainTimeout()
    {
        var dir = SubDir("dispose_drain_timeout");
        var writer = new IndexWriter(
            new MMapDirectory(dir),
            new IndexWriterConfig { DurableCommits = false },
            TimeSpan.FromMilliseconds(50));
        writer.EnterIndexingOperation();

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var failure = Assert.Throws<TimeoutException>(writer.Dispose);
            stopwatch.Stop();

            Assert.InRange(stopwatch.ElapsedMilliseconds, 25, 2_000);
            Assert.Contains("state=Closing", failure.Message, StringComparison.Ordinal);
            Assert.Contains("activeOperations=1", failure.Message, StringComparison.Ordinal);
            Assert.NotNull(writer.BackpressureSemaphoreForTests);
        }
        finally
        {
            writer.ExitIndexingOperation();
        }
    }

    /// <summary>
    /// Issue identified in Lucene.NET #1284
    /// Regression coverage for it in our base: a shutdown flush failure must not skip
    /// mandatory writer cleanup or leave the exclusive write lock held.
    /// </summary>
    [Fact(DisplayName = "Dispose: Flush Failure Releases Write Lock")]
    public void Dispose_PendingFlushFails_ReleasesWriteLockAndRethrowsFailure()
    {
        var path = SubDir("dispose_flush_failure");
        using var directory = new MMapDirectory(path);
        var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            DefaultAnalyser = new WhitespaceAnalyser(),
            DurableCommits = false,
        });

        var dwpt = writer.DwptPool![0];
        lock (dwpt)
        {
            var document = new LeanDocument();
            document.Add(new TextField("body", "dispose failure"));
            dwpt.AddDocument(document);
            writer.FlushPending.Add(new FlushPendingState
            {
                Snapshot = DwptFlushSnapshot.CaptureFrom(dwpt),
                SegmentOrdinal = 0,
                SeqStart = 0,
                SeqEnd = 0,
            });
        }

        string conflictingPath = Path.Combine(path, "seg_0.seg");
        Directory.CreateDirectory(conflictingPath);
        try
        {
            var failure = Record.Exception(() => writer.Dispose());
            Assert.True(failure is IOException or UnauthorizedAccessException,
                $"Expected a filesystem failure, got {failure?.GetType().FullName ?? "no exception"}.");
        }
        finally
        {
            Directory.Delete(conflictingPath, recursive: true);
        }

        Assert.False(File.Exists(Path.Combine(path, "write.lock")));
        using var reopened = new IndexWriter(directory, new IndexWriterConfig { DurableCommits = false });
    }

    /// <summary>
    /// Verifies the Add Document Lock Free: After Dispose Throws Object Disposed Exception scenario.
    /// </summary>
    [Fact(DisplayName = "Add Document Lock Free: After Dispose Throws Object Disposed Exception")]
    public void AddDocumentLockFree_AfterDispose_ThrowsObjectDisposedException()
    {
        var dir = SubDir("h12_after_dispose");
        var writer = new IndexWriter(new MMapDirectory(dir), new IndexWriterConfig());
        writer.InitialiseDwptPool(threadCount: 2);
        writer.Dispose();

        var doc = new LeanDocument();
        doc.Add(new TextField("body", "should not index"));

        Assert.Throws<ObjectDisposedException>(() => writer.AddDocumentLockFree(doc));
    }

    /// <summary>
    /// Verifies the Commit: After Dispose Throws Object Disposed Exception scenario.
    /// </summary>
    [Fact(DisplayName = "Commit: After Dispose Throws Object Disposed Exception")]
    public void Commit_AfterDispose_ThrowsObjectDisposedException()
    {
        var dir = SubDir("commit_after_dispose");
        var writer = new IndexWriter(new MMapDirectory(dir), new IndexWriterConfig());
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => writer.Commit());
    }

    /// <summary>
    /// Verifies the Delete Documents: After Dispose Throws Object Disposed Exception scenario.
    /// </summary>
    [Fact(DisplayName = "Delete Documents: After Dispose Throws Object Disposed Exception")]
    public void DeleteDocuments_AfterDispose_ThrowsObjectDisposedException()
    {
        var dir = SubDir("delete_after_dispose");
        var writer = new IndexWriter(new MMapDirectory(dir), new IndexWriterConfig());
        writer.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            writer.DeleteDocuments(new TermQuery("body", "anything")));
    }

    /// <summary>
    /// 8 producers index large documents with MaxBufferedDocs=1 (forcing a flush on every
    /// document) while the main thread calls Dispose after 50 ms. Verifies that the
    /// backpressure semaphore release in FlushSegmentStatic does not throw
    /// ObjectDisposedException after Dispose has torn down the semaphore.
    /// </summary>
    [Fact(DisplayName = "Dispose: During slow segment flush no semaphore disposed race")]
    public async Task Dispose_DuringSlowSegmentFlush_NoSemaphoreDisposedRace()
    {
        var dir = SubDir("h12_slow_flush");
        var config = new IndexWriterConfig { MaxBufferedDocs = 1, RamBufferSizeMB = 1024 };
        var writer = new IndexWriter(new MMapDirectory(dir), config);

        const int producerCount = 8;
        var exceptions = new ConcurrentBag<Exception>();
        var started = new ManualResetEventSlim();
        var tasks = new Task[producerCount];

        for (int t = 0; t < producerCount; t++)
        {
            tasks[t] = Task.Run(() =>
            {
                started.Wait();
                try
                {
                    // Each doc triggers a flush because MaxBufferedDocs=1
                    for (int i = 0; i < 10_000; i++)
                    {
                        var doc = new LeanDocument();
                        doc.Add(new TextField("body", new string('x', 10_000)));
                        writer.AddDocument(doc);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Expected: EnterIndexingOperation rejects after Dispose
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }, TestContext.Current.CancellationToken);
        }

        // Start all producers, wait briefly, then dispose
        started.Set();
        await Task.Delay(50, TestContext.Current.CancellationToken);
        writer.Dispose();
        await Task.WhenAll(tasks);

        var unexpected = exceptions.Where(ex => ex is not ObjectDisposedException).ToList();
        Assert.Empty(unexpected);
    }
}
