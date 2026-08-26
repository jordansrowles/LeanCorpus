namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Current observable state of one local commit coordinator.</summary>
public sealed record LocalCommitState(
    long PendingOperations,
    LocalCommitReceipt? LastReceipt,
    string? LastFailure,
    int ConsecutiveFailures);
