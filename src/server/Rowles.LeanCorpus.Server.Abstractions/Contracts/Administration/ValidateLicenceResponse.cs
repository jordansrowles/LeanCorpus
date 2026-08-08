namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Reports whether a supplied licence envelope is valid.</summary>
public sealed record ValidateLicenceResponse(bool IsValid, string? Reason = null);
