namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Summarises the shard participation in a search.</summary>
public sealed record ShardSearchSummary(int Total, int Successful, int Failed, int Skipped);
