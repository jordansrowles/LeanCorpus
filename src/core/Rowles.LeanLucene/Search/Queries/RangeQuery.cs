namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Numeric range filter over NumericField values.
/// </summary>
public sealed class RangeQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the inclusive lower bound of the numeric range.</summary>
    public double Min { get; }

    /// <summary>Gets the inclusive upper bound of the numeric range.</summary>
    public double Max { get; }

    /// <summary>Initialises a new <see cref="RangeQuery"/> for the given field and numeric bounds.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The inclusive upper bound.</param>
    public RangeQuery(string field, double min, double max)
    {
        Field = field;
        Min = min;
        Max = max;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is RangeQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Min == other.Min && Max == other.Max && Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(RangeQuery), Field, Min, Max));
}
