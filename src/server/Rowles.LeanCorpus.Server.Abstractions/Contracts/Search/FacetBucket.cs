namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents one facet bucket.</summary>
public sealed record FacetBucket(string Key, long Count);
