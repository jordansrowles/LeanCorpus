using System.Text.Json;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;

/// <summary>Contains one document operation without depending on engine document types.</summary>
/// <param name="Kind">Requested operation.</param>
/// <param name="DocumentId">Caller-visible document identifier.</param>
/// <param name="Document">JSON document payload for index and update operations.</param>
/// <param name="IdempotencyKey">Optional per-operation idempotency key.</param>
public sealed record BulkDocumentOperation(
    DocumentOperationKind Kind,
    string DocumentId,
    JsonElement? Document = null,
    string? IdempotencyKey = null);
