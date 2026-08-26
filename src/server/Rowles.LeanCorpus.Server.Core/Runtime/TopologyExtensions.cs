using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Core.Runtime;

/// <summary>Provides topology behaviour without extending transport DTOs.</summary>
public static class TopologyExtensions
{
    /// <summary>Determines whether the topology has structurally valid counts.</summary>
    public static bool IsStructurallyValid(this IndexTopologySettings topology) =>
        topology.ShardCount > 0 && topology.ReplicaCount >= 0;

    /// <summary>Determines whether the topology describes one unreplicated Community copy.</summary>
    public static bool IsSingleNode(this IndexTopologySettings topology) =>
        topology.ShardCount == 1 && topology.ReplicaCount == 0;

    /// <summary>Gets the number of copies implied by the replica count.</summary>
    public static int CopyCount(this IndexTopologySettings topology) =>
        checked(topology.ReplicaCount + 1);
}
