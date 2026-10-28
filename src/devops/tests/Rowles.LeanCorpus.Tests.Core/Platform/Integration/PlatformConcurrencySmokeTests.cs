using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Platform.Integration;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.Search)]
[Scope(TestScope.PlatformSmoke)]
public sealed class PlatformConcurrencySmokeTests
{
    [Fact]
    public async Task Shared_searcher_returns_consistent_results_to_bounded_parallel_searches()
    {
        string root = Path.Combine(Path.GetTempPath(), $"leancorpus-platform-concurrency-{Guid.NewGuid():N}");
        string[] expected = ["doc-0", "doc-2", "doc-4"];
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            using (var directory = new MMapDirectory(root))
            {
                using var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance });
                for (int i = 0; i < 6; i++)
                {
                    var document = new LeanDocument();
                    document.Add(new StringField("id", $"doc-{i}", stored: true));
                    document.Add(new StringField("kind", i % 2 == 0 ? "selected" : "other"));
                    writer.AddDocument(document);
                }
                writer.Commit();
            }

            using (var directory = new MMapDirectory(root))
            using (var searcher = new IndexSearcher(directory))
            {
                var query = new TermQuery("kind", "selected");
                Task<string[][]>[] searches = Enumerable.Range(0, 4)
                    .Select(_ => Task.Run(() =>
                    {
                        var observed = new string[25][];
                        for (int i = 0; i < observed.Length; i++)
                        {
                            var results = searcher.Search(query, 10, cancellationToken);
                            Assert.Equal(expected.Length, results.ScoreDocs.Length);
                            observed[i] = results.ScoreDocs
                                .Select(hit => searcher.GetStoredFields(hit.DocId)["id"].Single())
                                .Order(StringComparer.Ordinal)
                                .ToArray();
                        }
                        return observed;
                    }, cancellationToken))
                    .ToArray();

                string[][][] allResults = await Task.WhenAll(searches);
                foreach (string[][] taskResults in allResults)
                foreach (string[] result in taskResults)
                    Assert.Equal(expected, result);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        Assert.False(Directory.Exists(root));
    }
}
