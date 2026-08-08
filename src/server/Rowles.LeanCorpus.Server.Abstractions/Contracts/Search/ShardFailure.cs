using Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Describes a shard failure returned with a partial result.</summary>
public sealed record ShardFailure(string ShardId, ApiFailure Failure);
