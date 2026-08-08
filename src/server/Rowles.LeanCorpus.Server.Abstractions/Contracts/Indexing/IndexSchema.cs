namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Defines an immutable index schema.</summary>
/// <param name="Fields">Field definitions.</param>
/// <param name="Analysis">Named analysis pipelines.</param>
public sealed record IndexSchema(
    IReadOnlyList<IndexFieldDefinition> Fields,
    IReadOnlyDictionary<string, AnalysisDefinition> Analysis);
