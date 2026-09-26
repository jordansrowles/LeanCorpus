using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Scoring;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class BlockJoinDeletionTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public BlockJoinDeletionTests(TestDirectoryFixture fixture) => _fixture = fixture;

    [Fact(DisplayName = "Block Join: Deleted Matching Child Does Not Return Its Parent")]
    public void DeletedMatchingChild_DoesNotReturnParent()
    {
        string path = CreateIndexPath();
        using (var writer = CreateWriter(path))
        {
            writer.AddDocumentBlock([Child("dead-child", "needle"), Parent("parent-one")]);
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "dead-child"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(0, Search(searcher, new TermQuery("body", "needle")).TotalHits);
    }

    [Fact(DisplayName = "Block Join: Deleted Parent Is Never Returned")]
    public void DeletedParent_IsNeverReturned()
    {
        string path = CreateIndexPath();
        using (var writer = CreateWriter(path))
        {
            writer.AddDocumentBlock([Child("live-child", "needle"), Parent("dead-parent")]);
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "dead-parent"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(0, Search(searcher, new TermQuery("body", "needle")).TotalHits);
    }

    [Fact(DisplayName = "Block Join: Deleted Child In One Block Does Not Affect Another Block")]
    public void DeletedChildInOneBlock_DoesNotAffectAnotherBlock()
    {
        string path = CreateIndexPath();
        using (var writer = CreateWriter(path))
        {
            writer.AddDocumentBlock([Child("dead-child", "needle"), Parent("parent-one")]);
            writer.AddDocumentBlock([Child("live-child", "needle"), Parent("parent-two")]);
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "dead-child"));
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var results = Search(searcher, new TermQuery("body", "needle"));
        Assert.Equal(1, results.TotalHits);
        Assert.Equal("parent-two", ParentId(searcher, results.ScoreDocs[0].DocId));
    }

    [Fact(DisplayName = "Block Join: Boolean Child Query Ignores Deleted Children")]
    public void BooleanChildQuery_IgnoresDeletedChildren()
    {
        string path = CreateIndexPath();
        using (var writer = CreateWriter(path))
        {
            writer.AddDocumentBlock([Child("dead-child", "needle marker"), Parent("parent-one")]);
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "dead-child"));
            writer.Commit();
        }

        var childQuery = new BooleanQuery.Builder()
            .Add(new TermQuery("body", "needle"), Occur.Must)
            .Add(new TermQuery("body", "marker"), Occur.Must)
            .Build();
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(0, Search(searcher, childQuery).TotalHits);
    }

    [Fact(DisplayName = "Block Join: Merge After Parent Deletion Drops The Whole Block")]
    public void MergeAfterParentDeletion_DropsWholeBlock()
    {
        string path = CreateIndexPath();
        using (var writer = CreateWriter(path))
        {
            writer.AddDocumentBlock([Child("orphan-child", "orphanterm"), Parent("dead-parent")]);
            writer.Commit();
            writer.AddDocumentBlock([Child("live-child", "liveterm"), Parent("live-parent")]);
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "dead-parent"));
            writer.Commit();

            Assert.True(writer.ForceMerge(1) > 0);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(0, Search(searcher, new TermQuery("body", "orphanterm")).TotalHits);

        var retained = Search(searcher, new TermQuery("body", "liveterm"));
        Assert.Equal(1, retained.TotalHits);
        Assert.Equal("live-parent", ParentId(searcher, retained.ScoreDocs[0].DocId));
    }

    [Fact(DisplayName = "Block Join: Retained Soft Deleted Parent Preserves Its Boundary")]
    public void RetainedSoftDeletedParent_PreservesBoundaryAndStaysHidden()
    {
        string path = CreateIndexPath();
        using (var writer = new IndexWriter(new MMapDirectory(path), new IndexWriterConfig
        {
            MaxBufferedDocs = 8,
            MergeThreshold = 100,
            MergePolicy = NoMergePolicy.Instance,
            SoftDeletesEnabled = true,
            SoftDeleteRetentionSeconds = 3600,
        }))
        {
            writer.AddDocumentBlock([Child("soft-child", "softterm"), Parent("soft-parent")]);
            writer.Commit();
            writer.AddDocumentBlock([Child("live-child", "liveterm"), Parent("live-parent")]);
            writer.Commit();
            writer.SoftDeleteDocuments(new TermQuery("id", "soft-parent"));
            writer.Commit();

            Assert.True(writer.ForceMerge(1) > 0);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(0, Search(searcher, new TermQuery("body", "softterm")).TotalHits);

        var retained = Search(searcher, new TermQuery("body", "liveterm"));
        Assert.Equal(1, retained.TotalHits);
        Assert.Equal("live-parent", ParentId(searcher, retained.ScoreDocs[0].DocId));
    }

    [Fact(DisplayName = "Block Join: Hard Deleted Parent Cannot Reparent Children In Later Merges")]
    public void HardDeletedParent_CannotReparentChildrenInLaterMerges()
    {
        string path = CreateIndexPath();
        using (var writer = CreateWriter(path))
        {
            writer.AddDocumentBlock([Child("orphan-child", "orphanterm"), Parent("dead-parent")]);
            writer.Commit();
            writer.AddDocumentBlock([Child("live-child", "firstlive"), Parent("first-parent")]);
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "dead-parent"));
            writer.Commit();
            Assert.True(writer.ForceMerge(1) > 0);
            writer.Commit();

            writer.AddDocumentBlock([Child("later-child", "laterlive"), Parent("later-parent")]);
            writer.Commit();
            Assert.True(writer.ForceMerge(1) > 0);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(new MMapDirectory(path));
        Assert.Equal(0, Search(searcher, new TermQuery("body", "orphanterm")).TotalHits);
        Assert.Equal(1, Search(searcher, new TermQuery("body", "firstlive")).TotalHits);
        Assert.Equal(1, Search(searcher, new TermQuery("body", "laterlive")).TotalHits);
    }

    [Fact(DisplayName = "Block Join: Parallel Searches Remain Isolated After Deletions")]
    public async Task ParallelSearches_RemainIsolatedAfterDeletions()
    {
        string path = CreateIndexPath();
        using (var writer = CreateWriter(path))
        {
            writer.AddDocumentBlock([Child("live-child", "needle marker"), Parent("live-parent")]);
            writer.AddDocumentBlock([Child("dead-child", "needle marker"), Parent("dead-child-parent")]);
            writer.AddDocumentBlock([Child("dead-parent-child", "needle marker"), Parent("dead-parent")]);
            writer.Commit();
            writer.DeleteDocuments(new TermQuery("id", "dead-child"));
            writer.DeleteDocuments(new TermQuery("id", "dead-parent"));
            writer.Commit();
        }

        var childQuery = new BooleanQuery.Builder()
            .Add(new TermQuery("body", "needle"), Occur.Must)
            .Add(new TermQuery("body", "marker"), Occur.Must)
            .Build();
        var query = new BlockJoinQuery(childQuery);
        using var searcher = new IndexSearcher(new MMapDirectory(path));
        var cancellationToken = TestContext.Current.CancellationToken;
        var tasks = Enumerable.Range(0, 32)
            .Select(_ => Task.Run(() => searcher.Search(query, 10, cancellationToken), cancellationToken));
        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.Equal(1, result.TotalHits));
        Assert.All(results, result => Assert.Equal("live-parent", ParentId(searcher, result.ScoreDocs[0].DocId)));
        Assert.Equal(1, searcher.Search(query, 10, cancellationToken).TotalHits);
    }

    private string CreateIndexPath()
    {
        string path = Path.Combine(_fixture.Path, $"block_join_deletion_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static IndexWriter CreateWriter(string path) => new(new MMapDirectory(path), new IndexWriterConfig
    {
        MaxBufferedDocs = 8,
        MergeThreshold = 100,
        MergePolicy = NoMergePolicy.Instance,
    });

    private static LeanDocument Child(string id, string body)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new TextField("body", body));
        return document;
    }

    private static LeanDocument Parent(string id)
    {
        var document = new LeanDocument();
        document.Add(new StringField("id", id));
        document.Add(new StoredField("parent-id", id));
        document.Add(new TextField("title", id));
        return document;
    }

    private static TopDocs Search(IndexSearcher searcher, Query childQuery)
        => searcher.Search(new BlockJoinQuery(childQuery), 10, TestContext.Current.CancellationToken);

    private static string ParentId(IndexSearcher searcher, int parentDocId)
        => searcher.GetStoredFields(parentDocId)["parent-id"][0];
}
