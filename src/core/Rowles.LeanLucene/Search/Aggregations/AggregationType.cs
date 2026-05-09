namespace Rowles.LeanLucene.Search.Aggregations;

/// <summary>Supported aggregation types.</summary>
public enum AggregationType
{
    /// <summary>Count, Min, Max, Sum, Avg.</summary>
    Stats,

    /// <summary>Fixed-width histogram buckets.</summary>
    Histogram
}
