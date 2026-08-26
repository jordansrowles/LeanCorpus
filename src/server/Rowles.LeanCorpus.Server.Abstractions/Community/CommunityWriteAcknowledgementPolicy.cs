using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.Abstractions.Community;

/// <summary>Uses local fsync as the Community write acknowledgement point.</summary>
public sealed class CommunityWriteAcknowledgementPolicy : IWriteAcknowledgementPolicy
{
    /// <inheritdoc />
    public ValueTask<WriteAcknowledgement> AcknowledgeAsync(WriteCommitState state, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new WriteAcknowledgement(true, state.IsDurable ? WriteDurability.LocalFsync : WriteDurability.Memory));
}
