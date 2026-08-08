namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Matches spans whose end position is at or before a field position.</summary>
public sealed class SpanFirstQuery : SpanQuery
{
    /// <summary>Gets the wrapped span query.</summary>
    public SpanQuery Match { get; }

    /// <summary>Gets the exclusive upper field position.</summary>
    public int End { get; }

    /// <inheritdoc/>
    public override string Field => Match.Field;

    /// <summary>Initialises a first-span query.</summary>
    public SpanFirstQuery(SpanQuery match, int end)
    {
        Match = match ?? throw new ArgumentNullException(nameof(match));
        ArgumentOutOfRangeException.ThrowIfNegative(end);
        End = end;
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        Match.Visit(visitor.GetSubVisitor(Occur.Must, this));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is SpanFirstQuery other
            && End == other.End
            && Match.Equals(other.Match)
            && Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode()
        => CombineBoost(HashCode.Combine(nameof(SpanFirstQuery), Match, End));
}
