namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Describes one shard placement.</summary>
public sealed record ShardPlacement(string ShardId, string NodeId, ShardState State, bool IsPrimary);
