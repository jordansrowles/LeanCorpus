namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Identifies the local commit that durably contains accepted writes.</summary>
public sealed record LocalCommitReceipt(
    long FirstSequenceNumber,
    long LastSequenceNumber,
    long CommitGeneration,
    long ContentToken,
    bool IsDurable,
    bool IsVisible);
