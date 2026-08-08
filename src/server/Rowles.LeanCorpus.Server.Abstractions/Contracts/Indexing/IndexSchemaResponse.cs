namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Returns an index schema and its stable hash.</summary>
/// <param name="IndexName">Customer-visible index name.</param>
/// <param name="Schema">Immutable schema.</param>
/// <param name="SchemaHash">Stable schema hash.</param>
/// <param name="Settings">Current mutable settings.</param>
public sealed record IndexSchemaResponse(
    string IndexName,
    IndexSchema Schema,
    string SchemaHash,
    MutableIndexSettings Settings);
