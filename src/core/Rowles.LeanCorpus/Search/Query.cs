namespace Rowles.LeanCorpus.Search;

/// <summary>
/// Base class for all query types.
/// </summary>
public abstract class Query : IEquatable<Query>
{
    /// <summary>Gets the single field this query targets, or an empty value for fieldless and multi-field queries.</summary>
    public abstract string Field { get; }

    /// <summary>Boost factor applied to this query's score. Default 1.0.</summary>
    public float Boost { get; set; } = 1.0f;

    /// <inheritdoc/>
    public abstract override bool Equals(object? obj);

    /// <inheritdoc/>
    public abstract override int GetHashCode();

    /// <inheritdoc/>
    public bool Equals(Query? other) => Equals((object?)other);

    /// <summary>
    /// Rewrites this query to an executable query form.
    /// Custom query types can override this to lower themselves to built-in queries.
    /// </summary>
    public virtual Query Rewrite() => this;

    /// <summary>
    /// Creates a custom weight for this query, or <see langword="null"/> to use built-in dispatch.
    /// </summary>
    public virtual Scoring.Weight? CreateWeight(Searcher.IndexSearcher searcher)
    {
        ArgumentNullException.ThrowIfNull(searcher);
        return null;
    }

    /// <summary>Visits this query and its children.</summary>
    public virtual void Visit(QueryVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);
        visitor.VisitLeaf(this);
    }

    /// <summary>Helper to combine boost into a hash code.</summary>
    protected int CombineBoost(int hash) => HashCode.Combine(hash, Boost);
}
