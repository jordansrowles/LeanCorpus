namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents a regular-expression query.</summary>
public sealed record RegexpQueryDefinition(string Field, string Pattern) : QueryDefinition;
