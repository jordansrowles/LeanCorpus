namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Contains the result of an authorisation decision.</summary>
public sealed record AuthorisationDecision(bool IsAllowed, string? Reason = null);
