using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;

namespace Rowles.LeanCorpus.Tests.Core.Platform.Integration;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
[Area(TestArea.Search)]
[Scope(TestScope.PlatformSmoke)]
public sealed class PlatformIndexSmokeTests
{
    [Fact]
    public void Committed_index_reopens_searches_and_releases_its_files()
    {
        string root = Path.Combine(Path.GetTempPath(), $"leancorpus-platform-index-{Guid.NewGuid():N}");
        try
        {
            using (var directory = new MMapDirectory(root))
            {
                using var writer = new IndexWriter(directory, new IndexWriterConfig { MergePolicy = NoMergePolicy.Instance });
                AddDocument(writer, "doc-1", "common", "alpha-one");
                AddDocument(writer, "doc-2", "selected", "alpha-two");
                AddDocument(writer, "doc-3", "selected", "beta-one");
                AddDocument(writer, "doc-4", "common", "alpha-three");
                writer.Commit();
            }

            using (var directory = new MMapDirectory(root))
            using (var searcher = new IndexSearcher(directory))
            {
                var selected = searcher.Search(new TermQuery("kind", "selected"), 10, TestContext.Current.CancellationToken);
                Assert.Equal(["doc-2", "doc-3"], GetIds(searcher, selected));

                var alphaTags = searcher.Search(new PrefixQuery("tag", "alpha-"), 10, TestContext.Current.CancellationToken);
                Assert.Equal(["doc-1", "doc-2", "doc-4"], GetIds(searcher, alphaTags));
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }

        Assert.False(Directory.Exists(root));
    }

    private static void AddDocument(IndexWriter writer, string id, string kind, string tag)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id, stored: true));
        document.Add(new StringField("kind", kind));
        document.Add(new StringField("tag", tag));
        writer.AddDocument(document);
    }

    private static string[] GetIds(IndexSearcher searcher, TopDocs results) =>
        results.ScoreDocs
            .Select(hit => searcher.GetStoredFields(hit.DocId)["id"].Single())
            .Order(StringComparer.Ordinal)
            .ToArray();
}
