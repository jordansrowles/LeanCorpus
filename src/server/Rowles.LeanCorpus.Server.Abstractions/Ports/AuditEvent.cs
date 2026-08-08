using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Describes an auditable server operation without retaining request payloads.</summary>
public sealed record AuditEvent(OperationContext Context, bool IsSuccessful, string? FailureCode = null);
