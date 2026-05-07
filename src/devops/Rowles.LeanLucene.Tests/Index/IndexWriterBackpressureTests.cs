using System.Reflection;
using Rowles.LeanLucene.Analysis;
using Rowles.LeanLucene.Analysis.Analysers;
using Rowles.LeanLucene.Document;
using Rowles.LeanLucene.Document.Fields;
using Rowles.LeanLucene.Index;
using Rowles.LeanLucene.Store;
using Rowles.LeanLucene.Tests.Fixtures;

namespace Rowles.LeanLucene.Tests.Index;

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
        var field = typeof(IndexWriter).GetField("_backpressureSemaphore",
            BindingFlags.Instance | BindingFlags.NonPublic);
        return field?.GetValue(writer) as SemaphoreSlim;
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
    [Fact(DisplayName = "Add Documents: Body Throws Mid Batch Restores All Backpressure Slots")]
    public void AddDocuments_BodyThrowsMidBatch_RestoresAllBackpressureSlots()
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

        for (int round = 0; round < 50; round++)
        {
            var docs = new List<LeanDocument>
            {
                MakeDoc("ok one"),
                MakeDoc("ok two"),
                MakeDoc("a b c d e f g h i"), // exceeds budget -> throws inside body
                MakeDoc("never reached"),
            };

            Assert.Throws<TokenBudgetExceededException>(() => writer.AddDocuments(docs));
            Assert.Equal(initial, sem.CurrentCount);
        }
    }

    /// <summary>
    /// Verifies the Add Document Block: Body Throws Mid Batch Restores All Backpressure Slots scenario.
    /// </summary>
    [Fact(DisplayName = "Add Document Block: Body Throws Mid Batch Restores All Backpressure Slots")]
    public void AddDocumentBlock_BodyThrowsMidBatch_RestoresAllBackpressureSlots()
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

        for (int round = 0; round < 50; round++)
        {
            var block = new List<LeanDocument>
            {
                MakeDoc("child one"),
                MakeDoc("child two"),
                MakeDoc("a b c d e f g h"), // exceeds budget -> throws inside body
                MakeDoc("parent doc"),
            };

            Assert.Throws<TokenBudgetExceededException>(() => writer.AddDocumentBlock(block));
            Assert.Equal(initial, sem.CurrentCount);
        }
    }

    /// <summary>
    /// Verifies the Add Documents: Repeated Failures No Slot Leak Keeps Indexing Responsive scenario.
    /// </summary>
    [Fact(DisplayName = "Add Documents: Repeated Failures No Slot Leak Keeps Indexing Responsive")]
    public void AddDocuments_RepeatedFailures_NoSlotLeak_KeepsIndexingResponsive()
    {
        // After many failed batches, indexing should remain responsive: if the
        // catch handler over- or under-released, MaxQueuedDocs would drift and
        // either deadlock subsequent waits or trigger SemaphoreFullException.
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

        for (int round = 0; round < 200; round++)
        {
            var docs = new List<LeanDocument>
            {
                MakeDoc("ok"),
                MakeDoc("a b c d e f"),
            };
            try { writer.AddDocuments(docs); } catch (TokenBudgetExceededException) { }
        }

        Assert.Equal(initial, sem.CurrentCount);

        for (int i = 0; i < 100; i++)
            writer.AddDocument(MakeDoc("clean"));

        writer.Commit();
    }
}
