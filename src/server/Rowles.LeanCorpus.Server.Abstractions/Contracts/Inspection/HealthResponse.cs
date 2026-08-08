namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Inspection;

/// <summary>Describes process health without exposing internal implementation details.</summary>
public sealed record HealthResponse(bool IsHealthy, string Status, DateTimeOffset ObservedUtc);
