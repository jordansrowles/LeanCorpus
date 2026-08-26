namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;

/// <summary>Describes the observable local state of one registered index.</summary>
public sealed record IndexHealthSummary(
    string IndexName,
    string IndexId,
    string Mode,
    long VisibleGeneration,
    long DurableGeneration,
    long PendingOperations,
    DateTimeOffset? LastSuccessfulCommitUtc,
    string? LastCommitError,
    int ConsecutiveCommitFailures,
    int ActiveSnapshotLeases,
    bool IsInstalling,
    bool IsUsable,
    bool IsDegraded,
    string? LastInstallError);
