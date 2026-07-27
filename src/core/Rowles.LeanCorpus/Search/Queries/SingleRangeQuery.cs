namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Numeric range query over single-precision values.</summary>
public sealed class SingleRangeQuery : Query
{
    /// <inheritdoc/>
    public override string Field { get; }
    /// <summary>Gets the lower bound.</summary>
    public float Min { get; }
    /// <summary>Gets the upper bound.</summary>
    public float Max { get; }
    /// <summary>Gets whether the lower bound is inclusive.</summary>
    public bool IncludeMin { get; }
    /// <summary>Gets whether the upper bound is inclusive.</summary>
    public bool IncludeMax { get; }

    /// <summary>Initialises a single-precision range query.</summary>
    public SingleRangeQuery(
        string field,
        float min,
        float max,
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
    public override Query Rewrite() => new RangeQuery(
        Field, Min, Max, IncludeMin, IncludeMax)
    {
        Boost = Boost
    };

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SingleRangeQuery other &&
        Field == other.Field && Min == other.Min && Max == other.Max &&
        IncludeMin == other.IncludeMin && IncludeMax == other.IncludeMax &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(
        HashCode.Combine(nameof(SingleRangeQuery), Field, Min, Max, IncludeMin, IncludeMax));
}
