namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Represents a completed facet and its completeness.</summary>
public sealed record FacetResult(string Name, FacetCompleteness Completeness, IReadOnlyList<FacetBucket> Buckets);
