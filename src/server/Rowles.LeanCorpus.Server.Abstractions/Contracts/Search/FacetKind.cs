namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Identifies a facet aggregation shape.</summary>
public enum FacetKind
{
    /// <summary>Groups exact values.</summary>
    Terms,
    /// <summary>Groups numeric ranges.</summary>
    Range
}
