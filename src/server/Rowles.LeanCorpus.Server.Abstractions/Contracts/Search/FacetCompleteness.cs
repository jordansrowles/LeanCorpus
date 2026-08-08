namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Describes whether a facet was calculated from all requested shards.</summary>
public enum FacetCompleteness
{
    /// <summary>All requested shards contributed.</summary>
    Complete,
    /// <summary>Only some requested shards contributed.</summary>
    Partial,
    /// <summary>The facet could not be calculated.</summary>
    Unavailable
}
