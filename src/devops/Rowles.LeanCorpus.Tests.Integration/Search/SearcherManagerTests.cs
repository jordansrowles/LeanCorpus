using System.Collections.Concurrent;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Simd;
using Rowles.LeanCorpus.Search.Parsing;
using Rowles.LeanCorpus.Search.Highlighting;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

/// <summary>
/// Contains unit tests for Searcher Manager.
/// </summary>
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
        TestDirectoryFixture.TryDeleteDirectory(_dir);
    }

    /// <summary>
    /// Verifies the Acquire: Returns Usable Searcher scenario.
    /// </summary>
    [Fact(DisplayName = "Acquire: Returns Usable Searcher")]
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

    /// <summary>
    /// Verifies the Using Searcher: Convenience Pattern scenario.
    /// </summary>
    [Fact(DisplayName = "Using Searcher: Convenience Pattern")]
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

    /// <summary>
    /// Verifies the Maybe Refresh: Detects New Commit scenario.
    /// </summary>
    [Fact(DisplayName = "Maybe Refresh: Detects New Commit")]
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

    /// <summary>
    /// Verifies the Maybe Refresh: Returns False When No New Commit scenario.
    /// </summary>
    [Fact(DisplayName = "Maybe Refresh: Returns False When No New Commit")]
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

    /// <summary>
    /// Verifies the Maybe Refresh: Returns False When Generation Changes Without Content Change scenario.
    /// </summary>
    [Fact(DisplayName = "Maybe Refresh: Returns False When Generation Changes Without Content Change")]
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

    /// <summary>
    /// Verifies the Held Searcher: Remains Usable After Refresh And Release scenario.
    /// </summary>
    [Fact(DisplayName = "Held Searcher: Remains Usable After Refresh And Release")]
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

    /// <summary>
    /// Verifies the Dispose: With Outstanding Acquire Keeps Held Searcher Usable Until Release scenario.
    /// </summary>
    [Fact(DisplayName = "Dispose: With Outstanding Acquire Keeps Held Searcher Usable Until Release")]
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

    /// <summary>
    /// Verifies the Release: Unknown Searcher Does Not Throw scenario.
    /// </summary>
    [Fact(DisplayName = "Release: Unknown Searcher Does Not Throw")]
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

    /// <summary>
    /// Verifies the Maybe Refresh Async: Works scenario.
    /// </summary>
    [Fact(DisplayName = "Maybe Refresh Async: Works")]
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

    /// <summary>
    /// Verifies the Dispose: Is Idempotent scenario.
    /// </summary>
    [Fact(DisplayName = "Dispose: Is Idempotent")]
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

    /// <summary>
    /// Verifies the Acquire: After Dispose Throws scenario.
    /// </summary>
    [Fact(DisplayName = "Acquire: After Dispose Throws")]
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
    [Fact(DisplayName = "Acquire: During Concurrent Refresh Never Returns Disposed Searcher", Timeout = 30_000)]
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

    /// <summary>
    /// Verifies the query cache survives searcher refreshes when content hasn't changed.
    /// </summary>
    [Fact(DisplayName = "QueryCache: Survives Searcher Refresh With Same Content")]
    public void QueryCache_SurvivesSearcherRefresh_WithSameContent()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("hello world"));
            w.Commit();
        }

        var config = new SearcherManagerConfig
        {
            RefreshInterval = TimeSpan.FromMinutes(5),
            SearcherConfig = new IndexSearcherConfig { EnableQueryCache = true }
        };
        using var mgr = new SearcherManager(dir, config);

        // First search populates the cache (first access is always a miss).
        var searcher1 = mgr.Acquire();
        try
        {
            searcher1.Search(new TermQuery("body", "hello"), 10);
        }
        finally { mgr.Release(searcher1); }

        long missesAfterFirst = mgr.UsingSearcher(s => s.Cache!.Misses);
        Assert.True(missesAfterFirst > 0,
            $"Cache should have recorded at least one miss after the first search. Misses: {missesAfterFirst}");

        // Force a refresh attempt — no content change, searcher stays the same, cache is not invalidated.
        mgr.MaybeRefresh();

        // Same query on a searcher acquired after refresh — should be a cache hit
        // because the cache survived the refresh.
        var searcher2 = mgr.Acquire();
        try
        {
            searcher2.Search(new TermQuery("body", "hello"), 10);
            Assert.True(searcher2.Cache!.Hits > 0,
                $"Cache should produce a hit after surviving the refresh. Hits: {searcher2.Cache.Hits}");
        }
        finally { mgr.Release(searcher2); }
    }

    /// <summary>
    /// Verifies the cache is invalidated when content changes (new commit with different content token).
    /// </summary>
    [Fact(DisplayName = "QueryCache: Invalidated When Content Changes")]
    public void QueryCache_InvalidatedWhenContentChanges()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("initial"));
            w.Commit();
        }

        var config = new SearcherManagerConfig
        {
            RefreshInterval = TimeSpan.FromMinutes(5),
            SearcherConfig = new IndexSearcherConfig { EnableQueryCache = true }
        };
        using var mgr = new SearcherManager(dir, config);

        // Populate the cache.
        var s1 = mgr.Acquire();
        try { s1.Search(new TermQuery("body", "initial"), 10); }
        finally { mgr.Release(s1); }

        long hitsBefore = mgr.UsingSearcher(s => s.Cache!.Hits);

        // Add a document and commit, changing content.
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("new document"));
            w.Commit();
        }

        Assert.True(mgr.MaybeRefresh());

        // Search again — cache should be invalidated, so we get a miss (and then a hit on the
        // next call for the same query from the new searcher).
        mgr.UsingSearcher<object?>(s =>
        {
            s.Search(new TermQuery("body", "initial"), 10);
            s.Search(new TermQuery("body", "initial"), 10);
            return null;
        });

        long hitsAfter = mgr.UsingSearcher(s => s.Cache!.Hits);
        Assert.True(hitsAfter > hitsBefore,
            $"Cache hits should increase after re-population. Before: {hitsBefore}, After: {hitsAfter}");
    }

    /// <summary>
    /// Verifies that cache hit rate is non-zero with a refreshing searcher
    /// (i.e. the cache actually caches across searches within the same searcher lifetime).
    /// </summary>
    [Fact(DisplayName = "QueryCache: Repeat Query Hits Cache Within Same Searcher")]
    public void QueryCache_RepeatQueryHitsCache_WithinSameSearcher()
    {
        var dir = new MMapDirectory(_dir);
        using (var w = new IndexWriter(dir, new IndexWriterConfig()))
        {
            w.AddDocument(Doc("hello world"));
            w.Commit();
        }

        var config = new SearcherManagerConfig
        {
            RefreshInterval = TimeSpan.FromMinutes(5),
            SearcherConfig = new IndexSearcherConfig { EnableQueryCache = true }
        };
        using var mgr = new SearcherManager(dir, config);

        mgr.UsingSearcher<object?>(s =>
        {
            // First search: cache miss.
            s.Search(new TermQuery("body", "hello"), 10);
            long misses1 = s.Cache!.Misses;
            long hits1 = s.Cache.Hits;

            // Second search with same query: cache hit.
            s.Search(new TermQuery("body", "hello"), 10);
            long misses2 = s.Cache.Misses;
            long hits2 = s.Cache.Hits;

            Assert.Equal(misses1, misses2);
            Assert.True(hits2 > hits1,
                $"Second search should be a cache hit, not a miss. Hits: {hits1} -> {hits2}");
            return null;
        });
    }

    private static LeanDocument Doc(string body)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        return doc;
    }
}
