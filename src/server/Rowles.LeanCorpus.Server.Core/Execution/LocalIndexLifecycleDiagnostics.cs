using Rowles.LeanCorpus.Index;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Correlates Server ownership and sequencing with its physical runtime.</summary>
internal sealed record LocalIndexLifecycleDiagnostics(
    string PhysicalIndexId,
    Guid RuntimeId,
    LocalIndexOpenMode Mode,
    long ServerSequence,
    int ActiveSnapshotLeases,
    PhysicalIndexRuntimeDiagnostics Physical);
