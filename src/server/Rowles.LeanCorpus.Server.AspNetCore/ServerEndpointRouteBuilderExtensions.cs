using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Core.Services;

namespace Rowles.LeanCorpus.Server.AspNetCore;

/// <summary>Maps the Community REST endpoints implemented by the local server.</summary>
public static class ServerEndpointRouteBuilderExtensions
{
    /// <summary>Maps the available version-one Community endpoints.</summary>
    public static IEndpointRouteBuilder MapLeanCorpusServerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/health", async (LocalServerCore server, CancellationToken cancellationToken) => (await server.GetHealthAsync(cancellationToken)).ToHttpResult());
        endpoints.MapGet("/v1/ready", async (LocalServerCore server, CancellationToken cancellationToken) => (await server.GetReadinessAsync(cancellationToken)).ToHttpResult());
        endpoints.MapGet("/v1/indices", async (LocalServerCore server, CancellationToken cancellationToken) => (await server.ListAsync(cancellationToken)).ToHttpResult());
        endpoints.MapPut("/v1/indices/{name}", async (string name, CreateIndexRequest request, LocalServerCore server, CancellationToken cancellationToken) => (await server.CreateAsync(request with { IndexName = name }, cancellationToken)).ToHttpResult());
        endpoints.MapDelete("/v1/indices/{name}", async (string name, DeleteIndexRequest request, LocalServerCore server, CancellationToken cancellationToken) => (await server.DeleteAsync(request with { IndexName = name }, cancellationToken)).ToHttpResult());
        endpoints.MapPost("/v1/indices/{name}/documents:bulk", async (string name, BulkDocumentsRequest request, LocalServerCore server, CancellationToken cancellationToken) => (await server.BulkAsync(request with { IndexName = name }, cancellationToken)).ToHttpResult());
        endpoints.MapPost("/v1/indices/{name}/refresh", async (string name, LocalServerCore server, CancellationToken cancellationToken) => (await server.RefreshAsync(new RefreshIndexRequest(name), cancellationToken)).ToHttpResult());
        endpoints.MapPost("/v1/indices/{name}/search", async (string name, SearchRequest request, LocalServerCore server, CancellationToken cancellationToken) => (await server.SearchAsync(name, request, cancellationToken)).ToHttpResult());
        endpoints.MapGet("/v1/indices/{name}/schema", async (string name, LocalServerCore server, CancellationToken cancellationToken) => (await server.GetSchemaAsync(name, cancellationToken)).ToHttpResult());
        endpoints.MapPatch("/v1/indices/{name}/settings", async (string name, UpdateIndexSettingsRequest request, LocalServerCore server, CancellationToken cancellationToken) => (await server.UpdateSettingsAsync(request with { IndexName = name }, cancellationToken)).ToHttpResult());
        endpoints.MapGet("/v1/indices/{name}/stats", async (string name, LocalServerCore server, CancellationToken cancellationToken) => (await server.GetStatisticsAsync(name, cancellationToken)).ToHttpResult());
        return endpoints;
    }
}
