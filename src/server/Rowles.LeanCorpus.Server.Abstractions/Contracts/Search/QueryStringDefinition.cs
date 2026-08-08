namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents a query parsed using the index query-string rules.</summary>
public sealed record QueryStringDefinition(string Text, string? DefaultField = null) : QueryDefinition;
