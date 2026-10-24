namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Summarises store ownership without exposing filesystem paths.</summary>
internal sealed record LocalIndexStoreDiagnostics(
    bool Closing,
    IReadOnlyList<LocalIndexStoreEntryDiagnostics> Entries,
    bool? LastCreateRollbackSucceeded,
    Exception? LastCreateRollbackFailure,
    IReadOnlyList<string> RetainedFailedCreateIds);

internal sealed record LocalIndexStoreEntryDiagnostics(
    string PhysicalIndexId,
    Guid RuntimeId,
    LocalIndexHandleLifecycleState State,
    long ReplacementGeneration);
