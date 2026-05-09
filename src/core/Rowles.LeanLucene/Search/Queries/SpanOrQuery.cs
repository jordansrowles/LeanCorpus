namespace Rowles.LeanLucene.Search.Queries;

/// <summary>Matches documents that match any of the provided span sub-queries.</summary>
public sealed class SpanOrQuery : SpanQuery
{
    /// <summary>Gets the list of span sub-queries, any of which may match.</summary>
    public IReadOnlyList<SpanQuery> Clauses { get; }

    /// <inheritdoc/>
    public override string Field => Clauses.Count > 0 ? Clauses[0].Field : string.Empty;

    /// <summary>Initialises a new <see cref="SpanOrQuery"/> with the given clauses.</summary>
    /// <param name="clauses">The span sub-queries, any one of which may produce a match.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clauses"/> is <see langword="null"/>.</exception>
    public SpanOrQuery(params SpanQuery[] clauses)
    {
        ArgumentNullException.ThrowIfNull(clauses);
        Clauses = clauses;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SpanOrQuery other && Boost == other.Boost &&
        Clauses.Count == other.Clauses.Count &&
        Clauses.SequenceEqual(other.Clauses);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(nameof(SpanOrQuery));
        foreach (var c in Clauses) h.Add(c);
        return CombineBoost(h.ToHashCode());
    }
}
