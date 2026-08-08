namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Identifies how scores were calculated across shards.</summary>
public enum ScoringModel
{
    /// <summary>Scores are calculated per shard.</summary>
    ShardLocal,
    /// <summary>Scores use global statistics.</summary>
    Global
}
