using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Reproduction test for GitHub issue: IndexWriter.Commit() throws FileNotFoundException
/// when SearcherManager holds segment references across many incremental commit cycles.
/// See: ADR007 (merge-must-not-block-commit) and the segment-file lifecycle under churn.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class CommitSearcherRaceReproTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;
    private readonly ITestOutputHelper _output;

    public CommitSearcherRaceReproTests(TestDirectoryFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Reproduction for: IndexWriter.Commit() → FileNotFoundException for segment file
    /// after many incremental commit cycles with interleaved searcher leases.
    ///
    /// The trigger is concurrent reads via a SearcherManager interleaved with per-operation
    /// commits: the searcher leases pin segments so the merger cannot reclaim them in step
    /// with the deletion policy, and a later Commit() scans a segment whose files have
    /// already been removed by a background merge running on Windows.
    ///
    /// On Linux the race is benign (mmap + unlink keeps files alive); this test
    /// primarily validates the scenario doesn't crash on any platform.
    /// </summary>
    [Fact(DisplayName = "Commit + SearcherManager loop: no FileNotFoundException after many cycles")]
    public void CommitWithSearcherManagerLoop_NoFileNotFoundException()
    {
        var dirPath = SubDir("commit_searcher_race");
        using var dir = new MMapDirectory(dirPath);
        using var writer = new IndexWriter(dir, new IndexWriterConfig { DurableCommits = false });
        using var manager = new SearcherManager(dir, null);

        int iterations = 200;
        int errors = 0;
        var caughtExceptions = new List<Exception>();

        for (int i = 0; i < iterations; i++)
        {
            var key = $"item_{i}";
            var doc = new LeanDocument();
            doc.Add(new StringField("_key", key, stored: false));
            doc.Add(new TextField("body", $"content number {i} lorem ipsum", stored: true, boost: 1.0f));

            try
            {
                writer.UpdateDocument("_key", key, doc);
                if (i % 5 == 0 && i > 20)
                    writer.DeleteDocuments(new TermQuery("_key", $"item_{i - 17}"));
                writer.Commit();
                manager.MaybeRefresh();
                if (i % 3 == 0)
                    manager.UsingSearcher(s => s.Search(new TermQuery("body", "lorem"), 10).ScoreDocs.Length);
            }
            catch (Exception ex)
            {
                errors++;
                caughtExceptions.Add(ex);
                _output.WriteLine($"[ERROR] Iteration {i}: {ex.GetType().Name}: {ex.Message}");

                // Stop early on first few errors.
                if (errors >= 3)
                {
                    _output.WriteLine("Too many errors, stopping early.");
                    break;
                }
            }
        }

        if (errors > 0)
        {
            var messages = string.Join("\n  ", caughtExceptions.Select(e => $"{e.GetType().Name}: {e.Message}"));
            Assert.Fail($"{errors} error(s) during {iterations}-iteration commit+search loop:\n  {messages}");
        }

        // Verify search still works after all the churn.
        manager.MaybeRefresh();
        int finalCount = manager.UsingSearcher(s => s.Search(new TermQuery("body", "lorem"), 1000).ScoreDocs.Length);
        Assert.True(finalCount > 0, "Expected to find at least one document after many cycles.");
        _output.WriteLine($"Final searcher found {finalCount} matching documents.");
    }

    /// <summary>
    /// Deterministically pins several generations of searchers while background merge
    /// cleanup runs concurrently with later commits. This directly exercises the
    /// Windows-specific file-lifetime race without relying on a long iteration count.
    /// </summary>
    [Fact(DisplayName = "Commit + SearcherManager stress: pinned generations survive merge churn", Timeout = 60_000)]
    public void CommitWithSearcherManagerStressLoop_NoCrash()
    {
        var dirPath = SubDir("commit_searcher_stress");
        using var dir = new MMapDirectory(dirPath);
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            DurableCommits = false,
            MaxBufferedDocs = 1,
            MergePolicy = new TieredMergePolicy(2)
        });
        using var manager = new SearcherManager(dir, null);

        const int iterations = 120;
        const int pinnedGenerations = 8;
        var heldSearchers = new Queue<IndexSearcher>();

        try
        {
            for (int i = 0; i < iterations; i++)
            {
                var key = $"item_{i}";
                var doc = new LeanDocument();
                doc.Add(new StringField("_key", key, stored: false));
                doc.Add(new TextField("body", $"content number {i} lorem ipsum dolor sit amet consectetur", stored: true, boost: 1.0f));

                writer.UpdateDocument("_key", key, doc);
                if (i % 5 == 0 && i > 20)
                    writer.DeleteDocuments(new TermQuery("_key", $"item_{i - 17}"));
                writer.Commit();
                manager.MaybeRefresh();

                heldSearchers.Enqueue(manager.Acquire());
                if (heldSearchers.Count > pinnedGenerations)
                {
                    var oldest = heldSearchers.Dequeue();
                    try
                    {
                        Assert.True(oldest.Search(
                            new TermQuery("body", "lorem"),
                            10,
                            TestContext.Current.CancellationToken).TotalHits > 0);
                    }
                    finally
                    {
                        manager.Release(oldest);
                    }
                }
            }
        }
        finally
        {
            while (heldSearchers.TryDequeue(out var searcher))
                manager.Release(searcher);
        }

        manager.MaybeRefresh();
        int finalCount = manager.UsingSearcher(s => s.Search(new TermQuery("body", "lorem"), 1000).ScoreDocs.Length);
        Assert.True(finalCount > 0, "Expected to find documents after pinned-generation merge churn.");
        _output.WriteLine($"Pinned-generation stress complete. Final searcher found {finalCount} matching documents.");
    }
}
