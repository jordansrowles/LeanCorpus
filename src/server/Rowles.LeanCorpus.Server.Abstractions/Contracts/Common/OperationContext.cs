namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Supplies common context to transport-neutral server operations.</summary>
/// <param name="RequestId">Caller supplied or host generated request identifier.</param>
/// <param name="Operation">Operation being executed.</param>
/// <param name="Caller">Authenticated caller identity.</param>
/// <param name="StartedUtc">UTC time at which the host accepted the request.</param>
/// <param name="IndexName">Optional customer-visible index name.</param>
/// <param name="CorrelationId">Optional caller correlation identifier.</param>
/// <param name="IdempotencyKey">Optional write idempotency key.</param>
public sealed record OperationContext(
    string RequestId,
    OperationKind Operation,
    CallerIdentity Caller,
    DateTimeOffset StartedUtc,
    string? IndexName = null,
    string? CorrelationId = null,
    string? IdempotencyKey = null);
