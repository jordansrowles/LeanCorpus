namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents a prefix query.</summary>
public sealed record PrefixQueryDefinition(string Field, string Prefix) : QueryDefinition;
