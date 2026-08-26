namespace Rowles.LeanCorpus.Server.Core.Execution;

internal sealed class NullLocalCommitObserver : ILocalCommitObserver
{
    internal static NullLocalCommitObserver Instance { get; } = new();

    public ValueTask OnCommittedAsync(LocalIndexDescriptor index, LocalCommitReceipt receipt, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
