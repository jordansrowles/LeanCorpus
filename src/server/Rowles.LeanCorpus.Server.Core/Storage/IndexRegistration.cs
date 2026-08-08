using Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

namespace Rowles.LeanCorpus.Server.Core.Storage;

/// <summary>Persists the stable name-to-directory mapping for an index.</summary>
public sealed record IndexRegistration(
    string Name,
    string Id,
    DateTimeOffset CreatedUtc,
    IndexSchema Schema,
    IndexTopologySettings Topology,
    MutableIndexSettings Settings,
    string SchemaHash);
