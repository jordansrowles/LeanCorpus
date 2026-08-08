using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;

/// <summary>Reports the result of one bulk document operation.</summary>
/// <param name="DocumentId">Caller-visible document identifier.</param>
/// <param name="Accepted">Whether the operation was accepted.</param>
/// <param name="Failure">Failure details when the operation was rejected.</param>
public sealed record BulkDocumentResult(string DocumentId, bool Accepted, ApiFailure? Failure = null);
