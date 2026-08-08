using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

namespace Rowles.LeanCorpus.Server.Core.Tests;

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

    private static CreateIndexRequest CreateRequest(string name) => new(
        name,
        new IndexSchema([new IndexFieldDefinition("content", IndexFieldType.Text, true, true)], new Dictionary<string, AnalysisDefinition>()),
        new IndexTopologySettings(1, 0),
        new MutableIndexSettings(null, null, "content", null));
}
