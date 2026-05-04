namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Combines sub-queries with MUST, SHOULD, and MUST_NOT clauses.
/// Once a <see cref="BooleanQuery"/> has been built via <see cref="Builder"/> it is
/// immutable; the clauses list is sealed and all properties are frozen.
/// </summary>
public sealed class BooleanQuery : Query
{
    private readonly List<BooleanClause> _clauses;
    private readonly bool _frozen;

    private BooleanQuery(List<BooleanClause> clauses, bool frozen)
    {
        _clauses = clauses;
        _frozen = frozen;
    }

    /// <summary>Initialises a new, mutable <see cref="BooleanQuery"/>.</summary>
    [Obsolete("Construct a BooleanQuery via BooleanQuery.Builder instead. Direct mutation will be removed in a future version.")]
    public BooleanQuery()
    {
        _clauses = [];
        _frozen = false;
    }

    /// <inheritdoc/>
    /// <remarks>Returns the field of the first clause, or empty string if no clauses have been added.</remarks>
    public override string Field => _clauses.Count > 0 ? _clauses[0].Query.Field : string.Empty;

    /// <summary>Gets the list of boolean clauses that compose this query.</summary>
    public IReadOnlyList<BooleanClause> Clauses => _clauses;

    /// <summary>
    /// Adds a sub-query with the specified occurrence type.
    /// </summary>
    /// <param name="query">The sub-query to add.</param>
    /// <param name="occur">How this clause participates in matching and scoring.</param>
    /// <exception cref="InvalidOperationException">Thrown when the query has been frozen via <see cref="Builder.Build"/>.</exception>
    [Obsolete("Use BooleanQuery.Builder.Add() and Build() instead. Direct mutation will be removed in a future version.")]
    public void Add(Query query, Occur occur)
    {
        if (_frozen)
            throw new InvalidOperationException("This BooleanQuery has been frozen and cannot be modified. Use BooleanQuery.Builder to create a new query.");
        _clauses.Add(new BooleanClause(query, occur));
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is BooleanQuery other &&
        Boost == other.Boost &&
        _clauses.Count == other._clauses.Count &&
        _clauses.SequenceEqual(other._clauses);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(nameof(BooleanQuery));
        foreach (var c in _clauses) h.Add(c);
        return CombineBoost(h.ToHashCode());
    }

    /// <summary>
    /// Builds an immutable <see cref="BooleanQuery"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var query = new BooleanQuery.Builder()
    ///     .Add(new TermQuery("title", "lucene"), Occur.Must)
    ///     .Add(new TermQuery("status", "active"), Occur.Should)
    ///     .Build();
    /// </code>
    /// </example>
    public sealed class Builder
    {
        private readonly List<BooleanClause> _clauses = [];
        private float _boost = 1.0f;

        /// <summary>
        /// Sets the boost factor applied to the whole query.
        /// </summary>
        /// <param name="boost">The boost multiplier.</param>
        /// <returns>This builder instance for method chaining.</returns>
        public Builder WithBoost(float boost)
        {
            _boost = boost;
            return this;
        }

        /// <summary>
        /// Adds a sub-query with the specified occurrence type.
        /// </summary>
        /// <param name="query">The sub-query to add.</param>
        /// <param name="occur">How this clause participates in matching and scoring.</param>
        /// <returns>This builder instance for method chaining.</returns>
        public Builder Add(Query query, Occur occur)
        {
            ArgumentNullException.ThrowIfNull(query);
            _clauses.Add(new BooleanClause(query, occur));
            return this;
        }

        /// <summary>
        /// Builds and returns a frozen (immutable) <see cref="BooleanQuery"/>.
        /// </summary>
        /// <returns>An immutable <see cref="BooleanQuery"/> containing the added clauses.</returns>
        public BooleanQuery Build()
        {
            var query = new BooleanQuery([.. _clauses], frozen: true)
            {
                Boost = _boost,
            };
            return query;
        }
    }
}
