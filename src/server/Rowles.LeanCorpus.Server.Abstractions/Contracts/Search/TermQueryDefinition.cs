namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents an exact term query.</summary>
public sealed record TermQueryDefinition(string Field, string Value) : QueryDefinition;
