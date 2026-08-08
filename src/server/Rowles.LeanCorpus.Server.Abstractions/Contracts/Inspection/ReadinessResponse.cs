namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;

/// <summary>Describes whether the service can accept requests.</summary>
public sealed record ReadinessResponse(bool IsReady, string Status, DateTimeOffset ObservedUtc, string? Reason = null);
