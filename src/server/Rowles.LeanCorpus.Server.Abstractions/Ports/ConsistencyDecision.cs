using Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Contains the effective consistency selected for an operation.</summary>
public sealed record ConsistencyDecision(bool IsAllowed, RequestedConsistency EffectiveConsistency, string? Reason = null);
