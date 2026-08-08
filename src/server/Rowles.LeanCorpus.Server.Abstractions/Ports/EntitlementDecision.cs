namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Contains the result of an entitlement decision.</summary>
public sealed record EntitlementDecision(bool IsAllowed, string? Reason = null);
