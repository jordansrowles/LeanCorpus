namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Indexing;

/// <summary>Defines immutable analysis components used by an index.</summary>
/// <param name="Tokeniser">Named tokeniser.</param>
/// <param name="CharacterFilters">Ordered character filters.</param>
/// <param name="TokenFilters">Ordered token filters.</param>
public sealed record AnalysisDefinition(
    string Tokeniser,
    IReadOnlyList<string> CharacterFilters,
    IReadOnlyList<string> TokenFilters);
