using System.Text.Json;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

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
