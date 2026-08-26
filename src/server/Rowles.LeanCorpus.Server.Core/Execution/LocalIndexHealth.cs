namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Reports the local health of one physical index runtime.</summary>
public sealed record LocalIndexHealth(
    LocalIndexOpenMode Mode,
    long VisibleGeneration,
    long DurableGeneration,
    long PendingOperations,
    DateTimeOffset? LastSuccessfulCommitUtc,
    string? LastCommitError,
    int ConsecutiveCommitFailures,
    int ActiveSnapshotLeases,
    bool IsInstalling);
