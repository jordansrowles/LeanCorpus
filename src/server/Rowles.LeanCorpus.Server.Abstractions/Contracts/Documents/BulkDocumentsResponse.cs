namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;

/// <summary>Returns results for a bulk document request.</summary>
/// <param name="Items">Results in request order.</param>
/// <param name="Acknowledged">Whether the requested acknowledgement policy was met.</param>
/// <param name="CommitGeneration">Committed generation when known.</param>
/// <param name="WriteToken">Versioned local token for a subsequent read-your-writes request.</param>
public sealed record BulkDocumentsResponse(
    IReadOnlyList<BulkDocumentResult> Items,
    bool Acknowledged,
    long? CommitGeneration,
    WriteToken? WriteToken = null);
