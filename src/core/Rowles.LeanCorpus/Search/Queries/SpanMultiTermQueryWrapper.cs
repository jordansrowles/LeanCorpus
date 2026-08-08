namespace Rowles.LeanCorpus.Search.Queries;

/// <summary>Expands a multi-term query into position-aware term spans at search time.</summary>
public sealed class SpanMultiTermQueryWrapper : SpanQuery
{
    /// <summary>Gets the wrapped multi-term query.</summary>
    public Query Match { get; }

    /// <inheritdoc/>
    public override string Field => Match.Field;

    /// <summary>Initialises a multi-term span wrapper.</summary>
    public SpanMultiTermQueryWrapper(Query match)
    {
        Match = match ?? throw new ArgumentNullException(nameof(match));
        if (match is not PrefixQuery
            and not WildcardQuery
            and not FuzzyQuery
            and not RegexpQuery
            and not TermRangeQuery)
        {
            throw new ArgumentException(
                "Span multi-term queries support prefix, wildcard, fuzzy, regular expression and term-range queries.",
                nameof(match));
        }
    }

    /// <inheritdoc/>
    public override void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        Match.Visit(visitor.GetSubVisitor(Occur.Should, this));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is SpanMultiTermQueryWrapper other
            && Match.Equals(other.Match)
            && Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode()
        => CombineBoost(HashCode.Combine(nameof(SpanMultiTermQueryWrapper), Match));
}
