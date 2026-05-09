namespace Rowles.LeanLucene.Search;

/// <summary>
/// Base class for all query types.
/// </summary>
public abstract class Query : IEquatable<Query>
{
    /// <summary>Gets the primary field this query targets.</summary>
    public abstract string Field { get; }

    /// <summary>Boost factor applied to this query's score. Default 1.0.</summary>
    public float Boost { get; set; } = 1.0f;

    /// <inheritdoc/>
    public abstract override bool Equals(object? obj);

    /// <inheritdoc/>
    public abstract override int GetHashCode();

    /// <inheritdoc/>
    public bool Equals(Query? other) => Equals((object?)other);

    /// <summary>Helper to combine boost into a hash code.</summary>
    protected int CombineBoost(int hash) => HashCode.Combine(hash, Boost);
}
