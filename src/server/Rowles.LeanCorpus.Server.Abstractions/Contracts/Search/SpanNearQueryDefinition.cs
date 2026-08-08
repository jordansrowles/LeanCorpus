namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents ordered or unordered proximity clauses.</summary>
public sealed record SpanNearQueryDefinition(IReadOnlyList<QueryDefinition> Clauses, int Slop, bool InOrder) : QueryDefinition;
