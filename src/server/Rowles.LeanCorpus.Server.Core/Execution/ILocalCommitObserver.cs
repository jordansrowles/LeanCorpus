namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Observes locally published commits without owning replication policy.</summary>
public interface ILocalCommitObserver
{
    /// <summary>Observes a published commit.</summary>
    ValueTask OnCommittedAsync(LocalIndexDescriptor index, LocalCommitReceipt receipt, CancellationToken cancellationToken = default);
}
