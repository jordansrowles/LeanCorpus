namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Returns inner spans that are contained by a matching enclosing span.</summary>
public sealed class SpanWithinQuery : SpanQuery
{
    /// <summary>Gets the inner span query.</summary>
    public SpanQuery Little { get; }

    /// <summary>Gets the enclosing span query.</summary>
    public SpanQuery Big { get; }

    /// <inheritdoc/>
    public override string Field => Little.Field;

    /// <summary>Initialises a within-span query.</summary>
    public SpanWithinQuery(SpanQuery little, SpanQuery big)
    {
        Little = little ?? throw new ArgumentNullException(nameof(little));
        Big = big ?? throw new ArgumentNullException(nameof(big));
        if (!string.Equals(Little.Field, Big.Field, StringComparison.Ordinal))
            throw new ArgumentException("Within span clauses must target the same field.");
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        Little.Visit(visitor.GetSubVisitor(Occur.Must, this));
        Big.Visit(visitor.GetSubVisitor(Occur.Must, this));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is SpanWithinQuery other
            && Little.Equals(other.Little)
            && Big.Equals(other.Big)
            && Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode()
        => CombineBoost(HashCode.Combine(nameof(SpanWithinQuery), Little, Big));
}
