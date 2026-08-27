namespace Rowles.LeanCorpus.Search.Scoring;

/// <summary>Specifies the ordering applied to returned facet buckets.</summary>
public enum FacetBucketOrder
{
    /// <summary>Orders buckets by count descending, then value ascending.</summary>
    CountDescending,

    /// <summary>Orders buckets by count ascending, then value ascending.</summary>
    CountAscending,

    /// <summary>Orders buckets by value ascending, then count descending.</summary>
    ValueAscending,

    /// <summary>Orders buckets by value descending, then count descending.</summary>
    ValueDescending
}
