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

    [Fact(DisplayName = "Async Backpressure: Capacity One Completes And Commits Every Concurrent Caller", Timeout = 30_000)]
    public async Task CapacityOne_ConcurrentAsyncCallersCommitEveryDocumentExactlyOnce()
    {
        const int documentCount = 32;
        var dirPath = SubDir(nameof(CapacityOne_ConcurrentAsyncCallersCommitEveryDocumentExactlyOnce));
        using var dir = new MMapDirectory(dirPath);
        using (var writer = new IndexWriter(dir, new IndexWriterConfig
               {
                   MaxQueuedDocs = 1,
                   MaxBufferedDocs = 10_000,
                   MergeThreshold = 1_000,
                   DurableCommits = false,
               }))
        {
            Task[] writes = Enumerable.Range(0, documentCount)
                .Select(index => writer.AddDocumentAsync(
                    MakeIdentifiedDoc($"doc-{index}", $"body-{index}")).AsTask())
                .ToArray();

            await Task.WhenAll(writes).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            await writer.CommitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

            // With a one-document admission limit and no merging, every next
            // admission detaches the previous document as its own flush snapshot.
            Assert.Equal(documentCount, writer.GetNrtSegments().Count);
        }

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(documentCount, searcher.Stats.LiveDocCount);
        for (int index = 0; index < documentCount; index++)
        {
            Assert.Equal(1, searcher.Search(
                new TermQuery("id", $"doc-{index}"), 10, TestContext.Current.CancellationToken).TotalHits);
        }
    }

    [Fact(DisplayName = "Async Backpressure: Capacity One Cancels A Command Waiting For The Bounded Channel", Timeout = 30_000)]
    public async Task CapacityOne_CancelsQueuedAsyncCommandAndCommitsAdmittedDocuments()
    {
        using var dir = new MMapDirectory(SubDir(nameof(CapacityOne_CancelsQueuedAsyncCommandAndCommitsAdmittedDocuments)));
        using var analyserEntered = new ManualResetEventSlim();
        using var releaseAnalyser = new ManualResetEventSlim();
        using var writer = new IndexWriter(dir, new IndexWriterConfig
        {
            MaxQueuedDocs = 1,
            MaxBufferedDocs = 10_000,
            MergeThreshold = 1_000,
            DurableCommits = false,
            DefaultAnalyser = new BlockingBackpressureAnalyser(analyserEntered, releaseAnalyser),
        });

        Task firstWrite = writer.AddDocumentAsync(MakeIdentifiedDoc("accepted-1", "first")).AsTask();
        try
        {
            Assert.True(analyserEntered.Wait(TimeSpan.FromSeconds(5)), "The async consumer did not enter the first document's analyser.");

            // The first command is processing and the second fills the one-command
            // channel. The third command must remain cancellable while it waits to enqueue.
            Task secondWrite = writer.AddDocumentAsync(MakeIdentifiedDoc("accepted-2", "second")).AsTask();
            Assert.False(secondWrite.IsCompleted, "The second command unexpectedly completed while the first analyser was blocked.");

            using var cancellation = new CancellationTokenSource();
            Task cancelledWrite = writer.AddDocumentAsync(
                MakeIdentifiedDoc("cancelled", "third"), cancellation.Token).AsTask();
            Assert.False(cancelledWrite.IsCompleted, "The third command was not waiting for channel capacity.");
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                cancelledWrite.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            releaseAnalyser.Set();
            await Task.WhenAll(firstWrite, secondWrite)
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            await writer.CommitAsync(TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
        }
        finally
        {
            releaseAnalyser.Set();
        }

        writer.Dispose();
        using var searcher = new IndexSearcher(dir);
        Assert.Equal(2, searcher.Stats.LiveDocCount);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "accepted-1"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "accepted-2"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "cancelled"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    private sealed class BlockingBackpressureAnalyser : IThreadLocalAnalyser
    {
        private readonly ManualResetEventSlim _entered;
        private readonly ManualResetEventSlim _release;

        internal BlockingBackpressureAnalyser(ManualResetEventSlim entered, ManualResetEventSlim release)
        {
            _entered = entered;
            _release = release;
        }

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            _entered.Set();
            if (!_release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release the async document analyser.");
            sink.Add(input, 0, input.Length);
        }

        public IAnalyser CreateThreadLocalAnalyser() => new BlockingBackpressureAnalyser(_entered, _release);
    }
}
