namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Wraps an inner query and assigns a constant score to every matching document,
/// bypassing BM25 scoring. Useful for filter-mode boolean clauses.
/// </summary>
public sealed class ConstantScoreQuery : Query
{
    /// <summary>Gets the wrapped inner query that must match for a document to be considered.</summary>
    public Query Inner { get; }

    /// <summary>Gets the constant score assigned to every matching document.</summary>
    public float ConstantScore { get; }

    /// <inheritdoc/>
    public override string Field => Inner.Field;

    /// <summary>Initialises a new <see cref="ConstantScoreQuery"/> wrapping the given inner query.</summary>
    /// <param name="inner">The query that documents must match.</param>
    /// <param name="score">The constant score to assign to every matching document. Default: 1.0.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="inner"/> is <see langword="null"/>.</exception>
    public ConstantScoreQuery(Query inner, float score = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(inner);
        Inner = inner;
        ConstantScore = score;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ConstantScoreQuery other &&
        Inner.Equals(other.Inner) &&
        ConstantScore == other.ConstantScore &&
        Boost == other.Boost;

    /// <inheritdoc/>
    public override int GetHashCode() => CombineBoost(HashCode.Combine(nameof(ConstantScoreQuery), Inner, ConstantScore));
}
