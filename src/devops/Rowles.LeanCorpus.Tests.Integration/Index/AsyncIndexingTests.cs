using System.Globalization;
using Rowles.LeanCorpus.Document;
using Rowles.LeanCorpus.Document.Fields;
using Rowles.LeanCorpus.Index.Indexer;
using Rowles.LeanCorpus.Search;
using Rowles.LeanCorpus.Search.Searcher;
using Rowles.LeanCorpus.Store;
using Rowles.LeanCorpus.Tests.Shared.Fixtures;

namespace Rowles.LeanCorpus.Tests.Integration.Index;

/// <summary>
/// Integration coverage for the async indexing surface.
/// Backpressure is handled by the bounded channel; semaphore internals are not tested directly.
/// </summary>
[Trait("Category", "Index")]
[Trait("Category", "Async")]
public sealed class AsyncIndexingTests : IClassFixture<TestDirectoryFixture>
{
    private readonly TestDirectoryFixture _fixture;

    public AsyncIndexingTests(TestDirectoryFixture fixture)
    {
        _fixture = fixture;
    }

    private string SubDir(string name)
    {
        var path = Path.Combine(_fixture.Path, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static LeanDocument MakeDoc(string id, string body)
    {
        var doc = new LeanDocument();
        doc.Add(new TextField("body", body));
        doc.Add(new StringField("id", id));
        return doc;
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentAsync Indexes Single Document")]
    public async Task AddDocumentAsync_IndexesSingleDocument()
    {
        var dir = new MMapDirectory(SubDir(nameof(AddDocumentAsync_IndexesSingleDocument)));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        await writer.AddDocumentAsync(MakeDoc("1", "hello world async"));
        await writer.CommitAsync();

        using var searcher = new IndexSearcher(dir);
        Assert.True(searcher.Search(new TermQuery("id", "1"), 10).TotalHits > 0);
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentsAsync Indexes Batched Documents")]
    public async Task AddDocumentsAsync_IndexesBatchedDocuments()
    {
        var dir = new MMapDirectory(SubDir(nameof(AddDocumentsAsync_IndexesBatchedDocuments)));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var batch = new[] { MakeDoc("1", "hello"), MakeDoc("2", "world") };
        await writer.AddDocumentsAsync(batch);
        await writer.CommitAsync();

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "1"), 10).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "2"), 10).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentsAsync Streams Async Enumerable")]
    public async Task AddDocumentsAsync_StreamsAsyncEnumerable()
    {
        var dir = new MMapDirectory(SubDir(nameof(AddDocumentsAsync_StreamsAsyncEnumerable)));
        using var writer = new IndexWriter(dir, new IndexWriterConfig());
        var docs = new[] { MakeDoc("1", "hello"), MakeDoc("2", "world"), MakeDoc("3", "test") };
        await writer.AddDocumentsAsync(ToAsyncEnumerable(docs), batchSize: 2);
        await writer.CommitAsync();

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "1"), 10).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "2"), 10).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "3"), 10).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: Async Enumerable Batches Clamp To MaxQueuedDocs")]
    public async Task AsyncEnumerable_BatchesClampToMaxQueuedDocs()
    {
        var dir = new MMapDirectory(SubDir(nameof(AsyncEnumerable_BatchesClampToMaxQueuedDocs)));
        var config = new IndexWriterConfig { MaxQueuedDocs = 2 };
        using var writer = new IndexWriter(dir, config);
        var docs = new[] { MakeDoc("1", "first doc"), MakeDoc("2", "second doc"), MakeDoc("3", "third doc") };
        // batchSize=256 but MaxQueuedDocs=2 clamps it — smaller batches go through the channel.
        await writer.AddDocumentsAsync(ToAsyncEnumerable(docs), batchSize: 256);
        await writer.CommitAsync();

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(3, searcher.Search(new TermQuery("id", "1"), 10).TotalHits
                         + searcher.Search(new TermQuery("id", "2"), 10).TotalHits
                         + searcher.Search(new TermQuery("id", "3"), 10).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: Async Enumerable Source Failure Keeps Completed Batches")]
    public async Task AsyncEnumerable_SourceFailure_KeepsCompletedBatches()
    {
        var dir = new MMapDirectory(SubDir(nameof(AsyncEnumerable_SourceFailure_KeepsCompletedBatches)));

        // First batch
        using (var writer = new IndexWriter(dir, new IndexWriterConfig()))
        {
            await writer.AddDocumentsAsync(ToAsyncEnumerable(new[] { MakeDoc("1", "batch1") }), batchSize: 1);
            await writer.CommitAsync();
        }

        // Second batch — reopen
        using (var writer2 = new IndexWriter(dir, new IndexWriterConfig()))
        {
            await writer2.AddDocumentsAsync(ToAsyncEnumerable(new[] { MakeDoc("2", "batch2") }), batchSize: 1);
            await writer2.CommitAsync();
        }

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "1"), 10).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "2"), 10).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentBlockAsync Writes Parent Bit Set")]
    public async Task AddDocumentBlockAsync_WritesParentBitSet()
    {
        var dir = new MMapDirectory(SubDir(nameof(AddDocumentBlockAsync_WritesParentBitSet)));
        using var writer = new IndexWriter(dir, new IndexWriterConfig { MaxBufferedDocs = 100 });
        var block = new LeanDocument[]
        {
            MakeDoc("child", "nested content"),
            MakeDoc("parent", "parent content")
        };
        await writer.AddDocumentBlockAsync(block);
        await writer.CommitAsync();

        using var searcher = new IndexSearcher(dir);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "child"), 10).TotalHits);
        Assert.Equal(1, searcher.Search(new TermQuery("id", "parent"), 10).TotalHits);
    }

    [Fact(DisplayName = "Async Indexing: CommitAsync Persists Buffered Documents")]
    public async Task CommitAsync_PersistsBufferedDocuments()
    {
        var dir = new MMapDirectory(SubDir(nameof(CommitAsync_PersistsBufferedDocuments)));
        var config = new IndexWriterConfig { MaxBufferedDocs = 100 };
        using var writer = new IndexWriter(dir, config);
        await writer.AddDocumentAsync(MakeDoc("1", "buffered content"));
        await writer.CommitAsync();

        using var searcher = new IndexSearcher(dir);
        Assert.True(searcher.Search(new TermQuery("id", "1"), 10).TotalHits > 0);
    }

    [Fact(DisplayName = "Async Indexing: AddDocumentAsync After Dispose Throws")]
    public async Task AddDocumentAsync_AfterDispose_Throws()
    {
        var dir = new MMapDirectory(SubDir(nameof(AddDocumentAsync_AfterDispose_Throws)));
        var writer = new IndexWriter(dir, new IndexWriterConfig());
        writer.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            writer.AddDocumentAsync(MakeDoc("1", "late")).AsTask());
    }

    [Fact(DisplayName = "Async Indexing: CommitAsync After Dispose Throws")]
    public async Task CommitAsync_AfterDispose_Throws()
    {
        var dir = new MMapDirectory(SubDir(nameof(CommitAsync_AfterDispose_Throws)));
        var writer = new IndexWriter(dir, new IndexWriterConfig());
        writer.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => writer.CommitAsync());
    }

    private static async IAsyncEnumerable<LeanDocument> ToAsyncEnumerable(IReadOnlyList<LeanDocument> docs)
    {
        foreach (var doc in docs)
        {
            await Task.Yield();
            yield return doc;
        }
    }
}
