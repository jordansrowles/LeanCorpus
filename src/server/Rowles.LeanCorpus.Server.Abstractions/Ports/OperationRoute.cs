namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Describes the chosen execution destination.</summary>
public sealed record OperationRoute(RouteTargetKind TargetKind, string? TargetId = null, string? RejectionReason = null);
