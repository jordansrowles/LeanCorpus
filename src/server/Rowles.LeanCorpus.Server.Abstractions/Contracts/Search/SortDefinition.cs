namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents one requested result sort.</summary>
public sealed record SortDefinition(string Field, SortDirection Direction = SortDirection.Descending);
