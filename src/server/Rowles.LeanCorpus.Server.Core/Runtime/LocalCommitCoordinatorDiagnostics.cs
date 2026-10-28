using Rowles.LeanCorpus.Server.Core.Execution;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Separates durable receipt state from coordinator and observer failures.</summary>
internal sealed record LocalCommitCoordinatorDiagnostics(
    long? LatestDurableSequence,
    long? CommitGeneration,
    long? ContentToken,
    bool? IsVisible,
    int Waiters,
    bool ActiveCommitAttempt,
    bool Closing,
    Exception? LastPhysicalFailure,
    int ConsecutivePhysicalFailures,
    Exception? LastObserverFailure);
