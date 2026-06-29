using System.Globalization;
using System.Reflection;
using Rowles.LeanCorpus.Analysis;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Index.Segment;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Integration coverage for the async indexing surface.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Async")]
public sealed class AsyncIndexingTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public AsyncIndexingTests(TestDirectoryFixture fixture) => _fixture = fixture;

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static LeanDocument MakeDoc(string id, string body)
    {
        var doc = new LeanDocument();
        doc.Add(new StringField("id", id));
        doc.Add(new TextField("body", body));
        return doc;
    }

    private static LeanDocument MakeChild(string body)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        return doc;
    }

    private static LeanDocument MakeParent(string title)
    {
        var doc = new LeanDocument();
        doc.Add(new StringField("title", title));
        return doc;
    }

    private static SemaphoreSlim? GetSemaphore(IndexWriter writer)
    {
        return writer.BackpressureSemaphoreForTests;
    }

    private static async IAsyncEnumerable<LeanDocument> StreamDocs(
        int count,
        string term,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return MakeDoc(i.ToString(CultureInfo.InvariantCulture), $"{term} document {i}");
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<LeanDocument> ThrowingStream(
        int countBeforeThrow,
        string term,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (int i = 0; i < countBeforeThrow; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return MakeDoc(i.ToString(CultureInfo.InvariantCulture), $"{term} document {i}");
            await Task.Yield();
        }

        throw new InvalidOperationException("stream failed");
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentAsync Indexes Single Document")]
    public async Task AddDocumentAsync_IndexesSingleDocument()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentAsync_IndexesSingleDocument)));
        using var writer = new IndexWriter(directory, new IndexWriterConfig());

        await writer.AddDocumentAsync(MakeDoc("1", "async body"));
        await writer.CommitAsync();

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "async"), 10).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentsAsync Indexes Batched Documents")]
    public async Task AddDocumentsAsync_IndexesBatchedDocuments()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentsAsync_IndexesBatchedDocuments)));
        using var writer = new IndexWriter(directory, new IndexWriterConfig());

        var docs = Enumerable.Range(0, 6)
            .Select(i => MakeDoc(i.ToString(CultureInfo.InvariantCulture), $"batch document {i}"))
            .ToArray();

        await writer.AddDocumentsAsync(docs);
        await writer.CommitAsync();

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(6, searcher.Search(new TermQuery("body", "batch"), 10).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentsAsync Streams Async Enumerable")]
    public async Task AddDocumentsAsync_StreamsAsyncEnumerable()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentsAsync_StreamsAsyncEnumerable)));
        using var writer = new IndexWriter(directory, new IndexWriterConfig());

        await writer.AddDocumentsAsync(StreamDocs(7, "streamed"), batchSize: 3);
        await writer.CommitAsync();

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(7, searcher.Search(new TermQuery("body", "streamed"), 20).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: Async Enumerable Batches Clamp To MaxQueuedDocs")]
    public async Task AddDocumentsAsync_AsyncEnumerable_ClampsBatchSizeToMaxQueuedDocs()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentsAsync_AsyncEnumerable_ClampsBatchSizeToMaxQueuedDocs)));
        using var writer = new IndexWriter(directory, new IndexWriterConfig
        {
            MaxQueuedDocs = 2,
            MaxBufferedDocs = 100
        });

        await writer.AddDocumentsAsync(StreamDocs(5, "clamped"), batchSize: 16);
        await writer.CommitAsync();

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(5, searcher.Search(new TermQuery("body", "clamped"), 10).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: Async Enumerable Source Failure Keeps Completed Batches")]
    public async Task AddDocumentsAsync_AsyncEnumerableSourceFailure_KeepsCompletedBatches()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentsAsync_AsyncEnumerableSourceFailure_KeepsCompletedBatches)));
        using var writer = new IndexWriter(directory, new IndexWriterConfig());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            writer.AddDocumentsAsync(ThrowingStream(5, "faulted"), batchSize: 2).AsTask());

        await writer.CommitAsync();

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(4, searcher.Search(new TermQuery("body", "faulted"), 10).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentBlockAsync Writes Parent Bit Set")]
    public async Task AddDocumentBlockAsync_WritesParentBitSet()
    {
        var path = SubDir(nameof(AddDocumentBlockAsync_WritesParentBitSet));
        var directory = new MMapDirectory(path);
        using var writer = new IndexWriter(directory, new IndexWriterConfig());

        await writer.AddDocumentBlockAsync(
        [
            MakeChild("child alpha"),
            MakeChild("child beta"),
            MakeParent("async parent")
        ]);
        await writer.CommitAsync();

        var pbsFile = Directory.GetFiles(path, "*.pbs").Single();
        var pbs = ParentBitSet.ReadFrom(pbsFile);

        Assert.False(pbs.IsParent(0));
        Assert.False(pbs.IsParent(1));
        Assert.True(pbs.IsParent(2));
    }

    [Fact(DisplayName = "Async Indexing: CommitAsync Persists Buffered Documents")]
    public async Task CommitAsync_PersistsBufferedDocuments()
    {
        var directory = new MMapDirectory(SubDir(nameof(CommitAsync_PersistsBufferedDocuments)));
        using var writer = new IndexWriter(directory, new IndexWriterConfig());

        await writer.AddDocumentAsync(MakeDoc("1", "durable commit"));
        await writer.CommitAsync();

        var segmentFiles = Directory.GetFiles(directory.DirectoryPath, "segments_*");
        Assert.NotEmpty(segmentFiles);

        using var searcher = new IndexSearcher(directory);
        Assert.Equal(1, searcher.Search(new TermQuery("body", "durable"), 10).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentsAsync Cancellation Releases Partially Acquired Slots")]
    public async Task AddDocumentsAsync_Cancellation_ReleasesPartiallyAcquiredSlots()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentsAsync_Cancellation_ReleasesPartiallyAcquiredSlots)));
        var config = new IndexWriterConfig
        {
            MaxQueuedDocs = 2,
            MaxBufferedDocs = 100
        };
        using var writer = new IndexWriter(directory, config);
        var semaphore = GetSemaphore(writer);
        Assert.NotNull(semaphore);

        Assert.True(semaphore!.Wait(0));
        Assert.Equal(1, semaphore.CurrentCount);

        using var cts = new CancellationTokenSource();
        var batch = new[]
        {
            MakeDoc("1", "first waiting"),
            MakeDoc("2", "second waiting")
        };

        var operation = writer.AddDocumentsAsync(batch, cts.Token).AsTask();
        Assert.True(SpinWait.SpinUntil(() => semaphore.CurrentCount == 0, TimeSpan.FromSeconds(5)));

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, semaphore.CurrentCount);
        semaphore.Release();
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentsAsync Failure Restores Backpressure Slots")]
    public async Task AddDocumentsAsync_Failure_RestoresBackpressureSlots()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentsAsync_Failure_RestoresBackpressureSlots)));
        var config = new IndexWriterConfig
        {
            MaxQueuedDocs = 8,
            MaxTokensPerDocument = 3,
            TokenBudgetPolicy = TokenBudgetPolicy.Reject
        };
        using var writer = new IndexWriter(directory, config);
        var semaphore = GetSemaphore(writer);
        Assert.NotNull(semaphore);
        int initial = semaphore!.CurrentCount;

        var docs = new List<LeanDocument>
        {
            MakeDoc("1", "ok one"),
            MakeDoc("2", "ok two"),
            MakeDoc("3", "a b c d e f g"),
            MakeDoc("4", "never indexed"),
        };

        await Assert.ThrowsAsync<TokenBudgetExceededException>(() => writer.AddDocumentsAsync(docs).AsTask());
        Assert.Equal(initial, semaphore.CurrentCount);
        Assert.Throws<InvalidOperationException>(() => writer.AddDocument(MakeDoc("after", "after failure")));
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentAsync After Dispose Throws")]
    public async Task AddDocumentAsync_AfterDispose_Throws()
    {
        var directory = new MMapDirectory(SubDir(nameof(AddDocumentAsync_AfterDispose_Throws)));
        var writer = new IndexWriter(directory, new IndexWriterConfig());
        writer.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => writer.AddDocumentAsync(MakeDoc("1", "disposed")).AsTask());
    }

    [Fact(DisplayName = "Async Indexing: CommitAsync After Dispose Throws")]
    public async Task CommitAsync_AfterDispose_Throws()
    {
        var directory = new MMapDirectory(SubDir(nameof(CommitAsync_AfterDispose_Throws)));
        var writer = new IndexWriter(directory, new IndexWriterConfig());
        writer.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await writer.CommitAsync());
    }
}
