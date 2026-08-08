namespace Rowles.LeanCorpus.Server.Abstractions.Contracts.Search;

/// <summary>Describes whether a total-hit value is exact.</summary>
public enum TotalHitsRelation
{
    /// <summary>The total is exact.</summary>
    Exact,
    /// <summary>The total is a lower bound.</summary>
    LowerBound
}
