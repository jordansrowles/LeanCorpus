using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Search.Searcher;

[Category(TestCategory.Integration)]
[Area(TestArea.Search)]
public sealed class CollectionFrequencyCacheTests
{
    [Fact]
    public void CollectionFrequencyCache_EvictsGenerationsAndPreservesSearchScores()
    {
        const int maximumEntries = 1024;
        string path = Path.Combine(Path.GetTempPath(), $"lc-term-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);

        try
        {
            using var directory = new MMapDirectory(path);
            string oversizedTerm = new('x', 600);
            string[] terms = Enumerable.Range(0, maximumEntries + 1)
                .Select(static i => $"cacheterm{i:D4}")
                .ToArray();

            using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
            {
                var document = new LeanDocument();
                document.Add(new TextField("body", $"{string.Join(' ', terms)} {oversizedTerm}"));
                writer.AddDocument(document);
                writer.Commit();
            }

            using var searcher = new IndexSearcher(directory, new IndexSearcherConfig
            {
                Similarity = DirichletSimilarity.Instance,
                EnableQueryCache = false,
            });

            var first = searcher.Search(new TermQuery("body", terms[0]), 1, TestContext.Current.CancellationToken);
            Assert.Equal(1, first.TotalHits);
            for (int i = 1; i < terms.Length; i++)
            {
                var result = searcher.Search(new TermQuery("body", terms[i]), 1, TestContext.Current.CancellationToken);
                Assert.Equal(1, result.TotalHits);
            }

            var repeated = searcher.Search(new TermQuery("body", terms[0]), 1, TestContext.Current.CancellationToken);
            Assert.Equal(first.ScoreDocs[0].Score, repeated.ScoreDocs[0].Score);

            var metrics = searcher.CollectionFrequencyCacheMetrics;
            Assert.InRange(metrics.EntryCount, 0, maximumEntries);
            Assert.True(metrics.EvictionCount > 0);

            var beforeOversizedTerm = searcher.CollectionFrequencyCacheMetrics;
            var oversizedResult = searcher.Search(new TermQuery("body", oversizedTerm), 1, TestContext.Current.CancellationToken);
            Assert.Equal(1, oversizedResult.TotalHits);
            Assert.Equal(beforeOversizedTerm, searcher.CollectionFrequencyCacheMetrics);
        }
        finally
        {
            TestDirectoryFixture.TryDeleteDirectory(path);
        }
    }
}
