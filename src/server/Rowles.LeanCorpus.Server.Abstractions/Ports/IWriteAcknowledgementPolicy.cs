namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Defines when a completed write may be acknowledged.</summary>
public interface IWriteAcknowledgementPolicy
{
    /// <summary>Waits for the configured acknowledgement condition.</summary>
    ValueTask<WriteAcknowledgement> AcknowledgeAsync(WriteCommitState state, CancellationToken cancellationToken = default);
}
