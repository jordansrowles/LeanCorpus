namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;

/// <summary>Requests a bounded, read-only inspection operation.</summary>
public sealed record InspectionRequest(InspectionResource Resource, int Limit = 100, IReadOnlyDictionary<string, string>? Arguments = null);
