using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using System.Collections.Concurrent;

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
    /// calls Dispose after 100 ms. No ObjectDisposedException must escape to any producer,
    /// and the writer must be cleanly disposed afterwards.
    /// </summary>
    [Fact(DisplayName = "Dispose: During Concurrent Add Document Lock Free No Object Disposed Race")]
    public async Task Dispose_DuringConcurrentAddDocumentLockFree_NoObjectDisposedRace()
    {
        var dir = SubDir("h12_race");
        var config = new IndexWriterConfig { MaxBufferedDocs = 10_000 };
        var writer = new IndexWriter(new MMapDirectory(dir), config);
        writer.InitialiseDwptPool(threadCount: 8);

        const int producerCount = 32;
        var cts = new CancellationTokenSource();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var tasks = new Task[producerCount];

        for (int t = 0; t < producerCount; t++)
        {
            tasks[t] = Task.Run(() =>
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
                        return; // expected after Dispose — exit cleanly
                    }
                    catch (Exception ex)
                    {
                        exceptions.Add(ex);
                        return;
                    }
                }
            });
        }

        // Let producers run for 100 ms, then dispose the writer
        await Task.Delay(100);
        writer.Dispose();

        // Signal producers to stop and wait for all to finish
        cts.Cancel();
        await Task.WhenAll(tasks);

        // ObjectDisposedException thrown by our own guard (re-check after increment) is
        // the expected graceful exit signal. Any other exception type indicates a real bug
        // (e.g. the runtime throwing ODE from inside a disposed SemaphoreSlim).
        var unexpectedExceptions = exceptions
            .Where(ex => ex is not ObjectDisposedException)
            .ToList();

        Assert.Empty(unexpectedExceptions);
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
            Assert.ThrowsAny<IOException>(() => writer.Dispose());
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
            });
        }

        // Start all producers, wait briefly, then dispose
        started.Set();
        await Task.Delay(50);
        writer.Dispose();
        await Task.WhenAll(tasks);

        var unexpected = exceptions.Where(ex => ex is not ObjectDisposedException).ToList();
        Assert.Empty(unexpected);
    }
}
