using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;
using Rowles.LeanCorpus.Server.Abstractions.Services;

namespace Rowles.LeanCorpus.Server.AspNetCore;

/// <summary>Maps the Community REST endpoints implemented by the local server.</summary>
public static class ServerEndpointRouteBuilderExtensions
{
    /// <summary>Maps the available version-one Community endpoints.</summary>
    public static IEndpointRouteBuilder MapLeanCorpusServerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/health", async ([FromServices] IHealthService service, CancellationToken cancellationToken) =>
            (await service.GetHealthAsync(cancellationToken)).ToHttpResult());
        endpoints.MapGet("/v1/ready", async ([FromServices] IHealthService service, CancellationToken cancellationToken) =>
            (await service.GetReadinessAsync(cancellationToken)).ToHttpResult());
        endpoints.MapGet("/v1/indices", async ([FromServices] IIndexService service, CancellationToken cancellationToken) =>
            (await service.ListAsync(cancellationToken)).ToHttpResult());
        endpoints.MapPut("/v1/indices/{name}", async (string name, [FromBody] CreateIndexRequest request, [FromServices] IIndexService service, CancellationToken cancellationToken) =>
            (await service.CreateAsync(request with { IndexName = name }, cancellationToken)).ToHttpResult());
        endpoints.MapDelete("/v1/indices/{name}", async (HttpContext httpContext, string name, [FromBody] DeleteIndexRequest? request, [FromServices] IIndexService service, CancellationToken cancellationToken) =>
        {
            string token = request?.ConfirmationToken
                ?? httpContext.Request.Headers["X-LeanCorpus-Confirm"].FirstOrDefault()
                ?? string.Empty;
            return (await service.DeleteAsync(new DeleteIndexRequest(name, token), cancellationToken)).ToHttpResult();
        });
        endpoints.MapPost("/v1/indices/{name}/documents:bulk", async (string name, [FromBody] BulkDocumentsRequest request, [FromServices] IDocumentService service, CancellationToken cancellationToken) =>
            (await service.BulkAsync(request with { IndexName = name }, cancellationToken)).ToHttpResult());
        endpoints.MapPost("/v1/indices/{name}/refresh", async (string name, [FromServices] IIndexService service, CancellationToken cancellationToken) =>
            (await service.RefreshAsync(new RefreshIndexRequest(name), cancellationToken)).ToHttpResult());
        endpoints.MapPost("/v1/indices/{name}/search", async (string name, [FromBody] SearchRequest request, [FromServices] ISearchService service, CancellationToken cancellationToken) =>
            (await service.SearchAsync(name, request, cancellationToken)).ToHttpResult());
        endpoints.MapPost("/v1/indices/{name}/explain", async (string name, [FromBody] ExplainRequest request, [FromServices] ISearchService service, CancellationToken cancellationToken) =>
            (await service.ExplainAsync(name, request, cancellationToken)).ToHttpResult());
        endpoints.MapGet("/v1/indices/{name}/schema", async (string name, [FromServices] IIndexService service, CancellationToken cancellationToken) =>
            (await service.GetSchemaAsync(name, cancellationToken)).ToHttpResult());
        endpoints.MapPatch("/v1/indices/{name}/settings", async (HttpContext httpContext, string name, [FromBody] UpdateIndexSettingsRequest request, [FromServices] IIndexService service, CancellationToken cancellationToken) =>
        {
            string? confirmation = request.ConfirmationToken
                ?? httpContext.Request.Headers["X-LeanCorpus-Confirm"].FirstOrDefault();
            return (await service.UpdateSettingsAsync(request with { IndexName = name, ConfirmationToken = confirmation }, cancellationToken)).ToHttpResult();
        });
        endpoints.MapGet("/v1/indices/{name}/stats", async (string name, [FromServices] IIndexService service, CancellationToken cancellationToken) =>
            (await service.GetStatisticsAsync(name, cancellationToken)).ToHttpResult());
        endpoints.MapGet("/v1/indices/{name}/inspection/{resource}", async (HttpContext httpContext, string name, string resource, [FromServices] IInspectionService service, CancellationToken cancellationToken) =>
        {
            if (!Enum.TryParse(resource, ignoreCase: true, out InspectionResource parsed))
                return ServiceResultHttpExtensions.ToHttpFailure("invalid_inspection_resource", "The inspection resource is not recognised.");
            int limit = 100;
            if (httpContext.Request.Query.TryGetValue("limit", out var values) && (!int.TryParse(values.FirstOrDefault(), out limit) || limit < 1))
                return ServiceResultHttpExtensions.ToHttpFailure("invalid_inspection_limit", "The inspection limit must be a positive integer.");
            return (await service.InspectAsync(name, new InspectionRequest(parsed, limit), cancellationToken)).ToHttpResult();
        });
        return endpoints;
    }
}
