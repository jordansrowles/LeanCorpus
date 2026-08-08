namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Contains search results and distributed execution metadata.</summary>
public sealed record SearchResponse(
    IReadOnlyList<SearchHit> Hits,
    long TotalHits,
    TotalHitsRelation TotalHitsRelation,
    ScoringModel ScoringModel,
    ShardSearchSummary Shards,
    SearchTiming Timing,
    IReadOnlyList<object?>? NextSearchAfter = null,
    IReadOnlyList<FacetResult>? Facets = null,
    IReadOnlyList<ShardFailure>? Failures = null,
    bool IsPartial = false);
