namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Identifies the externally visible state of a shard copy.</summary>
public enum ShardState
{
    /// <summary>The shard has no placement.</summary>
    Unassigned,
    /// <summary>The shard is being initialised.</summary>
    Initialising,
    /// <summary>The shard is recovering data.</summary>
    Recovering,
    /// <summary>The shard can serve requests.</summary>
    Active,
    /// <summary>The shard has failed.</summary>
    Failed
}
