namespace Rowles.LeanLucene.Search.Aggregations;

/// <summary>
/// Result of a numeric aggregation over matching documents.
/// </summary>
public sealed class AggregationResult
{
    /// <summary>Gets the caller-assigned name of this aggregation result.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the numeric field that was aggregated.</summary>
    public required string Field { get; init; }

    /// <summary>Gets the number of documents that had a value for the aggregated field.</summary>
    public long Count { get; init; }

    /// <summary>Gets the minimum value found across matching documents.</summary>
    public double Min { get; init; } = double.PositiveInfinity;

    /// <summary>Gets the maximum value found across matching documents.</summary>
    public double Max { get; init; } = double.NegativeInfinity;

    /// <summary>Gets the sum of values across all matching documents.</summary>
    public double Sum { get; init; }

    /// <summary>
    /// Gets the average value across all matching documents.
    /// </summary>
    /// <value>The average, or 0.0 if no documents had a value.</value>
    public double Avg => Count > 0 ? Sum / Count : 0.0;

    /// <summary>Histogram buckets (non-null only for Histogram aggregations).</summary>
    public IReadOnlyList<HistogramBucket>? Buckets { get; init; }

    /// <summary>An empty/no-data result.</summary>
    public static AggregationResult Empty(string name, string field)
        => new() { Name = name, Field = field, Count = 0, Min = 0, Max = 0, Sum = 0 };
}
