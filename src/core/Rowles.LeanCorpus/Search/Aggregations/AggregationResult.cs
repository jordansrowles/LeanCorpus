namespace Rowles.LeanCorpus.Search.Aggregations;

/// <summary>
/// Result of a numeric aggregation over matching documents.
/// </summary>
/// <remarks>
/// <see cref="Count"/> counts every observed numeric value, including non-finite
/// IEEE-754 values. For Stats aggregations, a NaN value propagates to the extrema,
/// sum and average; positive and negative infinity are retained using normal
/// IEEE-754 arithmetic.
/// </remarks>
public sealed class AggregationResult
{
    /// <summary>Gets the caller-assigned name of this aggregation result.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the numeric field that was aggregated.</summary>
    public required string Field { get; init; }

    /// <summary>Gets the number of observed numeric values included in the aggregation.</summary>
    public long Count { get; init; }

    /// <summary>Gets the minimum observed numeric value included in the aggregation.</summary>
    public double Min { get; init; } = double.PositiveInfinity;

    /// <summary>Gets the maximum observed numeric value included in the aggregation.</summary>
    public double Max { get; init; } = double.NegativeInfinity;

    /// <summary>Gets the sum of all observed numeric values included in the aggregation.</summary>
    public double Sum { get; init; }

    /// <summary>
    /// Gets the average of all observed numeric values included in the aggregation.
    /// </summary>
    /// <value>The average, or 0.0 when no numeric values were observed.</value>
    public double Avg => Count > 0 ? Sum / Count : 0.0;

    /// <summary>Histogram buckets (non-null only for Histogram aggregations).</summary>
    public IReadOnlyList<HistogramBucket>? Buckets { get; init; }

    /// <summary>An empty/no-data result.</summary>
    public static AggregationResult Empty(string name, string field)
        => new() { Name = name, Field = field, Count = 0, Min = 0, Max = 0, Sum = 0 };
}
