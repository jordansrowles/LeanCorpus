using System.Text.Json;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Core.Configuration;
using Rowles.LeanCorpus.Server.Core.Services;

namespace Rowles.LeanCorpus.Server.Core.Tests;

[Trait("Area", "Server")]
public sealed class SchemaValidationTests
{
    [Fact]
    public async Task MultiValueAndScalarTypeRulesAreEnforcedPerOperation()
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root });
            CreateIndexRequest create = new(
                "typed",
                new IndexSchema(
                [
                    new IndexFieldDefinition("title", IndexFieldType.Text, true, true),
                    new IndexFieldDefinition("tags", IndexFieldType.Keyword, true, true, MultiValued: true),
                    new IndexFieldDefinition("number", IndexFieldType.Int64, true, true),
                    new IndexFieldDefinition("payload", IndexFieldType.Binary, false, true),
                    new IndexFieldDefinition("embedding", IndexFieldType.Vector, true, false, VectorDimensions: 2)
                ], new Dictionary<string, AnalysisDefinition>()),
                new IndexTopologySettings(1, 0),
                new MutableIndexSettings(null, null, "title", null));
            Assert.True((await server.CreateAsync(create)).IsSuccess);

            using JsonDocument valid = JsonDocument.Parse("{\"title\":\"valid\",\"tags\":[\"one\",\"two\"],\"number\":1,\"payload\":\"AQI=\",\"embedding\":[1,0]}");
            using JsonDocument titleArray = JsonDocument.Parse("{\"title\":[\"invalid\"]}");
            using JsonDocument fractional = JsonDocument.Parse("{\"number\":1.5}");
            using JsonDocument vector = JsonDocument.Parse("{\"embedding\":[1,0,2]}");
            using JsonDocument binary = JsonDocument.Parse("{\"payload\":\"not base64\"}");
            var response = (await server.BulkAsync(new BulkDocumentsRequest("typed",
            [
                new BulkDocumentOperation(DocumentOperationKind.Index, "valid", valid.RootElement.Clone()),
                new BulkDocumentOperation(DocumentOperationKind.Index, "array", titleArray.RootElement.Clone()),
                new BulkDocumentOperation(DocumentOperationKind.Index, "fractional", fractional.RootElement.Clone()),
                new BulkDocumentOperation(DocumentOperationKind.Index, "vector", vector.RootElement.Clone()),
                new BulkDocumentOperation(DocumentOperationKind.Index, "binary", binary.RootElement.Clone())
            ], Refresh: true))).Value!;

            Assert.True(response.Items[0].Accepted);
            Assert.Equal("multi_value_not_allowed", response.Items[1].Failure?.Code);
            Assert.Equal("schema_validation", response.Items[2].Failure?.Code);
            Assert.Equal("schema_validation", response.Items[3].Failure?.Code);
            Assert.Equal("schema_validation", response.Items[4].Failure?.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Theory]
    [InlineData("_id", "standard")]
    [InlineData("content", "missing-analyser")]
    public async Task ReservedFieldsAndUnknownAnalysersAreRejected(string fieldName, string analyser)
    {
        string root = TemporaryRoot();
        try
        {
            using LocalServerCore server = await LocalServerCore.OpenAsync(new ServerCoreOptions { DataRoot = root });
            CreateIndexRequest request = new(
                "invalid",
                new IndexSchema([new IndexFieldDefinition(fieldName, IndexFieldType.Text, true, true, Analyser: analyser)], new Dictionary<string, AnalysisDefinition>()),
                new IndexTopologySettings(1, 0),
                new MutableIndexSettings(null, null, null, null));
            Assert.Equal("invalid_schema", (await server.CreateAsync(request)).Failure?.Code);
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string TemporaryRoot() => Path.Combine(Path.GetTempPath(), $"lean-corpus-server-schema-{Guid.NewGuid():N}");
    private static void DeleteRoot(string root) { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
