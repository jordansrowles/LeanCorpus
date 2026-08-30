namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>Supported aggregation types.</summary>
public enum AggregationType
{
    /// <summary>Count, Min, Max, Sum, Avg.</summary>
    Stats,

    /// <summary>Fixed-width histogram buckets.</summary>
    Histogram
,
    /// <summary>Approximate distinct numeric-value count using HyperLogLog++.</summary>
    Cardinality,
    /// <summary>Approximate double percentiles using t-digest.</summary>
    TDigestPercentiles,
    /// <summary>Approximate Int64 percentiles using an HDR histogram.</summary>
    HdrPercentiles
}
