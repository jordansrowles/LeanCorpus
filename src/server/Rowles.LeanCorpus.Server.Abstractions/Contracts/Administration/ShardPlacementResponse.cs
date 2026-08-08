namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Contains the placement of an index's shards.</summary>
public sealed record ShardPlacementResponse(string IndexName, IReadOnlyList<ShardPlacement> Shards);
