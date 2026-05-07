namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Pairs a <see cref="Query"/> with an <see cref="Search.Occur"/> specifying how it participates in a <see cref="BooleanQuery"/>.
/// </summary>
public sealed class BooleanClause : IEquatable<BooleanClause>
{
    /// <summary>Gets the query for this clause.</summary>
    public Query Query { get; }

    /// <summary>Gets the occurrence type that controls how this clause affects matching and scoring.</summary>
    public Occur Occur { get; }

    /// <summary>Initialises a new <see cref="BooleanClause"/> with the specified query and occurrence.</summary>
    /// <param name="query">The sub-query for this clause.</param>
    /// <param name="occur">How this clause participates in the enclosing boolean query.</param>
    public BooleanClause(Query query, Occur occur)
    {
        Query = query;
        Occur = occur;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is BooleanClause other && Equals(other);

    /// <inheritdoc/>
    public bool Equals(BooleanClause? other) =>
        other is not null && Query.Equals(other.Query) && Occur == other.Occur;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Query, Occur);
}
