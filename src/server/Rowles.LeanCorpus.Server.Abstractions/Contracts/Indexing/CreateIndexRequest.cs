namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Requests explicit creation of a logical index.</summary>
/// <param name="IndexName">Customer-visible index name.</param>
/// <param name="Schema">Immutable schema and analysis definition.</param>
/// <param name="Topology">Immutable topology settings.</param>
/// <param name="Settings">Initial mutable settings.</param>
public sealed record CreateIndexRequest(
    string IndexName,
    IndexSchema Schema,
    IndexTopologySettings Topology,
    MutableIndexSettings Settings);
