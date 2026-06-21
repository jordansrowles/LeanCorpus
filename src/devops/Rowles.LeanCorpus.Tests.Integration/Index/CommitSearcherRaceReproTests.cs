using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Xunit.Abstractions;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Reproduction test for GitHub issue: IndexWriter.Commit() throws FileNotFoundException
/// when SearcherManager holds segment references across many incremental commit cycles.
/// See: ADR007 (merge-must-not-block-commit) and the segment-file lifecycle under churn.
/// </summary>
[Trait("Category", "Index")]
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
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
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
    /// Higher-stress variant with more iterations, explicitly designed to hit the
    /// Windows-specific race where background merge cleanup runs concurrently with
    /// a subsequent commit's segment scan.
    /// </summary>
    [Fact(DisplayName = "Commit + SearcherManager stress: no crash after extended churn")]
    public void CommitWithSearcherManagerStressLoop_NoCrash()
    {
        var dirPath = SubDir("commit_searcher_stress");
        using var dir = new MMapDirectory(dirPath);
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        using var manager = new SearcherManager(dir, null);

        int iterations = 600;
        int errors = 0;
        var caughtExceptions = new List<Exception>();

        for (int i = 0; i < iterations; i++)
        {
            var key = $"item_{i}";
            var doc = new LeanDocument();
            doc.Add(new StringField("_key", key, stored: false));
            doc.Add(new TextField("body", $"content number {i} lorem ipsum dolor sit amet consectetur", stored: true, boost: 1.0f));

            try
            {
                writer.UpdateDocument("_key", key, doc);
                if (i % 5 == 0 && i > 20)
                    writer.DeleteDocuments(new TermQuery("_key", $"item_{i - 17}"));
                if (i % 33 == 0 && i > 100) // occasional extra delete to stir the merger
                    writer.DeleteDocuments(new TermQuery("_key", $"item_{i - 99}"));
                writer.Commit();
                manager.MaybeRefresh();
                if (i % 3 == 0)
                    manager.UsingSearcher(s => s.Search(new TermQuery("body", "lorem"), 10).ScoreDocs.Length);
                if (i % 7 == 0)
                    manager.UsingSearcher(s => s.Search(new TermQuery("body", "ipsum"), 10).ScoreDocs.Length);
            }
            catch (FileNotFoundException ex)
            {
                errors++;
                caughtExceptions.Add(ex);
                _output.WriteLine($"[ERROR] Iteration {i}: {ex.GetType().Name}: {ex.Message}");
                if (errors >= 3) break;
            }
            catch (IOException ex)
            {
                errors++;
                caughtExceptions.Add(ex);
                _output.WriteLine($"[ERROR] Iteration {i}: {ex.GetType().Name}: {ex.Message}");
                if (errors >= 3) break;
            }
            catch (Exception ex) when (ex is not FileNotFoundException && ex is not IOException)
            {
                errors++;
                caughtExceptions.Add(ex);
                _output.WriteLine($"[UNEXPECTED ERROR] Iteration {i}: {ex.GetType().Name}: {ex.Message}");
                if (errors >= 3) break;
            }
        }

        if (errors > 0)
        {
            var messages = string.Join("\n  ", caughtExceptions.Select(e => $"{e.GetType().Name}: {e.Message}"));
            Assert.Fail($"{errors} error(s) during {iterations}-iteration stress loop:\n  {messages}");
        }

        // Final sanity: verify index is healthy after the stress loop.
        manager.MaybeRefresh();
        int finalCount = manager.UsingSearcher(s => s.Search(new TermQuery("body", "lorem"), 1000).ScoreDocs.Length);
        Assert.True(finalCount > 0, "Expected to find documents after stress loop + force merge.");
        _output.WriteLine($"Stress loop complete. Final searcher found {finalCount} matching documents.");
    }
}
