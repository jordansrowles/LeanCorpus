using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Core.Execution;

/// <summary>Describes a server-owned physical local index without exposing its path.</summary>
public sealed record LocalIndexDescriptor(
    PhysicalIndexId Id,
    IndexSchema Schema,
    string SchemaHash,
    MutableIndexSettings Settings,
    IndexTopologySettings? Topology = null);
