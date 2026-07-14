namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>
/// Numeric range filter over 64-bit integer field values.
/// </summary>
public sealed class Int64RangeQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the inclusive lower bound of the range.</summary>
    public long Min { get; }

    /// <summary>Gets the inclusive upper bound of the range.</summary>
    public long Max { get; }

    /// <summary>Initialises a new <see cref="Int64RangeQuery"/> for the given field and bounds.</summary>
    /// <param name="field">The field to search.</param>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The inclusive upper bound.</param>
    public Int64RangeQuery(string field, long min, long max)
    {
        Field = field;
        Min = min;
        Max = max;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is Int64RangeQuery other &&
        string.Equals(Field, other.Field, StringComparison.Ordinal) &&
        Min == other.Min && Max == other.Max && Math.Abs(Boost - other.Boost) < 1e-6f;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(Int64RangeQuery), Field, Min, Max));
}
