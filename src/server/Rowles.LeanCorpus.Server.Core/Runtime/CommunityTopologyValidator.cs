using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Applies the single-node Community topology policy.</summary>
public static class CommunityTopologyValidator
{
    /// <summary>Validates the topology permitted by the single-node Community host.</summary>
    public static void Validate(IndexTopologySettings topology)
    {
        ArgumentNullException.ThrowIfNull(topology);
        if (topology.ShardCount != 1 || topology.ReplicaCount != 0)
            throw new InvalidOperationException("Community Server requires exactly one shard and zero replicas.");
    }
}
