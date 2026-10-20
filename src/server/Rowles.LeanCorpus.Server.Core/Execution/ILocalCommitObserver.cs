namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Observes locally published commits without owning replication policy.</summary>
public interface ILocalCommitObserver
{
    /// <summary>
    /// Observes a published commit. Notifications may overlap or start and finish
    /// out of receipt order; consumers use <see cref="LocalCommitReceipt.LastSequenceNumber"/>
    /// as the ordering token. Observer failures do not change physical commit
    /// durability or waiter outcomes.
    /// </summary>
    ValueTask OnCommittedAsync(LocalIndexDescriptor index, LocalCommitReceipt receipt, CancellationToken cancellationToken = default);
}
