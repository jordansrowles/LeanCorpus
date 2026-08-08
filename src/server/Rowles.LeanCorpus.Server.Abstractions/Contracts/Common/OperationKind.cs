namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Common;

/// <summary>Identifies an externally reachable server operation.</summary>
public enum OperationKind
{
    /// <summary>Read process health.</summary>
    ReadHealth,
    /// <summary>Read process readiness.</summary>
    ReadReadiness,
    /// <summary>List indexes.</summary>
    ListIndexes,
    /// <summary>Create an index.</summary>
    CreateIndex,
    /// <summary>Delete an index.</summary>
    DeleteIndex,
    /// <summary>Update mutable index settings.</summary>
    UpdateIndexSettings,
    /// <summary>Index or delete documents.</summary>
    WriteDocuments,
    /// <summary>Refresh an index.</summary>
    RefreshIndex,
    /// <summary>Search an index.</summary>
    Search,
    /// <summary>Read index metadata.</summary>
    ReadIndexMetadata,
    /// <summary>Read a bounded inspection view.</summary>
    Inspect,
    /// <summary>Read cluster state.</summary>
    ReadCluster,
    /// <summary>Read shard placement.</summary>
    ReadShards,
    /// <summary>Drain a node.</summary>
    DrainNode,
    /// <summary>Recover a shard.</summary>
    RecoverShard,
    /// <summary>Read or validate a licence.</summary>
    ManageLicence,
    /// <summary>Create or restore a snapshot.</summary>
    ManageSnapshot,
    /// <summary>Read bounded diagnostics.</summary>
    ReadDiagnostics,
}
