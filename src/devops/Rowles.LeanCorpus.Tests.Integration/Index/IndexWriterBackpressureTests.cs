using System.Reflection;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Contains unit tests for Index Writer Backpressure.
/// </summary>
[Trait("Category", "Index")]
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

    /// <summary>
    /// Verifies the Add Documents: Body Throws Mid Batch Restores All Backpressure Slots scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents: Body Throws Mid Batch Restores Slots And Poisons Writer")]
    public void AddDocuments_BodyThrowsMidBatch_RestoresSlots_AndPoisonsWriter()
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
        Assert.Equal(initial, sem.CurrentCount);
        Assert.Throws<InvalidOperationException>(() => writer.AddDocuments(docs));
        Assert.Equal(initial, sem.CurrentCount);
    }

    /// <summary>
    /// Verifies the Add Document Block: Body Throws Mid Batch Restores All Backpressure Slots scenario.
    /// </summary>
    [Fact(DisplayName = "Add Document Block: Body Throws Mid Batch Restores Slots And Poisons Writer")]
    public void AddDocumentBlock_BodyThrowsMidBatch_RestoresSlots_AndPoisonsWriter()
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
        Assert.Throws<InvalidOperationException>(() => writer.AddDocumentBlock(block));
        Assert.Equal(initial, sem.CurrentCount);
    }

    /// <summary>
    /// Verifies the Add Documents: Repeated Failures No Slot Leak Keeps Indexing Responsive scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents: Failure Restores Slots And Reopen Remains Responsive")]
    public void AddDocuments_FailureRestoresSlots_AndReopenRemainsResponsive()
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
        Assert.Equal(initial, sem.CurrentCount);
        Assert.Throws<InvalidOperationException>(() => writer.AddDocuments(docs));
        Assert.Equal(initial, sem.CurrentCount);
        writer.Dispose();

        using var reopened = new IndexWriter(dir, config);
        for (int i = 0; i < 100; i++)
            reopened.AddDocument(MakeDoc("clean"));

        reopened.Commit();
    }
}
