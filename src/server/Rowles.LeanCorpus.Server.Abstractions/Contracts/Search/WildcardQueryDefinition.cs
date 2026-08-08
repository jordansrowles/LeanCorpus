namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents a wildcard query.</summary>
public sealed record WildcardQueryDefinition(string Field, string Pattern) : QueryDefinition;
