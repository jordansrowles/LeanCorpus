namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Reports the current state of a node drain.</summary>
public sealed record DrainNodeResponse(string NodeId, string State, int RemainingShardCount);
