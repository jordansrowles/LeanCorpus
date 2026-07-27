namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Numeric range query over 32-bit integer values.</summary>
public sealed class Int32RangeQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }
    /// <summary>Gets the lower bound.</summary>
    public int Min { get; }
    /// <summary>Gets the upper bound.</summary>
    public int Max { get; }
    /// <summary>Gets whether the lower bound is inclusive.</summary>
    public bool IncludeMin { get; }
    /// <summary>Gets whether the upper bound is inclusive.</summary>
    public bool IncludeMax { get; }

    /// <summary>Initialises a 32-bit integer range query.</summary>
    public Int32RangeQuery(
        string field,
        int min,
        int max,
        bool includeMin = true,
        bool includeMax = true)
    {
        Field = field;
        Min = min;
        Max = max;
        IncludeMin = includeMin;
        IncludeMax = includeMax;
    }

    /// <inheritdoc/>
    public override Query Rewrite() => new Int64RangeQuery(
        Field, Min, Max, IncludeMin, IncludeMax)
    {
        Boost = Boost
    };

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is Int32RangeQuery other &&
        Field == other.Field && Min == other.Min && Max == other.Max &&
        IncludeMin == other.IncludeMin && IncludeMax == other.IncludeMax &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(
        HashCode.Combine(nameof(Int32RangeQuery), Field, Min, Max, IncludeMin, IncludeMax));
}
