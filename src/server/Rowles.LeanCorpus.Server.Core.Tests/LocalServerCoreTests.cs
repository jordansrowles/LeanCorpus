using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class LocalServerCoreTests
{
    [Fact]
    public async Task CreateListAndReopenIndexPreservesRegistration()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-{Guid.NewGuid():N}");
        try
        {
            ServerCoreOptions options = new() { DataRoot = root };
            LocalServerCoreScope first = await LocalServerCoreScope.OpenAsync(options);
            var created = await first.Server.CreateAsync(CreateRequest("books"));
            Assert.True(created.IsSuccess);
            Assert.Single((await first.Server.ListAsync()).Value!);

            await first.DisposeAsync();
            await using LocalServerCoreScope second = await LocalServerCoreScope.OpenAsync(options);
            var indices = await second.Server.ListAsync();
            Assert.Single(indices.Value!);
            Assert.Equal("books", indices.Value![0].IndexName);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task UnknownIndexDoesNotCreateStorage()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-{Guid.NewGuid():N}");
        try
        {
            await using LocalServerCoreScope scope = await LocalServerCoreScope.OpenAsync(new ServerCoreOptions { DataRoot = root });
            var schema = await scope.Server.GetSchemaAsync("missing");
            Assert.False(schema.IsSuccess);
            Assert.Empty((await scope.Server.ListAsync()).Value!);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task IndexedDocumentIsSearchableAfterRefresh()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-{Guid.NewGuid():N}");
        try
        {
            await using LocalServerCoreScope scope = await LocalServerCoreScope.OpenAsync(new ServerCoreOptions { DataRoot = root });
            Assert.True((await scope.Server.CreateAsync(CreateRequest("books"))).IsSuccess);
            using JsonDocument document = JsonDocument.Parse("{\"content\":\"practical search\"}");
            var indexed = await scope.Server.BulkAsync(new BulkDocumentsRequest("books", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", document.RootElement.Clone())], Refresh: true));

            Assert.True(indexed.IsSuccess);
            var search = await scope.Server.SearchAsync("books", new SearchRequest(new TermQueryDefinition("_id", "one")));
            Assert.True(search.IsSuccess);
            Assert.Single(search.Value!.Hits);
            Assert.Equal("one", search.Value.Hits[0].DocumentId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task SchemaTypesAreMappedAndUnknownFieldsAreRejected()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-{Guid.NewGuid():N}");
        try
        {
            await using LocalServerCoreScope scope = await LocalServerCoreScope.OpenAsync(new ServerCoreOptions { DataRoot = root });
            CreateIndexRequest request = new(
                "typed",
                new IndexSchema(
                [
                    new IndexFieldDefinition("title", IndexFieldType.Text, true, true),
                    new IndexFieldDefinition("tag", IndexFieldType.Keyword, true, true),
                    new IndexFieldDefinition("number", IndexFieldType.Int64, true, true),
                    new IndexFieldDefinition("ratio", IndexFieldType.Double, true, true),
                    new IndexFieldDefinition("enabled", IndexFieldType.Boolean, true, true),
                    new IndexFieldDefinition("when", IndexFieldType.DateTime, true, true),
                    new IndexFieldDefinition("payload", IndexFieldType.Binary, false, true),
                    new IndexFieldDefinition("embedding", IndexFieldType.Vector, true, true, VectorDimensions: 3)
                ], new Dictionary<string, AnalysisDefinition>()),
                new IndexTopologySettings(1, 0),
                new MutableIndexSettings(null, null, "title", null));
            Assert.True((await scope.Server.CreateAsync(request)).IsSuccess);
            using JsonDocument document = JsonDocument.Parse("{\"title\":\"quick fox\",\"tag\":\"animal\",\"number\":42,\"ratio\":1.5,\"enabled\":true,\"when\":\"2026-01-01T00:00:00Z\",\"payload\":\"AQI=\",\"embedding\":[0.1,0.2,0.3]}");
            var result = await scope.Server.BulkAsync(new BulkDocumentsRequest("typed", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", document.RootElement.Clone())], Refresh: true));
            Assert.True(result.IsSuccess);
            Assert.True(result.Value!.Items[0].Accepted);

            var search = await scope.Server.SearchAsync("typed", new SearchRequest(new TermQueryDefinition("tag", "animal")));
            Assert.True(search.IsSuccess);
            Assert.Single(search.Value!.Hits);

            using JsonDocument unknown = JsonDocument.Parse("{\"unknown\":true}");
            var invalid = await scope.Server.BulkAsync(new BulkDocumentsRequest("typed", [new BulkDocumentOperation(DocumentOperationKind.Index, "two", unknown.RootElement.Clone())]));
            Assert.True(invalid.IsSuccess);
            Assert.Equal("unknown_field", invalid.Value!.Items[0].Failure!.Code);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task AdvertisedQueryDefinitionsExecuteThroughTheSameSearchPath()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-{Guid.NewGuid():N}");
        try
        {
            await using LocalServerCoreScope scope = await LocalServerCoreScope.OpenAsync(new ServerCoreOptions { DataRoot = root });
            CreateIndexRequest request = new(
                "queries",
                new IndexSchema(
                [
                    new IndexFieldDefinition("content", IndexFieldType.Text, true, true),
                    new IndexFieldDefinition("embedding", IndexFieldType.Vector, true, true, VectorDimensions: 3)
                ], new Dictionary<string, AnalysisDefinition>()),
                new IndexTopologySettings(1, 0),
                new MutableIndexSettings(null, null, "content", null));
            Assert.True((await scope.Server.CreateAsync(request)).IsSuccess);
            using JsonDocument first = JsonDocument.Parse("{\"content\":\"quick brown fox\",\"embedding\":[1,0,0]}");
            using JsonDocument second = JsonDocument.Parse("{\"content\":\"slow red fox\",\"embedding\":[0,1,0]}");
            Assert.True((await scope.Server.BulkAsync(new BulkDocumentsRequest("queries", [
                new BulkDocumentOperation(DocumentOperationKind.Index, "one", first.RootElement.Clone()),
                new BulkDocumentOperation(DocumentOperationKind.Index, "two", second.RootElement.Clone())], Refresh: true))).IsSuccess);

            QueryDefinition[] definitions =
            [
                new QueryStringDefinition("quick"),
                new TermQueryDefinition("content", "quick"),
                new BooleanQueryDefinition(Must: [new TermQueryDefinition("content", "fox")]),
                new PhraseQueryDefinition("content", ["quick", "brown"]),
                new PrefixQueryDefinition("content", "bro"),
                new WildcardQueryDefinition("content", "qu*"),
                new RegexpQueryDefinition("content", "q.*"),
                new SpanNearQueryDefinition([new TermQueryDefinition("content", "quick"), new TermQueryDefinition("content", "brown")], 1, true),
                new VectorQueryDefinition("embedding", [1, 0, 0], 1)
            ];
            foreach (QueryDefinition definition in definitions)
            {
                var search = await scope.Server.SearchAsync("queries", new SearchRequest(definition, IncludeDocuments: false));
                Assert.True(search.IsSuccess, search.Failure?.Message);
                Assert.NotEmpty(search.Value!.Hits);
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task IdempotencyReplaysEquivalentResultsAndRejectsDifferentPayloads()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-{Guid.NewGuid():N}");
        try
        {
            await using LocalServerCoreScope scope = await LocalServerCoreScope.OpenAsync(new ServerCoreOptions { DataRoot = root });
            Assert.True((await scope.Server.CreateAsync(CreateRequest("books"))).IsSuccess);
            using JsonDocument document = JsonDocument.Parse("{\"content\":\"idempotent\"}");
            BulkDocumentsRequest request = new("books", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", document.RootElement.Clone())], Refresh: true, IdempotencyKey: "request-1");

            ServiceResult<BulkDocumentsResponse> first = await scope.Server.BulkAsync(request);
            ServiceResult<BulkDocumentsResponse> replay = await scope.Server.BulkAsync(request);
            Assert.True(first.IsSuccess);
            Assert.True(replay.IsSuccess);
            Assert.Equal(first.Value, replay.Value);

            using JsonDocument changed = JsonDocument.Parse("{\"content\":\"different\"}");
            ServiceResult<BulkDocumentsResponse> conflict = await scope.Server.BulkAsync(request with
            {
                Operations = [new BulkDocumentOperation(DocumentOperationKind.Index, "one", changed.RootElement.Clone())]
            });
            Assert.False(conflict.IsSuccess);
            Assert.Equal("idempotency_conflict", conflict.Failure!.Code);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task DestructiveOperationsRequireOperationSpecificConfirmation()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-{Guid.NewGuid():N}");
        try
        {
            await using LocalServerCoreScope scope = await LocalServerCoreScope.OpenAsync(new ServerCoreOptions { DataRoot = root });
            Assert.True((await scope.Server.CreateAsync(CreateRequest("books"))).IsSuccess);

            ServiceResult<bool> missing = await scope.Server.DeleteAsync(new DeleteIndexRequest("books", string.Empty));
            Assert.False(missing.IsSuccess);
            Assert.Equal("confirmation_required", missing.Failure!.Code);

            ServiceResult<bool> deleted = await scope.Server.DeleteAsync(new DeleteIndexRequest("books", ConfirmationTokens.Create("delete-index", "books")));
            Assert.True(deleted.IsSuccess);
            ServiceResult<IReadOnlyList<IndexSummary>> remaining = await scope.Server.ListAsync();
            Assert.Empty(remaining.Value!);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task InspectionHonoursTheConfiguredBound()
    {
        string root = Path.Combine(Path.GetTempPath(), $"lean-corpus-server-{Guid.NewGuid():N}");
        try
        {
            await using LocalServerCoreScope scope = await LocalServerCoreScope.OpenAsync(new ServerCoreOptions { DataRoot = root, MaximumInspectionItems = 1 });
            Assert.True((await scope.Server.CreateAsync(CreateRequest("books"))).IsSuccess);
            using JsonDocument first = JsonDocument.Parse("{\"content\":\"one\"}");
            using JsonDocument second = JsonDocument.Parse("{\"content\":\"two\"}");
            Assert.True((await scope.Server.BulkAsync(new BulkDocumentsRequest("books", [
                new BulkDocumentOperation(DocumentOperationKind.Index, "one", first.RootElement.Clone()),
                new BulkDocumentOperation(DocumentOperationKind.Index, "two", second.RootElement.Clone())], Refresh: true))).IsSuccess);

            ServiceResult<InspectionResponse> inspection = await scope.Server.InspectAsync("books", new InspectionRequest(InspectionResource.Documents, 1));
            Assert.True(inspection.IsSuccess);
            Assert.True(inspection.Value!.IsTruncated);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static CreateIndexRequest CreateRequest(string name) => new(
        name,
        new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
        new IndexTopologySettings(1, 0),
        new MutableIndexSettings(null, null, "content", null));
}
