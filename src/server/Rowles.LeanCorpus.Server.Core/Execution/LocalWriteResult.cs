using Rowles.LeanCorpus.Server.Abstractions.Contracts.Documents;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Result of applying a write to one already-selected local physical index.</summary>
public sealed record LocalWriteResult(
    IReadOnlyList<BulkDocumentResult> Items,
    int AcceptedOperations,
    bool Committed,
    LocalCommitReceipt? Receipt,
    long SequenceNumber,
    long VisibleGeneration);
