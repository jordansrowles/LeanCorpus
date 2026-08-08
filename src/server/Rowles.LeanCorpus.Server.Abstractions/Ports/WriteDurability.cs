namespace Rowles.LeanCorpus.Server.Abstractions.Ports;

/// <summary>Describes the durability reached by an acknowledged write.</summary>
public enum WriteDurability
{
    /// <summary>The write is retained in memory only.</summary>
    Memory,
    /// <summary>The local data was flushed durably.</summary>
    LocalFsync,
    /// <summary>A quorum confirmed the write.</summary>
    Quorum,
    /// <summary>Required replicas confirmed the write.</summary>
    Replicated
}
