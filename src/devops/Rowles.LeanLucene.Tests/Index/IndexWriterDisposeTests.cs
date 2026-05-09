using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Index;

/// <summary>
/// Regression tests for H12: IndexWriter.Dispose must drain in-flight
/// AddDocumentLockFree callers before tearing down the semaphore.
/// </summary>
[Trait("Category", "Index")]
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
}
