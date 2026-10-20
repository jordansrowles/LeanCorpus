namespace Rowles.LeanCorpus.Index;

internal enum PhysicalIndexRuntimeState
{
    Opening,
    Open,
    Quiescing,
    Closing,
    Closed,
    Faulted,
}

internal sealed record PhysicalIndexRuntimeDiagnostics(
    Guid RuntimeId,
    PhysicalIndexRuntimeState State,
    int ActiveOperations,
    int ActiveSearcherLeases,
    bool WriterOwned,
    bool DirectoryOwned,
    Exception? LastCloseFailure,
    DateTimeOffset? CloseStarted,
    TimeSpan? CloseDuration,
    PhysicalIndexRuntimeHealth Health,
    bool WritesAllowed,
    bool SearchesAllowed,
    bool ReopenRequired,
    PhysicalIndexRuntimeFailure? LastFailure,
    int CurrentGeneration,
    int RetiredGenerations,
    bool WriterLockOwned,
    bool PendingClose,
    int PendingDeferredDeletes,
    int DirectoryFileLeases,
    int ActiveInputs,
    int ActiveMappedViews,
    int ActiveMerges,
    PhysicalIndexWriterState WriterState);

internal enum PhysicalIndexRuntimeHealth
{
    Healthy,
    Degraded,
    Faulted,
}

internal sealed record PhysicalIndexRuntimeFailure(string Component, Exception Exception, DateTimeOffset OccurredAt);

internal enum PhysicalIndexWriterState
{
    ReadOnly,
    Open,
    Closing,
    Closed,
}
