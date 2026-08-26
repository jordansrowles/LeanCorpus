using System.Text.Json;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class LocalIndexExecutorTests
{
    [Fact]
    public async Task MemoryBackedPayloadUsesReadOnlyStreamWithoutAnIntermediateCopy()
    {
        byte[] payload = "snapshot-manifest"u8.ToArray();
        await using Stream stream = LocalStreamAdapters.ReadOnly(payload);
        byte[] read = new byte[payload.Length];
        int count = await stream.ReadAsync(read);
        Assert.Equal(payload.Length, count);
        Assert.Equal(payload, read);
        Assert.False(stream.CanWrite);
    }

    [Fact]
    public async Task ExecutesWithAnAlreadyEstablishedOperationContext()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-executor-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            LocalIndexDescriptor descriptor = new(
                PhysicalIndexId.New(),
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "executor-schema",
                new MutableIndexSettings(null, null, "content", null),
                new IndexTopologySettings(1, 0));
            await using LocalIndexHandle handle = await store.CreateAsync(descriptor);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });
            OperationContext context = new("request-1", OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow, "books");
            using JsonDocument document = JsonDocument.Parse("{\"content\":\"local execution\"}");

            LocalWriteResult write = await executor.WriteAsync(context, handle, new BulkDocumentsRequest(
                "books", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", document.RootElement.Clone())], Refresh: true));
            Assert.Equal(1, write.AcceptedOperations);
            Assert.True(write.Committed);
            Assert.NotNull(write.Receipt);
            Assert.Equal(write.Receipt!.FirstSequenceNumber, write.Receipt.LastSequenceNumber);
            Assert.True(write.Receipt.CommitGeneration > 0);
            Assert.True(write.Receipt.ContentToken > 0);

            SearchResponse search = await executor.SearchAsync(context with { Operation = OperationKind.Search }, handle,
                new SearchRequest(new TermQueryDefinition("content", "local")));
            Assert.Single(search.Hits);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CoordinatorCompletesSequenceWaitersFromOneExplicitCommit()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-coordinator-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1));
            LocalIndexDescriptor descriptor = new(PhysicalIndexId.New(),
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "coordinator-schema", new MutableIndexSettings(null, null, "content", null), new IndexTopologySettings(1, 0));
            await using LocalIndexHandle handle = await store.CreateAsync(descriptor);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });
            OperationContext context = new("request-2", OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow);
            using JsonDocument first = JsonDocument.Parse("{\"content\":\"first\"}");
            using JsonDocument second = JsonDocument.Parse("{\"content\":\"second\"}");
            LocalWriteResult one = await executor.WriteAsync(context, handle, new BulkDocumentsRequest("index", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", first.RootElement.Clone())]));
            LocalWriteResult two = await executor.WriteAsync(context, handle, new BulkDocumentsRequest("index", [new BulkDocumentOperation(DocumentOperationKind.Index, "two", second.RootElement.Clone())]));
            Task<LocalCommitReceipt> waiterOne = handle.CommitCoordinator.WaitUntilCommittedAsync(one.SequenceNumber).AsTask();
            Task<LocalCommitReceipt> waiterTwo = handle.CommitCoordinator.WaitUntilCommittedAsync(two.SequenceNumber).AsTask();

            CommitResult result = handle.CommitCoordinator.Commit(refresh: true);
            CommitPublished published = result switch
            {
                CommitPublished value => value,
                NothingToCommit => throw new Xunit.Sdk.XunitException("The explicit commit unexpectedly had no pending writes."),
                CommitFailed failed => throw new Xunit.Sdk.XunitException($"The explicit commit failed: {failed.Message}")
            };
            Assert.Same(published.Receipt, await waiterOne);
            Assert.Same(published.Receipt, await waiterTwo);
            Assert.Equal(two.SequenceNumber, published.Receipt.LastSequenceNumber);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task CommitObserverRunsAfterPublicationAndDoesNotChangeTheReceipt()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-observer-{Guid.NewGuid():N}");
        RecordingObserver observer = new();
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), observer);
            LocalIndexDescriptor descriptor = new(PhysicalIndexId.New(),
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "observer-schema", new MutableIndexSettings(null, null, "content", null), new IndexTopologySettings(1, 0));
            await using LocalIndexHandle handle = await store.CreateAsync(descriptor);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });
            using JsonDocument document = JsonDocument.Parse("{\"content\":\"observed\"}");
            LocalWriteResult write = await executor.WriteAsync(new OperationContext("request-3", OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow), handle,
                new BulkDocumentsRequest("index", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", document.RootElement.Clone())], Refresh: true));
            Assert.NotNull(write.Receipt);
            Assert.Same(write.Receipt, observer.Receipt);
            Assert.Equal(descriptor.Id, observer.Index?.Id);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SynchronousObserverFailureDoesNotUndoPublishedCommit()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-observer-failure-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(1), new ThrowingObserver());
            LocalIndexDescriptor descriptor = new(PhysicalIndexId.New(),
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "observer-failure-schema", new MutableIndexSettings(null, null, "content", null), new IndexTopologySettings(1, 0));
            await using LocalIndexHandle handle = await store.CreateAsync(descriptor);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root });
            using JsonDocument document = JsonDocument.Parse("{\"content\":\"published\"}");
            LocalWriteResult write = await executor.WriteAsync(new OperationContext("request-4", OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow), handle,
                new BulkDocumentsRequest("index", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", document.RootElement.Clone())], Refresh: true));

            Assert.True(write.Committed);
            Assert.NotNull(write.Receipt);
            Assert.Equal(1, handle.Health.ConsecutiveCommitFailures);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class RecordingObserver : ILocalCommitObserver
    {
        internal LocalIndexDescriptor? Index { get; private set; }
        internal LocalCommitReceipt? Receipt { get; private set; }

        public ValueTask OnCommittedAsync(LocalIndexDescriptor index, LocalCommitReceipt receipt, CancellationToken cancellationToken = default)
        {
            Index = index;
            Receipt = receipt;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingObserver : ILocalCommitObserver
    {
        public ValueTask OnCommittedAsync(LocalIndexDescriptor index, LocalCommitReceipt receipt, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("observer failure");
    }
}
