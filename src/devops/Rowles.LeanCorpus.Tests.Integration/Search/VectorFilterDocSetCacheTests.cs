using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Search;

[Trait("Category", "Search")]
public sealed class VectorFilterDocSetCacheTests : IClassFixture<TestDirectoryFixture>
{
    private readonly string _path;

    public VectorFilterDocSetCacheTests(TestDirectoryFixture fixture) => _path = fixture.Path;

    [Fact(DisplayName = "Vector filter planning: Reuses immutable segment docsets")]
    public void Search_ReusesFilterDocSetForEquivalentQuery()
    {
        string path = Path.Combine(_path, nameof(Search_ReusesFilterDocSetForEquivalentQuery));
        using var directory = new MMapDirectory(path);
        using (var writer = new IndexWriter(directory, new IndexWriterConfig()))
        {
            for (int i = 0; i < 80; i++)
            {
                var document = new LeanDocument();
                document.Add(new TextField("kind", i % 2 == 0 ? "keep" : "drop"));
                document.Add(new VectorField("embedding", new float[] { i, 1f }));
                writer.AddDocument(document);
            }
            writer.Commit();
        }

        using var searcher = new IndexSearcher(
            directory,
            new IndexSearcherConfig { MaxCachedFilterDocSets = 2 });
        var query = new VectorQuery(
            "embedding",
            [1f, 1f],
            filter: new TermQuery("kind", "keep"));

        searcher.Search(query, 10);
        searcher.Search(new VectorQuery("embedding", [1f, 1f], filter: new TermQuery("kind", "keep")), 10);

        Assert.Equal(1, searcher.CachedFilterDocSetCount);
        Assert.Equal(1, searcher.LoadedFilterDocSetCount);
    }
}
