namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Returns enclosing spans that contain a matching inner span.</summary>
public sealed class SpanContainingQuery : SpanQuery
{
    /// <summary>Gets the enclosing span query.</summary>
    public SpanQuery Big { get; }

    /// <summary>Gets the contained span query.</summary>
    public SpanQuery Little { get; }

    /// <inheritdoc/>
    public override string Field => Big.Field;

    /// <summary>Initialises a containing-span query.</summary>
    public SpanContainingQuery(SpanQuery big, SpanQuery little)
    {
        Big = big ?? throw new ArgumentNullException(nameof(big));
        Little = little ?? throw new ArgumentNullException(nameof(little));
        if (!string.Equals(Big.Field, Little.Field, StringComparison.Ordinal))
            throw new ArgumentException("Containing span clauses must target the same field.");
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        Big.Visit(visitor.GetSubVisitor(Occur.Must, this));
        Little.Visit(visitor.GetSubVisitor(Occur.Must, this));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is SpanContainingQuery other
            && Big.Equals(other.Big)
            && Little.Equals(other.Little)
            && Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode()
        => CombineBoost(HashCode.Combine(nameof(SpanContainingQuery), Big, Little));
}
