namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Describes a cluster node without identity or secret material.</summary>
public sealed record ClusterNodeSummary(string NodeId, string State, IReadOnlyList<string> Roles);
