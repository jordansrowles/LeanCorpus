namespace Rowles.LeanCorpus.OrchardCore.Search.Storage;

internal sealed record OrchardIndexOwnershipDiagnostics(
    Guid CacheId,
    int? ShellId,
    Guid? SharedStateId,
    int ShellReferences,
    IReadOnlyList<Guid> OwnerCacheIds,
    bool AdmissionClosed,
    bool CloseCompleted,
    IReadOnlyList<Guid> RuntimeIds,
    Exception? CloseFailure);
