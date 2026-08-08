namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Reports the accepted recovery operation.</summary>
public sealed record RecoverShardResponse(string ShardId, string State);
