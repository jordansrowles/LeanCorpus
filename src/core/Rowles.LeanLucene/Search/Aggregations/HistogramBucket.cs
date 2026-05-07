namespace Rowles.LeanLucene.Search.Aggregations;

/// <summary>A single histogram bucket.</summary>
public sealed record HistogramBucket(double LowerBound, double UpperBound, long Count);
