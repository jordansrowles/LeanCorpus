using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Search;
using Rowles.LeanLucene.Search.Simd;
using Rowles.LeanLucene.Search.Parsing;
using Rowles.LeanLucene.Search.Highlighting;
using Rowles.LeanLucene.Store;

namespace Rowles.LeanLucene.Tests.Search;

/// <summary>
/// Contains unit tests for Index Searcher Thread Safety.
/// </summary>
[Trait("Category", "ThreadSafety")]
public sealed class IndexSearcherThreadSafetyTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"ll-searcher-ts-{Guid.NewGuid():N}");

    public IndexSearcherThreadSafetyTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    /// <summary>
    /// Verifies the Parallel Term Queries: Across Threads Produce Identical Results scenario.
    /// </summary>
    [Fact(DisplayName = "Parallel Term Queries: Across Threads Produce Identical Results")]
    public void ParallelTermQueries_AcrossThreads_ProduceIdenticalResults()
    {
        const int docCount = 200;
        const int threadCount = 16;
        const int queriesPerThread = 50;

        var directory = new MMapDirectory(_dir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 500 });

        for (int i = 0; i < docCount; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", $"word{i % 20} content article"));
            doc.Add(new StringField("id", i.ToString()));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(directory);

        // Establish single-threaded reference results for each distinct term
        var referenceResults = new Dictionary<string, int[]>();
        for (int t = 0; t < 20; t++)
        {
            string term = $"word{t}";
            var hits = searcher.Search(new TermQuery("body", term), docCount);
            referenceResults[term] = hits.ScoreDocs.Select(sd => sd.DocId).OrderBy(id => id).ToArray();
        }

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        var threads = Enumerable.Range(0, threadCount)
            .Select(threadIdx => new Thread(() =>
            {
                var rng = new Random(threadIdx * 17);
                for (int q = 0; q < queriesPerThread; q++)
                {
                    string term = $"word{rng.Next(20)}";
                    var hits = searcher.Search(new TermQuery("body", term), docCount);
                    var actual = hits.ScoreDocs.Select(sd => sd.DocId).OrderBy(id => id).ToArray();
                    var expected = referenceResults[term];

                    if (!actual.SequenceEqual(expected))
                        errors.Add($"Thread {threadIdx}, query {q}: term={term}, expected {expected.Length} docs, got {actual.Length}");
                }
            }))
            .ToList();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join(TimeSpan.FromSeconds(30));

        Assert.Empty(errors);
    }

    /// <summary>
    /// Verifies the Parallel Phrase Queries: Across Threads Produce Identical Results scenario.
    /// </summary>
    [Fact(DisplayName = "Parallel Phrase Queries: Across Threads Produce Identical Results")]
    public void ParallelPhraseQueries_AcrossThreads_ProduceIdenticalResults()
    {
        const int docCount = 100;
        const int threadCount = 8;
        const int queriesPerThread = 30;

        var subDir = Path.Combine(_dir, "phrase");
        Directory.CreateDirectory(subDir);

        var directory = new MMapDirectory(subDir);
        using var writer = new IndexWriter(directory, new IndexWriterConfig { MaxBufferedDocs = 200 });

        for (int i = 0; i < docCount; i++)
        {
            var doc = new LeanDocument();
            doc.Add(new TextField("body", i % 3 == 0 ? "quick brown fox" : i % 3 == 1 ? "lazy dog jumps" : "random other words"));
            writer.AddDocument(doc);
        }
        writer.Commit();

        using var searcher = new IndexSearcher(directory);

        var reference = searcher.Search(new PhraseQuery("body", ["quick", "brown"]), docCount);
        var expectedIds = reference.ScoreDocs.Select(sd => sd.DocId).OrderBy(id => id).ToArray();

        var errors = new System.Collections.Concurrent.ConcurrentBag<string>();

        var threads = Enumerable.Range(0, threadCount)
            .Select(threadIdx => new Thread(() =>
            {
                for (int q = 0; q < queriesPerThread; q++)
                {
                    var hits = searcher.Search(new PhraseQuery("body", ["quick", "brown"]), docCount);
                    var actual = hits.ScoreDocs.Select(sd => sd.DocId).OrderBy(id => id).ToArray();
                    if (!actual.SequenceEqual(expectedIds))
                        errors.Add($"Thread {threadIdx}, query {q}: expected {expectedIds.Length}, got {actual.Length}");
                }
            }))
            .ToList();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join(TimeSpan.FromSeconds(30));

        Assert.Empty(errors);
    }
}
