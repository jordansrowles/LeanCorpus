using System.Collections.Concurrent;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index.Indexer;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Search.Searcher;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search;

public sealed class SearcherManagerTests : IDisposable
{
    private readonly string _dir;

    public SearcherManagerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"ll_smgr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Acquire_ReturnsUsableSearcher()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("hello world"));
            w.Commit();
        }

        using var mgr = new SearcherManager(dir, new SearcherManagerConfig { RefreshInterval = TimeSpan.FromMinutes(5) });
        var searcher = mgr.Acquire();
        try
        {
            var results = searcher.Search(new TermQuery("body", "hello"), 10);
            Assert.Equal(1, results.TotalHits);
        }
        finally
        {
            mgr.Release(searcher);
        }
    }

    [Fact]
    public void UsingSearcher_ConveniencePattern()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("test"));
            w.Commit();
        }

        using var mgr = new SearcherManager(dir, new SearcherManagerConfig { RefreshInterval = TimeSpan.FromMinutes(5) });
        var hits = mgr.UsingSearcher(s => s.Search(new TermQuery("body", "test"), 10).TotalHits);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void MaybeRefresh_DetectsNewCommit()
    {
        var dir = new MMapDirectory(_dir);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        writer.AddDocument(Doc("first"));
        writer.Commit();

        using var mgr = new SearcherManager(dir, new SearcherManagerConfig { RefreshInterval = TimeSpan.FromMinutes(5) });
        var before = mgr.UsingSearcher(s => s.Stats.TotalDocCount);

        writer.AddDocument(Doc("second"));
        writer.Commit();

        bool refreshed = mgr.MaybeRefresh();
        Assert.True(refreshed);

        var after = mgr.UsingSearcher(s => s.Stats.TotalDocCount);
        Assert.Equal(2, after);
    }

    [Fact]
    public void MaybeRefresh_ReturnsFalse_WhenNoNewCommit()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("test"));
            w.Commit();
        }

        using var mgr = new SearcherManager(dir);
        bool refreshed = mgr.MaybeRefresh();
        Assert.False(refreshed);
    }

    [Fact]
    public void MaybeRefresh_ReturnsFalse_WhenGenerationChangesWithoutContentChange()
    {
        var dir = new MMapDirectory(_dir);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        writer.AddDocument(Doc("first"));
        writer.Commit();

        using var mgr = new SearcherManager(dir, new SearcherManagerConfig { RefreshInterval = TimeSpan.FromMinutes(5) });
        writer.Commit();

        Assert.False(mgr.MaybeRefresh());

        writer.AddDocument(Doc("second"));
        writer.Commit();

        Assert.True(mgr.MaybeRefresh());
    }

    [Fact]
    public void HeldSearcher_RemainsUsableAfterRefreshAndRelease()
    {
        var dir = new MMapDirectory(_dir);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        writer.AddDocument(Doc("first"));
        writer.Commit();

        using var mgr = new SearcherManager(dir, new SearcherManagerConfig { RefreshInterval = TimeSpan.FromMinutes(5) });
        var held = mgr.Acquire();
        try
        {
            writer.AddDocument(Doc("second"));
            writer.Commit();

            Assert.True(mgr.MaybeRefresh());
            Assert.Equal(1, held.Search(new TermQuery("body", "first"), 10).TotalHits);
            Assert.Equal(0, held.Search(new TermQuery("body", "second"), 10).TotalHits);
            Assert.Equal(2, mgr.UsingSearcher(s => s.Search(new TermQuery("body", "first"), 10).TotalHits
                + s.Search(new TermQuery("body", "second"), 10).TotalHits));
        }
        finally
        {
            mgr.Release(held);
        }
    }

    [Fact]
    public void Dispose_WithOutstandingAcquire_KeepsHeldSearcherUsableUntilRelease()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            writer.AddDocument(Doc("held"));
            writer.Commit();
        }

        var mgr = new SearcherManager(dir);
        var held = mgr.Acquire();

        mgr.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mgr.Acquire());
        Assert.Equal(1, held.Search(new TermQuery("body", "held"), 10).TotalHits);

        mgr.Release(held);
    }

    [Fact]
    public void Release_UnknownSearcher_DoesNotThrow()
    {
        var dir = new MMapDirectory(_dir);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            writer.AddDocument(Doc("known"));
            writer.Commit();
        }

        using var mgr = new SearcherManager(dir);
        using var unknownSearcher = new IndexSearcher(dir);

        mgr.Release(unknownSearcher);
    }

    [Fact]
    public async Task MaybeRefreshAsync_Works()
    {
        var dir = new MMapDirectory(_dir);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());

        writer.AddDocument(Doc("first"));
        writer.Commit();

        using var mgr = new SearcherManager(dir);

        writer.AddDocument(Doc("second"));
        writer.Commit();

        bool refreshed = await mgr.MaybeRefreshAsync();
        Assert.True(refreshed);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("test"));
            w.Commit();
        }

        var mgr = new SearcherManager(dir);
        mgr.Dispose();
        mgr.Dispose(); // should not throw
    }

    [Fact]
    public void Acquire_AfterDispose_Throws()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("test"));
            w.Commit();
        }

        var mgr = new SearcherManager(dir);
        mgr.Dispose();

        Assert.Throws<ObjectDisposedException>(() => mgr.Acquire());
    }

    /// <summary>
    /// Regression test for C3: verifies that concurrent Acquire/Release calls never receive
    /// a disposed <see cref="IndexSearcher"/> while a refresh thread is swapping in new ones.
    /// </summary>
    [Fact(Timeout = 30_000)]
    public async Task Acquire_DuringConcurrentRefresh_NeverReturnsDisposedSearcher()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("initial"));
            w.Commit();
        }

        using var mgr = new SearcherManager(dir);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var errors = new ConcurrentBag<Exception>();

        // 64 workers: Acquire → read Stats → Release in a tight loop
        var workers = Enumerable.Range(0, 64).Select(_ => Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                IndexSearcher? searcher = null;
                try
                {
                    searcher = mgr.Acquire();
                    _ = searcher.Stats.TotalDocCount;
                    _ = searcher.Search(new TermQuery("body", "initial"), 10).TotalHits;
                }
                catch (ObjectDisposedException ex)
                {
                    errors.Add(ex);
                    return;
                }
                catch (OperationCanceledException) { return; }
                finally
                {
                    if (searcher is not null)
                        mgr.Release(searcher);
                }
                await Task.Yield();
            }
        })).ToArray();

        // Refresh thread: commits a new document then calls MaybeRefresh every ~10 ms
        var refreshTask = Task.Run(async () =>
        {
            int i = 0;
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    using (var w = new IndexWriter(dir, new IndexWriterConfig()))
                    {
                        w.AddDocument(Doc($"refresh{i++}"));
                        w.Commit();
                    }
                    mgr.MaybeRefresh();
                    await Task.Delay(10, cts.Token);
                }
                catch (OperationCanceledException) { return; }
                catch (IOException) { /* Transient; skip this commit cycle */ }
                catch (InvalidDataException) { /* Transient; skip this commit cycle */ }
            }
        });

        await Task.WhenAll(workers.Append(refreshTask));

        Assert.Empty(errors);
        var finalCount = mgr.UsingSearcher(s => s.Stats.TotalDocCount);
        Assert.True(finalCount >= 1, $"Expected at least 1 document; got {finalCount}");
    }

    private static LeanDocument Doc(string body)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        return doc;
    }
}
