using System.Text.Json;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Abstractions.Serialisation;

namespace Rowles.LeanCorpus.Server.Abstractions.Tests;

[Trait("Area", "Server")]
public sealed class ContractArtefactTests
{
    [Fact]
    public void OpenApiContractIsVersionedAndContainsThePublicEndpoints()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Contracts", "OpenApi", "lean-corpus-server.community.v1.openapi.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal("3.1.0", document.RootElement.GetProperty("openapi").GetString());
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/v1/indices/{name}/search", out _));
        Assert.False(document.RootElement.GetProperty("paths").TryGetProperty("/v1/admin/snapshots/{id}:restore", out _));
        JsonElement schemas = document.RootElement.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("WriteToken", out _));
        Assert.Contains("ReadYourWrites", schemas.GetProperty("SearchRequest").GetProperty("properties").GetProperty("consistency").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("LocalFsync", schemas.GetProperty("BulkDocumentsRequest").GetProperty("properties").GetProperty("durability").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public void ProtobufContractDeclaresTheCustomerServices()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Contracts", "Grpc", "lean-corpus-server.v1.proto");
        string contract = File.ReadAllText(path);

        Assert.Contains("service SearchService", contract, StringComparison.Ordinal);
        Assert.Contains("service IndexService", contract, StringComparison.Ordinal);
        Assert.Contains("message SearchRequest", contract, StringComparison.Ordinal);
        Assert.Contains("message BulkDocumentsRequest", contract, StringComparison.Ordinal);
        Assert.Contains("service InspectionService", contract, StringComparison.Ordinal);
        Assert.DoesNotContain("JsonEnvelope", contract, StringComparison.Ordinal);
    }

    [Fact]
    public void WriteAndReadContractShapesAgreeAcrossJsonOpenApiAndProtobuf()
    {
        string openApiPath = Path.Combine(AppContext.BaseDirectory, "Contracts", "OpenApi", "lean-corpus-server.community.v1.openapi.json");
        using JsonDocument openApi = JsonDocument.Parse(File.ReadAllText(openApiPath));
        JsonElement schemas = openApi.RootElement.GetProperty("components").GetProperty("schemas");

        JsonElement writeToken = schemas.GetProperty("WriteToken");
        Assert.Equal(["version", "indexId", "sequenceNumber"], writeToken.GetProperty("required").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal("integer", writeToken.GetProperty("properties").GetProperty("version").GetProperty("type").GetString());
        Assert.Equal("integer", writeToken.GetProperty("properties").GetProperty("sequenceNumber").GetProperty("type").GetString());

        JsonElement durability = schemas.GetProperty("BulkDocumentsRequest").GetProperty("properties").GetProperty("durability");
        Assert.Equal("string", durability.GetProperty("type").GetString());
        Assert.Equal(Enum.GetNames<RequestedWriteDurability>(), durability.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!).ToArray());

        JsonElement consistency = schemas.GetProperty("SearchRequest").GetProperty("properties").GetProperty("consistency");
        Assert.Equal("string", consistency.GetProperty("type").GetString());
        Assert.Equal(Enum.GetNames<RequestedConsistency>(), consistency.GetProperty("enum").EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Contains("query", schemas.GetProperty("SearchRequest").GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(["queryString", "term", "boolean", "phrase", "prefix", "wildcard", "regexp", "spanNear", "vector"],
            schemas.GetProperty("SearchRequest").GetProperty("properties").GetProperty("query").GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(value => value.GetString()!).ToArray());

        BulkDocumentsRequest bulk = new("books", [new(DocumentOperationKind.Index, "one", JsonDocument.Parse("{}").RootElement.Clone())], Durability: RequestedWriteDurability.LocalFsync);
        using JsonDocument bulkJson = JsonDocument.Parse(JsonSerializer.Serialize(bulk, ServerJsonSerialiserContext.Default.BulkDocumentsRequest));
        Assert.Equal("LocalFsync", bulkJson.RootElement.GetProperty("durability").GetString());
        Assert.Equal("one", bulkJson.RootElement.GetProperty("operations")[0].GetProperty("documentId").GetString());

        SearchRequest search = new(new TermQueryDefinition("title", "value"), Consistency: RequestedConsistency.ReadYourWrites,
            ReadToken: new WriteToken(1, "physical", 7, null, null));
        using JsonDocument searchJson = JsonDocument.Parse(JsonSerializer.Serialize(search, ServerJsonSerialiserContext.Default.SearchRequest));
        Assert.Equal("term", searchJson.RootElement.GetProperty("query").GetProperty("kind").GetString());
        Assert.Equal("ReadYourWrites", searchJson.RootElement.GetProperty("consistency").GetString());
        Assert.Equal("physical", searchJson.RootElement.GetProperty("readToken").GetProperty("indexId").GetString());

        string protoPath = Path.Combine(AppContext.BaseDirectory, "Contracts", "Grpc", "lean-corpus-server.v1.proto");
        string proto = File.ReadAllText(protoPath);
        Assert.Contains("message WriteToken { int32 version = 1; string index_id = 2; int64 sequence_number = 3; optional int64 commit_generation = 4; optional int64 content_token = 5; }", proto, StringComparison.Ordinal);
        Assert.Contains("message BulkDocumentsRequest { string index_name = 1; repeated BulkDocumentOperation operations = 2; bool refresh = 3; string idempotency_key = 4; string durability = 5; }", proto, StringComparison.Ordinal);
        Assert.Contains("string consistency = 7;", proto, StringComparison.Ordinal);
        Assert.Contains("WriteToken read_token = 10;", proto, StringComparison.Ordinal);
        Assert.Contains("google.protobuf.Struct query = 2;", proto, StringComparison.Ordinal);
    }

    [Fact]
    public void CommunityOpenApiPathsMatchTheEndpointCatalogue()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Contracts", "OpenApi", "lean-corpus-server.community.v1.openapi.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        HashSet<string> openApiRoutes = [];
        foreach (JsonProperty property in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            if (property.Name.StartsWith("/v1/", StringComparison.Ordinal))
            {
                foreach (JsonProperty method in property.Value.EnumerateObject())
                    openApiRoutes.Add($"{method.Name.ToUpperInvariant()} {property.Name}");
            }
        }

        HashSet<string> catalogueRoutes = ServerEndpointCatalog.All
            .Where(static endpoint => endpoint.Edition == ApiEdition.Community)
            .Select(static endpoint => $"{endpoint.Method} {endpoint.Route}")
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(catalogueRoutes.SetEquals(openApiRoutes), $"Community OpenAPI routes differ from the catalogue. Expected: {string.Join(", ", catalogueRoutes.Order())}; actual: {string.Join(", ", openApiRoutes.Order())}");
    }
}
