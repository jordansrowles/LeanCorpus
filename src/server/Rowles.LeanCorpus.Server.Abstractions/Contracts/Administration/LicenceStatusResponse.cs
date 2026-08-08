namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Contains non-sensitive entitlement status.</summary>
public sealed record LicenceStatusResponse(bool IsValid, DateTimeOffset? ExpiresUtc, IReadOnlyList<string> Features);
