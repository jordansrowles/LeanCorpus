namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Matches spans from <see cref="Include"/> that do not overlap with any span from <see cref="Exclude"/>.
/// </summary>
public sealed class SpanNotQuery : SpanQuery
{
    /// <summary>Gets the span query whose matches are included when they do not overlap with <see cref="Exclude"/>.</summary>
    public SpanQuery Include { get; }

    /// <summary>Gets the span query whose matches are excluded from the results of <see cref="Include"/>.</summary>
    public SpanQuery Exclude { get; }

    /// <inheritdoc/>
    public override string Field => Include.Field;

    /// <summary>Initialises a new <see cref="SpanNotQuery"/> with the given include and exclude span queries.</summary>
    /// <param name="include">Spans from this query are returned when they do not overlap with <paramref name="exclude"/>.</param>
    /// <param name="exclude">Spans from this query suppress overlapping spans from <paramref name="include"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null"/>.</exception>
    public SpanNotQuery(SpanQuery include, SpanQuery exclude)
    {
        ArgumentNullException.ThrowIfNull(include);
        ArgumentNullException.ThrowIfNull(exclude);
        Include = include;
        Exclude = exclude;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SpanNotQuery other &&
        Include.Equals(other.Include) && Exclude.Equals(other.Exclude) &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() =>
        CombineBoost(HashCode.Combine(nameof(SpanNotQuery), Include, Exclude));
}
