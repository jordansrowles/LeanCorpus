namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Identifies a feature whose availability may be entitlement-controlled.</summary>
public enum ServerFeature
{
    /// <summary>Local single-node server.</summary>
    LocalServer,
    /// <summary>Distributed server operation.</summary>
    DistributedServer,
    /// <summary>High-availability operation.</summary>
    HighAvailability,
    /// <summary>Shard replication.</summary>
    Replication,
    /// <summary>Remote backup.</summary>
    RemoteBackup,
    /// <summary>Enterprise administration.</summary>
    EnterpriseAdministration,
    /// <summary>Enterprise Studio topology inspection.</summary>
    StudioTopology
}
