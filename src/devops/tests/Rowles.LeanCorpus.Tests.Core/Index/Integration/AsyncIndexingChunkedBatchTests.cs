using System.Collections.Concurrent;
using System.Globalization;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Analysis.Analysers;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search.Queries;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Core.Index;

/// <summary>
/// Async list batches larger than <see cref="IndexWriterConfig.MaxQueuedDocs"/> are admitted
/// as ordered, bounded batch commands rather than one command per document.
/// </summary>
[Category(TestCategory.Integration)]
[Area(TestArea.Index)]
public sealed class AsyncIndexingChunkedBatchTests : IClassFixture<TestDirectoryFixture>
{
    private static readonly string[] Vocabulary = ["alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf"];
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(20);

    private readonly TestDirectoryFixture _fixture;

    public AsyncIndexingChunkedBatchTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        Directory.CreateDirectory(path);
        return path;
    }

    private static LeanDocument MakeDoc(int index, string? body = null)
    {
        var doc = new LeanDocument();
        doc.Add(new StringField("id", index.ToString("D5", CultureInfo.InvariantCulture), stored: true));
        doc.Add(new TextField("body", body ?? BodyFor(index), stored: true));
        return doc;
    }

    private static string BodyFor(int index) =>
        $"{Vocabulary[index % Vocabulary.Length]} {Vocabulary[(index * 3 + 1) % Vocabulary.Length]} common";

    private static LeanDocument[] MakeDocs(int count) => Enumerable.Range(0, count).Select(i => MakeDoc(i)).ToArray();

    private static IndexWriterConfig Config(int maxQueuedDocs, IAnalyser? analyser = null) => new()
    {
        MaxQueuedDocs = maxQueuedDocs,
        MaxBufferedDocs = 40,
        MergeThreshold = 1_000,
        DurableCommits = false,
        DefaultAnalyser = analyser ?? new StandardAnalyser(),
    };

    private static ConcurrentQueue<int> RecordAdmissions(IndexWriter writer)
    {
        var admissions = new ConcurrentQueue<int>();
        writer.AsyncCommandAdmittedForTests = admissions.Enqueue;
        return admissions;
    }

    [Theory(DisplayName = "Async Chunked Batch: Commands Are Bounded By MaxQueuedDocs", Timeout = 60_000)]
    [InlineData(10, 9, new[] { 9 })]
    [InlineData(10, 10, new[] { 10 })]
    [InlineData(10, 11, new[] { 10, 1 })]
    [InlineData(10, 35, new[] { 10, 10, 10, 5 })]
    [InlineData(1, 4, new[] { 1, 1, 1, 1 })]
    public async Task OversizedBatch_IsAdmittedAsBoundedCommands(int maxQueuedDocs, int documents, int[] expectedCommands)
    {
        using var dir = new MMapDirectory(SubDir($"bounded-{maxQueuedDocs}-{documents}"));
        using var writer = new IndexWriter(dir, Config(maxQueuedDocs));
        var admissions = RecordAdmissions(writer);

        await writer.AddDocumentsAsync(MakeDocs(documents), TestContext.Current.CancellationToken)
            .AsTask().WaitAsync(OperationTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(expectedCommands, admissions.ToArray());
        writer.Commit();
        writer.Dispose();
        using var searcher = new IndexSearcher(dir);
        Assert.Equal(documents, searcher.Stats.LiveDocCount);
    }

    [Fact(DisplayName = "Async Chunked Batch: Without A Document Limit The Batch Is One Command")]
    public async Task UnlimitedQueue_AdmitsOneCommand()
    {
        using var dir = new MMapDirectory(SubDir(nameof(UnlimitedQueue_AdmitsOneCommand)));
        using var writer = new IndexWriter(dir, Config(maxQueuedDocs: 0));
        var admissions = RecordAdmissions(writer);

        await writer.AddDocumentsAsync(MakeDocs(75), TestContext.Current.CancellationToken);

        Assert.Equal([75], admissions.ToArray());
    }

    [Fact(DisplayName = "Async Chunked Batch: Cancellation Before Admission Indexes Nothing")]
    public async Task CancellationBeforeAdmission_IndexesNothing()
    {
        using var dir = new MMapDirectory(SubDir(nameof(CancellationBeforeAdmission_IndexesNothing)));
        using var writer = new IndexWriter(dir, Config(maxQueuedDocs: 10));
        var admissions = RecordAdmissions(writer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            writer.AddDocumentsAsync(MakeDocs(35), cancellation.Token).AsTask());

        Assert.Empty(admissions);
        writer.Commit();
        writer.Dispose();
        using var searcher = new IndexSearcher(dir);
        Assert.Equal(0, searcher.Stats.LiveDocCount);
    }

    [Fact(DisplayName = "Async Chunked Batch: Cancellation After Admission Keeps Accepted Chunks Only", Timeout = 60_000)]
    public async Task CancellationAfterAdmission_KeepsAcceptedChunksOnly()
    {
        using var dir = new MMapDirectory(SubDir(nameof(CancellationAfterAdmission_KeepsAcceptedChunksOnly)));
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var writer = new IndexWriter(dir, Config(maxQueuedDocs: 10, new BlockingAnalyser(entered, release)));
        var admissions = RecordAdmissions(writer);
        using var cancellation = new CancellationTokenSource();

        Task batch = writer.AddDocumentsAsync(MakeDocs(35), cancellation.Token).AsTask();
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)), "The consumer did not start the first chunk.");
            cancellation.Cancel();
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                batch.WaitAsync(OperationTimeout, TestContext.Current.CancellationToken));
        }
        finally
        {
            release.Set();
        }

        // The admitted chunk is writer-owned and completes; later chunks were never admitted.
        Assert.Equal([10], admissions.ToArray());
        await writer.CommitAsync(TestContext.Current.CancellationToken).WaitAsync(OperationTimeout, TestContext.Current.CancellationToken);
        writer.Dispose();
        using var searcher = new IndexSearcher(dir);
        Assert.Equal(10, searcher.Stats.LiveDocCount);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "00009"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "00010"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "Async Chunked Batch: Acknowledged Documents Become Durable At Commit")]
    public async Task AcknowledgedDocuments_AreDurableAtCommitBoundary()
    {
        var path = SubDir(nameof(AcknowledgedDocuments_AreDurableAtCommitBoundary));
        var config = Config(maxQueuedDocs: 10);
        config.DurableCommits = true;
        using (var dir = new MMapDirectory(path))
        using (var writer = new IndexWriter(dir, config))
        {
            await writer.AddDocumentsAsync(MakeDocs(35), TestContext.Current.CancellationToken);
            await writer.CommitAsync(TestContext.Current.CancellationToken);

            // Acknowledged after the commit but never committed: not visible after reopen.
            await writer.AddDocumentsAsync(
                Enumerable.Range(1_000, 25).Select(i => MakeDoc(i)).ToArray(), TestContext.Current.CancellationToken);
        }

        using var reopened = new MMapDirectory(path);
        using var searcher = new IndexSearcher(reopened);
        Assert.Equal(35, searcher.Stats.LiveDocCount);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "01000"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "Async Chunked Batch: Interleaved Commits Keep Every Document Exactly Once", Timeout = 60_000)]
    public async Task InterleavedCommits_KeepEveryDocumentExactlyOnce()
    {
        using var dir = new MMapDirectory(SubDir(nameof(InterleavedCommits_KeepEveryDocumentExactlyOnce)));
        using var writer = new IndexWriter(dir, Config(maxQueuedDocs: 7));
        var docs = MakeDocs(140);

        Task producerA = writer.AddDocumentsAsync(docs[..70], TestContext.Current.CancellationToken).AsTask();
        Task producerB = writer.AddDocumentsAsync(docs[70..], TestContext.Current.CancellationToken).AsTask();
        while (!producerA.IsCompleted || !producerB.IsCompleted)
            await writer.CommitAsync(TestContext.Current.CancellationToken).WaitAsync(OperationTimeout, TestContext.Current.CancellationToken);

        await Task.WhenAll(producerA, producerB).WaitAsync(OperationTimeout, TestContext.Current.CancellationToken);
        await writer.CommitAsync(TestContext.Current.CancellationToken);
        writer.Dispose();

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(140, searcher.Stats.LiveDocCount);
        for (int i = 0; i < docs.Length; i++)
        {
            Assert.Equal(1, searcher.Search(
                new TermQuery("id", i.ToString("D5", CultureInfo.InvariantCulture)), 10, TestContext.Current.CancellationToken).TotalHits);
        }
    }

    [Fact(DisplayName = "Async Chunked Batch: Rollback Discards Uncommitted Chunks")]
    public async Task Rollback_DiscardsUncommittedChunks()
    {
        var path = SubDir(nameof(Rollback_DiscardsUncommittedChunks));
        using var dir = new MMapDirectory(path);
        using (var writer = new IndexWriter(dir, Config(maxQueuedDocs: 10)))
        {
            await writer.AddDocumentsAsync(MakeDocs(12), TestContext.Current.CancellationToken);
            writer.Commit();
            await writer.AddDocumentsAsync(
                Enumerable.Range(100, 35).Select(i => MakeDoc(i)).ToArray(), TestContext.Current.CancellationToken);
            writer.Rollback();
        }

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(12, searcher.Stats.LiveDocCount);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "00100"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "Async Chunked Batch: Abandoned Writer Leaves The Last Commit Recoverable")]
    public async Task AbandonedWriter_LeavesLastCommitRecoverable()
    {
        var path = SubDir(nameof(AbandonedWriter_LeavesLastCommitRecoverable));
        using (var dir = new MMapDirectory(path))
        {
            var writer = new IndexWriter(dir, Config(maxQueuedDocs: 10));
            await writer.AddDocumentsAsync(MakeDocs(23), TestContext.Current.CancellationToken);
            writer.Commit();
            await writer.AddDocumentsAsync(
                Enumerable.Range(200, 31).Select(i => MakeDoc(i)).ToArray(), TestContext.Current.CancellationToken);

            // Simulate a crash before the second commit: the writer is disposed without committing.
            writer.Dispose();
        }

        using var reopened = new MMapDirectory(path);
        using (var writer = new IndexWriter(reopened, Config(maxQueuedDocs: 10)))
        {
            await writer.AddDocumentsAsync(
                Enumerable.Range(300, 11).Select(i => MakeDoc(i)).ToArray(), TestContext.Current.CancellationToken);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(reopened);
        Assert.Equal(34, searcher.Stats.LiveDocCount);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "00200"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "Async Chunked Batch: Fault In A Later Chunk Keeps Earlier Documents And Writer")]
    public async Task FaultInLaterChunk_KeepsEarlierDocumentsAndWriter()
    {
        using var dir = new MMapDirectory(SubDir(nameof(FaultInLaterChunk_KeepsEarlierDocumentsAndWriter)));
        var config = Config(maxQueuedDocs: 10);
        config.MaxTokensPerDocument = 3;
        config.TokenBudgetPolicy = TokenBudgetPolicy.Reject;
        using var writer = new IndexWriter(dir, config);
        var admissions = RecordAdmissions(writer);
        var docs = MakeDocs(35);
        docs[23] = MakeDoc(23, "one two three four five");

        await Assert.ThrowsAsync<TokenBudgetExceededException>(() =>
            writer.AddDocumentsAsync(docs, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal([10, 10, 10], admissions.ToArray());
        await writer.AddDocumentAsync(MakeDoc(900), TestContext.Current.CancellationToken);
        writer.Commit();
        writer.Dispose();

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(24, searcher.Stats.LiveDocCount);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "00022"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "00023"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(0, searcher.Search(new TermQuery("id", "00024"), 10, TestContext.Current.CancellationToken).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "00900"), 10, TestContext.Current.CancellationToken).TotalHits);
    }

    [Fact(DisplayName = "Async Chunked Batch: Capacity One With Concurrent Batches Is Deadlock-Free", Timeout = 60_000)]
    public async Task CapacityOne_ConcurrentBatchesComplete()
    {
        using var dir = new MMapDirectory(SubDir(nameof(CapacityOne_ConcurrentBatchesComplete)));
        using var writer = new IndexWriter(dir, Config(maxQueuedDocs: 1));
        var docs = MakeDocs(120);

        var producers = Enumerable.Range(0, 4)
            .Select(p => writer.AddDocumentsAsync(docs[(p * 30)..((p + 1) * 30)], TestContext.Current.CancellationToken).AsTask())
            .Append(writer.AddDocumentAsync(MakeDoc(500), TestContext.Current.CancellationToken).AsTask())
            .ToArray();
        await Task.WhenAll(producers).WaitAsync(OperationTimeout, TestContext.Current.CancellationToken);
        await writer.CommitAsync(TestContext.Current.CancellationToken).WaitAsync(OperationTimeout, TestContext.Current.CancellationToken);
        writer.Dispose();

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(121, searcher.Stats.LiveDocCount);
    }

    [Fact(DisplayName = "Async Chunked Batch: Sync, Async And Batch Produce Equal Logical Indexes", Timeout = 120_000)]
    public async Task SyncAsyncAndBatch_ProduceEqualLogicalIndexes()
    {
        var docs = MakeDocs(257);

        var sync = await BuildAndDescribe("equal-sync", writer =>
        {
            foreach (var doc in docs)
                writer.AddDocument(doc);
            return Task.CompletedTask;
        });
        var singleAsync = await BuildAndDescribe("equal-async", async writer =>
        {
            foreach (var doc in docs)
                await writer.AddDocumentAsync(doc, TestContext.Current.CancellationToken);
        });
        var chunkedBatch = await BuildAndDescribe("equal-batch", async writer =>
            await writer.AddDocumentsAsync(docs, TestContext.Current.CancellationToken));

        Assert.Equal(sync, singleAsync);
        Assert.Equal(sync, chunkedBatch);
    }

    private async Task<string> BuildAndDescribe(string name, Func<IndexWriter, Task> index)
    {
        var path = SubDir(nameof(SyncAsyncAndBatch_ProduceEqualLogicalIndexes) + "-" + name);
        using var dir = new MMapDirectory(path);
        using (var writer = new IndexWriter(dir, Config(maxQueuedDocs: 16)))
        {
            await index(writer);
            writer.Commit();
        }

        using var searcher = new IndexSearcher(dir);
        var lines = new List<string>
        {
            $"live={searcher.Stats.LiveDocCount}",
            $"total={searcher.Stats.TotalDocCount}",
            $"bodyDocs={searcher.Stats.GetFieldDocCount("body")}",
            $"bodyLength={searcher.Stats.GetFieldLengthSum("body")}",
        };

        var stored = new List<string>();
        for (int docId = 0; docId < searcher.Stats.TotalDocCount; docId++)
        {
            var fields = searcher.GetStoredFields(docId);
            stored.Add($"{docId}:{fields["id"][0]}:{fields["body"][0]}");
        }
        lines.AddRange(stored);

        foreach (var term in Vocabulary.Append("common"))
        {
            var hits = searcher.Search(new TermQuery("body", term), 1_000, TestContext.Current.CancellationToken);
            var ids = hits.ScoreDocs.Select(hit => hit.DocId).Order().Select(id => id.ToString(CultureInfo.InvariantCulture));
            lines.Add($"{term}:{hits.TotalHits}:{string.Join(',', ids)}");
        }

        return string.Join('\n', lines);
    }

    private sealed class BlockingAnalyser : IThreadLocalAnalyser
    {
        private readonly ManualResetEventSlim _entered;
        private readonly ManualResetEventSlim _release;

        internal BlockingAnalyser(ManualResetEventSlim entered, ManualResetEventSlim release)
        {
            _entered = entered;
            _release = release;
        }

        public void Analyse(ReadOnlySpan<char> input, ISpanTokenSink sink)
        {
            _entered.Set();
            if (!_release.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The test did not release the blocking analyser.");
            sink.Add(input, 0, input.Length);
        }

        public IAnalyser CreateThreadLocalAnalyser() => new BlockingAnalyser(_entered, _release);
    }
}
