namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Contains the externally visible cluster state.</summary>
public sealed record ClusterInfoResponse(string ClusterId, string? LeaderNodeId, IReadOnlyList<ClusterNodeSummary> Nodes, long MetadataEpoch, ClusterHealthStatus Health);
