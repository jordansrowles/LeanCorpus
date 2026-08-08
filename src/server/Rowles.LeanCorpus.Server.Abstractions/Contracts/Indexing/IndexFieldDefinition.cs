namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Defines one immutable index field.</summary>
/// <param name="Name">Customer-visible field name.</param>
/// <param name="Type">Field type.</param>
/// <param name="Indexed">Whether the field participates in search.</param>
/// <param name="Stored">Whether the field is returned as stored content.</param>
/// <param name="MultiValued">Whether the field accepts multiple values.</param>
/// <param name="Analyser">Optional named analysis pipeline for text fields.</param>
/// <param name="VectorDimensions">Required dimension count for vector fields.</param>
public sealed record IndexFieldDefinition(
    string Name,
    IndexFieldType Type,
    bool Indexed,
    bool Stored,
    bool MultiValued = false,
    string? Analyser = null,
    int? VectorDimensions = null);
