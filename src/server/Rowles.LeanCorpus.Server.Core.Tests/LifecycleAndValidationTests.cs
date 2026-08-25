using System.Text.Json;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class LifecycleAndValidationTests
{
    [Fact]
    public async Task DuplicateInvalidNameAndInvalidSchemaDoNotCreateStorage()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root });
            Assert.True((await server.CreateAsync(CreateRequest("books"))).IsSuccess);
            Assert.Equal("index_exists", (await server.CreateAsync(CreateRequest("books"))).Failure?.Code);
            Assert.Equal("invalid_index_name", (await server.CreateAsync(CreateRequest("../escape"))).Failure?.Code);

            CreateIndexRequest invalid = CreateRequest("invalid") with { Topology = new IndexTopologySettings(2, 0) };
            Assert.Equal("invalid_schema", (await server.CreateAsync(invalid)).Failure?.Code);
            Assert.Single(Directory.EnumerateDirectories(Path.Combine(root, "indices")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"formatVersion\":2,\"indices\":[]}")]
    public async Task CorruptAndFutureRegistryFormatsFailExplicitly(string registry)
    {
        string root = TemporaryRoot();
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "registry.json"), registry);
            await Assert.ThrowsAnyAsync<InvalidDataException>(async () =>
                await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root }));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task SettingsAndCommittedDocumentsSurviveRestart()
    {
        string root = TemporaryRoot();
        try
        {
            using (LocalServerCore first = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root }))
            {
                Assert.True((await first.CreateAsync(CreateRequest("books"))).IsSuccess);
                MutableIndexSettings settings = new(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), "content", 64);
                Assert.True((await first.UpdateSettingsAsync(new UpdateIndexSettingsRequest("books", settings, ConfirmationTokens.Create("update-settings", "books")))).IsSuccess);
                using JsonDocument document = JsonDocument.Parse("{\"content\":\"restart proof\"}");
                Assert.True((await first.BulkAsync(new BulkDocumentsRequest("books", [new BulkDocumentOperation(DocumentOperationKind.Index, "one", document.RootElement.Clone())], Refresh: true))).IsSuccess);
            }

            using LocalServerCore second = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root });
            ServiceResult<IndexSchemaResponse> schema = await second.GetSchemaAsync("books");
            Assert.Equal(64, schema.Value!.Settings.MaximumQueryClauses);
            Assert.Equal(TimeSpan.FromSeconds(2), schema.Value.Settings.RefreshInterval);
            Assert.Single((await second.SearchAsync("books", new(new Rowles.LeanCorpus.Server.Abstractions.Contracts.Search.TermQueryDefinition("content", "restart")))).Value!.Hits);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task BulkAndDocumentLimitsReturnStableFailures()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions
            {
                DataRoot = root,
                MaximumBulkOperations = 1,
                MaximumDocumentBytes = 24
            });
            Assert.True((await server.CreateAsync(CreateRequest("books"))).IsSuccess);
            using JsonDocument small = JsonDocument.Parse("{\"content\":\"ok\"}");
            BulkDocumentOperation operation = new(DocumentOperationKind.Index, "one", small.RootElement.Clone());
            Assert.Equal("invalid_bulk_request", (await server.BulkAsync(new BulkDocumentsRequest("books", [operation, operation]))).Failure?.Code);

            using JsonDocument large = JsonDocument.Parse("{\"content\":\"this document is deliberately too large\"}");
            ServiceResult<BulkDocumentsResponse> result = await server.BulkAsync(new BulkDocumentsRequest("books", [new BulkDocumentOperation(DocumentOperationKind.Index, "two", large.RootElement.Clone())]));
            Assert.Equal("document_too_large", result.Value!.Items[0].Failure?.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task DeleteRemovesRegistryEntryAndOpaqueStorageDirectory()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root });
            IndexSummary created = (await server.CreateAsync(CreateRequest("books"))).Value!;
            string path = Path.Combine(root, "indices", created.IndexId);
            Assert.True(Directory.Exists(path));

            Assert.True((await server.DeleteAsync(new DeleteIndexRequest("books", ConfirmationTokens.Create("delete-index", "books")))).IsSuccess);
            Assert.False(Directory.Exists(path));
            Assert.Empty((await server.ListAsync()).Value!);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static CreateIndexRequest CreateRequest(string name) => new(
        name,
        new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
        new IndexTopologySettings(1, 0),
        new MutableIndexSettings(null, null, "content", null));

    private static string TemporaryRoot() => Path.Combine(Path.GetTempPath(), $"lean-corpus-server-lifecycle-{Guid.NewGuid():N}");

    private static void DeleteRoot(string root)
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
