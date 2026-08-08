using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.AspNetCore;

/// <summary>Converts transport-neutral service results into HTTP responses.</summary>
internal static class ServiceResultHttpExtensions
{
    internal static IResult ToHttpResult<T>(this ServiceResult<T> result) =>
        result.IsSuccess ? Results.Ok(result) : Results.Json(result, statusCode: StatusCode(result.Failure!.Code));

    private static int StatusCode(string code) => code switch
    {
        "index_not_found" => StatusCodes.Status404NotFound,
        "index_exists" => StatusCodes.Status409Conflict,
        "forbidden" => StatusCodes.Status403Forbidden,
        "route_unavailable" or "consistency_unavailable" => StatusCodes.Status503ServiceUnavailable,
        "invalid_index_name" or "invalid_schema" or "invalid_document" or "invalid_document_id" or "invalid_bulk_request" or "unsupported_query" or "unsupported_search" => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };
}
