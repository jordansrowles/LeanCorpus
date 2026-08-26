namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Coordinates explicit, periodic and sequence-based local commit work.</summary>
public interface ILocalCommitCoordinator
{
    /// <summary>Gets the current coordinator state.</summary>
    LocalCommitState State { get; }

    /// <summary>Commits pending operations as one publication.</summary>
    CommitResult Commit(bool refresh = false);

    /// <summary>Asynchronously requests one local commit publication.</summary>
    ValueTask<CommitResult> CommitAsync(bool refresh = false, CancellationToken cancellationToken = default);

    /// <summary>Waits until a committed receipt covers a sequence number.</summary>
    ValueTask<LocalCommitReceipt> WaitUntilCommittedAsync(long sequenceNumber, CancellationToken cancellationToken = default);
}
