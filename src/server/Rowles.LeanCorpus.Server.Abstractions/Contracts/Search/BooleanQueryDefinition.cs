namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents boolean query clauses.</summary>
public sealed record BooleanQueryDefinition(
    IReadOnlyList<QueryDefinition>? Must = null,
    IReadOnlyList<QueryDefinition>? Should = null,
    IReadOnlyList<QueryDefinition>? MustNot = null,
    int? MinimumShouldMatch = null) : QueryDefinition;
