namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Administration;

/// <summary>Describes the externally visible cluster health state.</summary>
public enum ClusterHealthStatus
{
    /// <summary>All required shards are available.</summary>
    Green,
    /// <summary>Service continues with reduced resilience.</summary>
    Yellow,
    /// <summary>One or more required shards are unavailable.</summary>
    Red
}
