namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;

/// <summary>Requests a bounded batch of document operations.</summary>
/// <param name="IndexName">Customer-visible index name.</param>
/// <param name="Operations">Operations in caller order.</param>
/// <param name="Refresh">Whether the caller requests visibility after acceptance.</param>
/// <param name="IdempotencyKey">Optional request-level idempotency key.</param>
/// <param name="Durability">Durability required before acknowledgement.</param>
public sealed record BulkDocumentsRequest(
    string IndexName,
    IReadOnlyList<BulkDocumentOperation> Operations,
    bool Refresh = false,
    string? IdempotencyKey = null,
    RequestedWriteDurability Durability = RequestedWriteDurability.Memory);
