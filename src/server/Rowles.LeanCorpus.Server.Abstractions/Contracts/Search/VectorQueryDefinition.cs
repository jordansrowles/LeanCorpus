namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents an approximate nearest-neighbour query with an optional lexical filter.</summary>
public sealed record VectorQueryDefinition(string Field, IReadOnlyList<float> Vector, int CandidateCount, QueryDefinition? Filter = null) : QueryDefinition;
