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
    private readonly ITestOutputHelper _output;

    public LocalIndexExecutorTests(ITestOutputHelper output) => _output = output;

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

    [Fact]
    public async Task NonEmptyIndexUsesOneSegmentForAnAppendOnlyHundredDocumentBatch()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-bulk-append-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            LocalIndexDescriptor descriptor = new(
                PhysicalIndexId.New(),
                new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
                "bulk-append-schema",
                new MutableIndexSettings(null, null, "content", null),
                new IndexTopologySettings(1, 0));
            await using LocalIndexHandle handle = await store.CreateAsync(descriptor);
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 250 });
            OperationContext context = new("bulk-append", OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow);

            BulkDocumentOperation[] firstBatch = CreateBatch(0, DocumentOperationKind.Index);
            LocalWriteResult first = await executor.WriteAsync(context, handle,
                new BulkDocumentsRequest("books", firstBatch, Refresh: true, Durability: RequestedWriteDurability.Memory));
            Assert.Equal(100, first.AcceptedOperations);
            int segmentsBeforeAppend = handle.Runtime.Writer.GetNrtSegments().Count;

            BulkDocumentOperation[] secondBatch = Enumerable.Range(100, 100)
                .Select(static id => new BulkDocumentOperation(
                    id % 2 == 0 ? DocumentOperationKind.Index : DocumentOperationKind.Update,
                    $"doc-{id}",
                    JsonSerializer.SerializeToElement(new { content = $"searchable doc-{id}" })))
                .ToArray();
            LocalWriteResult second = await executor.WriteAsync(context, handle,
                new BulkDocumentsRequest("books", secondBatch, Durability: RequestedWriteDurability.Memory));
            Assert.Equal(100, second.AcceptedOperations);

            int segmentsAfterAppend = handle.Runtime.Writer.GetNrtSegments().Count;
            int segmentGrowth = segmentsAfterAppend - segmentsBeforeAppend;
            _output.WriteLine($"Append batch: 100 documents, segments {segmentsBeforeAppend} -> {segmentsAfterAppend} (+{segmentGrowth}).");
            Assert.True(segmentGrowth <= 2,
                $"An append-only batch of 100 documents should add at most two segments, but added {segmentGrowth} ({segmentsBeforeAppend} to {segmentsAfterAppend}).");

            Assert.True(handle.Runtime.Commits.Commit(refresh: true) is CommitPublished);
            SearchResponse search = await executor.SearchAsync(context with { Operation = OperationKind.Search }, handle,
                new SearchRequest(new TermQueryDefinition("content", "searchable"), Size: 250));
            Assert.Equal(200, search.TotalHits);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task MixedBatchWithExistingIdsPreservesIndexAndUpdateReplacementSemantics()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-bulk-replace-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            await using LocalIndexHandle handle = await store.CreateAsync(CreateDescriptor("bulk-replace-schema"));
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 250 });
            OperationContext context = CreateContext("bulk-replace");

            await executor.WriteAsync(context, handle, new BulkDocumentsRequest("books", [
                CreateOperation(DocumentOperationKind.Index, "doc-a", "originalalpha"),
                CreateOperation(DocumentOperationKind.Index, "doc-b", "originalbeta")
            ], Refresh: true, Durability: RequestedWriteDurability.Memory));

            LocalWriteResult replacement = await executor.WriteAsync(context, handle, new BulkDocumentsRequest("books", [
                CreateOperation(DocumentOperationKind.Index, "doc-a", "replacementalpha"),
                CreateOperation(DocumentOperationKind.Update, "doc-b", "replacementbeta"),
                CreateOperation(DocumentOperationKind.Index, "doc-c", "newdocument")
            ], Refresh: true, Durability: RequestedWriteDurability.Memory));

            Assert.Equal(3, replacement.AcceptedOperations);
            Assert.Equal(1, (await SearchAsync(executor, handle, "_id", "doc-a")).TotalHits);
            Assert.Equal(1, (await SearchAsync(executor, handle, "_id", "doc-b")).TotalHits);
            Assert.Equal(1, (await SearchAsync(executor, handle, "_id", "doc-c")).TotalHits);
            Assert.Equal(0, (await SearchAsync(executor, handle, "content", "originalalpha")).TotalHits);
            Assert.Equal(0, (await SearchAsync(executor, handle, "content", "originalbeta")).TotalHits);
            Assert.Equal(1, (await SearchAsync(executor, handle, "content", "replacementalpha")).TotalHits);
            Assert.Equal(1, (await SearchAsync(executor, handle, "content", "replacementbeta")).TotalHits);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DuplicateIdsWithinOneRequestKeepTheLastCallerOrderedReplacement()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-bulk-duplicate-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            await using LocalIndexHandle handle = await store.CreateAsync(CreateDescriptor("bulk-duplicate-schema"));
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 250 });

            LocalWriteResult write = await executor.WriteAsync(CreateContext("bulk-duplicate"), handle,
                new BulkDocumentsRequest("books", [
                    CreateOperation(DocumentOperationKind.Index, "doc-duplicate", "firstvalue"),
                    CreateOperation(DocumentOperationKind.Update, "doc-duplicate", "lastvalue")
                ], Refresh: true, Durability: RequestedWriteDurability.Memory));

            Assert.Equal(2, write.AcceptedOperations);
            Assert.Equal(1, (await SearchAsync(executor, handle, "_id", "doc-duplicate")).TotalHits);
            Assert.Equal(0, (await SearchAsync(executor, handle, "content", "firstvalue")).TotalHits);
            Assert.Equal(1, (await SearchAsync(executor, handle, "content", "lastvalue")).TotalHits);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task StaleVisibleGenerationCannotMakeAnExistingIdLookAppendOnly()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-bulk-stale-{Guid.NewGuid():N}");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            await using LocalIndexHandle handle = await store.CreateAsync(CreateDescriptor("bulk-stale-schema"));
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 250 });
            OperationContext context = CreateContext("bulk-stale");

            await executor.WriteAsync(context, handle,
                new BulkDocumentsRequest("books", [CreateOperation(DocumentOperationKind.Index, "doc-stale", "oldversion")],
                    Durability: RequestedWriteDurability.Memory));
            Assert.True(handle.Runtime.Commits.Commit(refresh: false) is CommitPublished);
            using (var visible = handle.Runtime.Searchers.AcquireLease())
                Assert.NotEqual(handle.Runtime.Writer.CurrentCommitGeneration, visible.CommitGeneration);

            LocalWriteResult replacement = await executor.WriteAsync(context, handle,
                new BulkDocumentsRequest("books", [CreateOperation(DocumentOperationKind.Update, "doc-stale", "newversion")],
                    Refresh: true, Durability: RequestedWriteDurability.Memory));

            Assert.Equal(1, replacement.AcceptedOperations);
            Assert.Equal(1, (await SearchAsync(executor, handle, "_id", "doc-stale")).TotalHits);
            Assert.Equal(0, (await SearchAsync(executor, handle, "content", "oldversion")).TotalHits);
            Assert.Equal(1, (await SearchAsync(executor, handle, "content", "newversion")).TotalHits);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task MappingAndValidationFailuresRetainPerOperationResults()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-bulk-validation-{Guid.NewGuid():N}");
        using JsonDocument invalidShape = JsonDocument.Parse("[]");
        try
        {
            await using LocalIndexStore store = new(root, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            await using LocalIndexHandle handle = await store.CreateAsync(CreateDescriptor("bulk-validation-schema"));
            LocalIndexExecutor executor = new(new ServerCoreOptions { DataRoot = root, MaximumSearchResults = 250 });

            LocalWriteResult write = await executor.WriteAsync(CreateContext("bulk-validation"), handle,
                new BulkDocumentsRequest("books", [
                    CreateOperation(DocumentOperationKind.Index, "valid", "validcontent"),
                    new BulkDocumentOperation(DocumentOperationKind.Index, "unknown-field", JsonSerializer.SerializeToElement(new { missing = "value" })),
                    new BulkDocumentOperation(DocumentOperationKind.Update, "invalid-shape", invalidShape.RootElement.Clone()),
                    CreateOperation(DocumentOperationKind.Index, "", "invalidid")
                ], Refresh: true, Durability: RequestedWriteDurability.Memory));

            Assert.Equal(1, write.AcceptedOperations);
            Assert.Collection(write.Items,
                static result => Assert.True(result.Accepted),
                static result => Assert.Equal("unknown_field", result.Failure?.Code),
                static result => Assert.Equal("invalid_document", result.Failure?.Code),
                static result => Assert.Equal("invalid_document_id", result.Failure?.Code));
            Assert.Equal(1, (await SearchAsync(executor, handle, "_id", "valid")).TotalHits);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private static BulkDocumentOperation[] CreateBatch(int start, DocumentOperationKind kind) =>
        Enumerable.Range(start, 100)
            .Select(id => new BulkDocumentOperation(
                kind,
                $"doc-{id}",
                JsonSerializer.SerializeToElement(new { content = $"searchable doc-{id}" })))
            .ToArray();

    private static LocalIndexDescriptor CreateDescriptor(string schemaHash) => new(
        PhysicalIndexId.New(),
        new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
        schemaHash,
        new MutableIndexSettings(null, null, "content", null),
        new IndexTopologySettings(1, 0));

    private static OperationContext CreateContext(string operationId) =>
        new(operationId, OperationKind.WriteDocuments, CallerIdentity.Anonymous, DateTimeOffset.UtcNow);

    private static BulkDocumentOperation CreateOperation(DocumentOperationKind kind, string id, string content) =>
        new(kind, id, JsonSerializer.SerializeToElement(new { content }));

    private static Task<SearchResponse> SearchAsync(LocalIndexExecutor executor, LocalIndexHandle handle, string field, string term) =>
        executor.SearchAsync(new OperationContext("bulk-write-search", OperationKind.Search, CallerIdentity.Anonymous, DateTimeOffset.UtcNow),
            handle, new SearchRequest(new TermQueryDefinition(field, term), Size: 250, IncludeDocuments: false)).AsTask();

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
