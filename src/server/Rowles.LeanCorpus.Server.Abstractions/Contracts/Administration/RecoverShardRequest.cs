namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Requests recovery of one shard.</summary>
public sealed record RecoverShardRequest(string ShardId, string? TargetNodeId = null);
