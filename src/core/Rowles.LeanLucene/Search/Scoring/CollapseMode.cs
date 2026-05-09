namespace Rowles.LeanLucene.Search.Scoring;

/// <summary>How to select the representative document per group.</summary>
public enum CollapseMode
{
    /// <summary>Keep the highest-scoring document per group.</summary>
    TopScore,

    /// <summary>Keep the lowest-scoring document per group (useful for ascending sort).</summary>
    MinScore
}
