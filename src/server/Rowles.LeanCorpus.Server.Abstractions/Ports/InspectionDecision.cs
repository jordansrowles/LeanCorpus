using Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;

namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Contains the inspection bounds allowed for a caller.</summary>
public sealed record InspectionDecision(bool IsAllowed, int MaximumLimit, string? Reason = null);
