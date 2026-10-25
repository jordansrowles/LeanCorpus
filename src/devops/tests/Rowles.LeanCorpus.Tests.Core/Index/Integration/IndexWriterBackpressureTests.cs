using System.Reflection;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Contains unit tests for Index Writer Backpressure.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class IndexWriterBackpressureTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public IndexWriterBackpressureTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static SemaphoreSlim? GetSemaphore(IndexWriter writer)
    {
        return writer.BackpressureSemaphoreForTests;
    }

    private static LeanDocument MakeDoc(string body)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        return doc;
    }

    private static LeanDocument MakeIdentifiedDoc(string id, string body)
    {
        var doc = new LeanDocument();
        doc.Add(new StringField("id", id));
        doc.Add(new TextField("body", body));
        return doc;
    }

    [Fact(DisplayName = "Update Documents: Token Rejection Leaves Every Existing Version Intact")]
    public void UpdateDocuments_TokenRejection_LeavesExistingVersionsIntact()
    {
        var dir = new MMapDirectory(SubDir("c7_update_batch_rejects_before_delete"));
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxBufferedDocs = 100,
            MaxTokensPerDocument = 3,
            TokenBudgetPolicy = TokenBudgetPolicy.Reject,
            MergeThreshold = 100,
        });
        writer.AddDocuments([
            MakeIdentifiedDoc("doc-1", "original one"),
            MakeIdentifiedDoc("doc-2", "original two"),
        ]);
        writer.Commit();

        (string Term, LeanDocument Replacement)[] replacements =
        [
            ("doc-1", MakeIdentifiedDoc("doc-1", "replacement one")),
            ("doc-2", MakeIdentifiedDoc("doc-2", "one two three four")),
        ];
        Assert.Throws<TokenBudgetExceededException>(() => writer.UpdateDocuments("id", replacements));

        writer.Commit();
        using var searcher = new IndexSearcher(dir);
        Assert.Equal(2, searcher.Search(new TermQuery("body", "original"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("body", "replacement"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(2, searcher.Stats.LiveDocCount);
    }

    /// <summary>
    /// Verifies a rejected document preserves earlier sequentially accepted documents.
    /// </summary>
    [Fact(DisplayName = "Add Documents: Token Rejection Preserves Earlier Documents And Writer")]
    public void AddDocuments_TokenRejection_PreservesEarlierDocumentsAndWriter()
    {
        var dir = new MMapDirectory(SubDir("c7_addocs_body_throws"));
        var config = new IndexWriterConfig
        {
            MaxQueuedDocs = 16,
            MaxTokensPerDocument = 3,
            TokenBudgetPolicy = TokenBudgetPolicy.Reject,
        };
        using var writer = new IndexWriter(dir, config);
        var sem = GetSemaphore(writer);
        Assert.NotNull(sem);
        var initial = sem!.CurrentCount;

        var docs = new List<LeanDocument>
        {
            MakeDoc("ok one"),
            MakeDoc("ok two"),
            MakeDoc("a b c d e f g h i"), // exceeds budget -> throws inside body
            MakeDoc("never reached"),
        };

        Assert.Throws<TokenBudgetExceededException>(() => writer.AddDocuments(docs));
        Assert.Equal(initial - 2, sem.CurrentCount);

        writer.Commit();
        Assert.Equal(initial, sem.CurrentCount);

        writer.AddDocument(MakeDoc("still usable"));
        writer.Commit();
        Assert.Equal(initial, sem.CurrentCount);
    }

    /// <summary>
    /// Verifies a rejected block is atomic and leaves the writer usable.
    /// </summary>
    [Fact(DisplayName = "Add Document Block: Token Rejection Is Atomic And Writer Remains Usable")]
    public void AddDocumentBlock_TokenRejection_IsAtomicAndWriterRemainsUsable()
    {
        var dir = new MMapDirectory(SubDir("c7_block_body_throws"));
        var config = new IndexWriterConfig
        {
            MaxQueuedDocs = 16,
            MaxTokensPerDocument = 3,
            TokenBudgetPolicy = TokenBudgetPolicy.Reject,
        };
        using var writer = new IndexWriter(dir, config);
        var sem = GetSemaphore(writer);
        Assert.NotNull(sem);
        var initial = sem!.CurrentCount;

        var block = new List<LeanDocument>
        {
            MakeDoc("child one"),
            MakeDoc("child two"),
            MakeDoc("a b c d e f g h"), // exceeds budget -> throws inside body
            MakeDoc("parent doc"),
        };

        Assert.Throws<TokenBudgetExceededException>(() => writer.AddDocumentBlock(block));
        Assert.Equal(initial, sem.CurrentCount);
        Assert.Throws<TokenBudgetExceededException>(() => writer.AddDocumentBlock(block));
        Assert.Equal(initial, sem.CurrentCount);

        writer.AddDocument(MakeDoc("still usable"));
        writer.Commit();
        Assert.Equal(initial, sem.CurrentCount);
    }

    /// <summary>
    /// Verifies a partial sequential batch can be committed and indexing can continue.
    /// </summary>
    [Fact(DisplayName = "Add Documents: Partial Batch Commits And Writer Remains Responsive")]
    public void AddDocuments_PartialBatch_CommitsAndWriterRemainsResponsive()
    {
        var dir = new MMapDirectory(SubDir("c7_stress"));
        var config = new IndexWriterConfig
        {
            MaxQueuedDocs = 8,
            MaxTokensPerDocument = 3,
            TokenBudgetPolicy = TokenBudgetPolicy.Reject,
        };
        using var writer = new IndexWriter(dir, config);
        var sem = GetSemaphore(writer);
        Assert.NotNull(sem);
        var initial = sem!.CurrentCount;

        var docs = new List<LeanDocument>
        {
            MakeDoc("ok"),
            MakeDoc("a b c d e f"),
        };

        Assert.Throws<TokenBudgetExceededException>(() => writer.AddDocuments(docs));
        Assert.Equal(initial - 1, sem.CurrentCount);

        writer.Commit();
        Assert.Equal(initial, sem.CurrentCount);

        writer.AddDocument(MakeDoc("clean"));
        writer.Commit();
        Assert.Equal(initial, sem.CurrentCount);
    }
}
