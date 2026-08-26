using Microsoft.AspNetCore.Http;
using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.AspNetCore;

/// <summary>Converts transport-neutral service results into HTTP responses.</summary>
internal static class ServiceResultHttpExtensions
{
    internal static IResult ToHttpResult<T>(this ServiceResult<T> result) =>
        new ServiceResultHttpResult<T>(result, result.IsSuccess ? StatusCodes.Status200OK : StatusCode(result.Failure!.Code));

    internal static IResult ToHttpFailure(string code, string message)
    {
        ResponseMetadata metadata = new(Guid.NewGuid().ToString("N"), ServerApiVersions.V1, DateTimeOffset.UtcNow);
        ServiceResult<object> result = ServiceResult<object>.Failed(metadata, new ApiFailure(code, message));
        return new ServiceResultHttpResult<object>(result, StatusCode(code));
    }

    private static int StatusCode(string code) => code switch
    {
        "index_not_found" => StatusCodes.Status404NotFound,
        "index_exists" => StatusCodes.Status409Conflict,
        "idempotency_conflict" => StatusCodes.Status409Conflict,
        "unauthenticated" => StatusCodes.Status401Unauthorized,
        "forbidden" => StatusCodes.Status403Forbidden,
        "route_unavailable" or "consistency_unavailable" or "consistency_wait_timeout" or "consistency_wait_cancelled" or "server_stopping" => StatusCodes.Status503ServiceUnavailable,
        "document_too_large" => StatusCodes.Status413PayloadTooLarge,
        "unsupported_query" or "unsupported_search" or "unsupported_facet" or "highlights_not_supported" or "explain_not_supported" or "inspection_not_supported" or "durability_not_supported" => StatusCodes.Status422UnprocessableEntity,
        "invalid_index_name" or "invalid_schema" or "invalid_topology" or "invalid_settings" or "invalid_document" or "invalid_document_id" or "invalid_bulk_request" or "invalid_operation" or "invalid_durability" or "unknown_field" or "multi_value_not_allowed" or "schema_validation" or "invalid_search_request" or "invalid_query" or "invalid_query_field" or "invalid_vector" or "query_too_complex" or "invalid_sort" or "invalid_facet_field" or "invalid_search_after" or "invalid_inspection_limit" or "invalid_inspection_resource" or "confirmation_required" or "write_token_required" or "invalid_write_token" or "invalid_explain_request" => StatusCodes.Status400BadRequest,
        "inspection_denied" or "feature_unavailable" => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status500InternalServerError
    };

    private sealed class ServiceResultHttpResult<T>(ServiceResult<T> result, int statusCode) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.Headers["X-Request-ID"] = result.Metadata.RequestId;
            httpContext.Response.Headers["X-API-Version"] = result.Metadata.ApiVersion;
            await httpContext.Response.WriteAsJsonAsync(result, httpContext.RequestAborted).ConfigureAwait(false);
        }
    }
}
