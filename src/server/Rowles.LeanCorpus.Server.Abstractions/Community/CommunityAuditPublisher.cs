using Rowles.LeanCorpus.Server.Abstractions.Ports;

namespace Rowles.LeanCorpus.Server.Abstractions.Community;

/// <summary>Discards audit events until a host supplies an audit publisher.</summary>
public sealed class CommunityAuditPublisher : IAuditPublisher
{
    /// <inheritdoc />
    public ValueTask PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}
