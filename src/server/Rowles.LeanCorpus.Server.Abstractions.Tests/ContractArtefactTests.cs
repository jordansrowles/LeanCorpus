using System.Text.Json;

namespace Rowles.LeanCorpus.Server.Abstractions.Tests;

public sealed class ContractArtefactTests
{
    [Fact]
    public void OpenApiContractIsVersionedAndContainsThePublicEndpoints()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Contracts", "OpenApi", "lean-corpus-server.v1.openapi.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));

        Assert.Equal("3.1.0", document.RootElement.GetProperty("openapi").GetString());
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/v1/indices/{name}/search", out _));
        Assert.True(document.RootElement.GetProperty("paths").TryGetProperty("/v1/admin/snapshots/{id}:restore", out _));
    }

    [Fact]
    public void ProtobufContractDeclaresTheCustomerServices()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Contracts", "Grpc", "lean-corpus-server.v1.proto");
        string contract = File.ReadAllText(path);

        Assert.Contains("service SearchService", contract, StringComparison.Ordinal);
        Assert.Contains("service IndexService", contract, StringComparison.Ordinal);
        Assert.Contains("service AdminService", contract, StringComparison.Ordinal);
    }
}
