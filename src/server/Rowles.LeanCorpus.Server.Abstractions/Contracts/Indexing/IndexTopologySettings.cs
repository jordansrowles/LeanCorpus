namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Defines immutable shard and replica settings selected at index creation.</summary>
/// <param name="ShardCount">Requested shard count.</param>
/// <param name="ReplicaCount">Requested replica count per shard.</param>
public sealed record IndexTopologySettings(int ShardCount, int ReplicaCount);
