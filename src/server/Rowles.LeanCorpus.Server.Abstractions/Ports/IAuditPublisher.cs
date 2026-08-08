namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Publishes audit events.</summary>
public interface IAuditPublisher
{
    /// <summary>Publishes an audit event.</summary>
    ValueTask PublishAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}
