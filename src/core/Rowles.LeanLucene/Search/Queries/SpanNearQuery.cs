namespace Rowles.LeanLucene.Search.Queries;

/// <summary>
/// Matches documents where the spans from all clauses appear within <see cref="Slop"/> positions
/// of each other. When <see cref="InOrder"/> is true, the spans must appear in the given order.
/// </summary>
public sealed class SpanNearQuery : SpanQuery
{
    /// <summary>Gets the ordered list of span sub-queries that must appear within the slop window.</summary>
    public IReadOnlyList<SpanQuery> Clauses { get; }

    /// <summary>Gets the maximum positional distance allowed between span boundaries.</summary>
    public int Slop { get; }

    /// <summary>Gets a value indicating whether spans must appear in the order given by <see cref="Clauses"/>.</summary>
    public bool InOrder { get; }

    /// <inheritdoc/>
    public override string Field => Clauses.Count > 0 ? Clauses[0].Field : string.Empty;

    /// <summary>Initialises a new <see cref="SpanNearQuery"/> with the given clauses, slop, and ordering constraint.</summary>
    /// <param name="clauses">The ordered span sub-queries.</param>
    /// <param name="slop">Maximum allowed positional gap between spans. Must be non-negative.</param>
    /// <param name="inOrder">When <see langword="true"/>, spans must appear in the given order.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="clauses"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="slop"/> is negative.</exception>
    public SpanNearQuery(SpanQuery[] clauses, int slop, bool inOrder = true)
    {
        ArgumentNullException.ThrowIfNull(clauses);
        ArgumentOutOfRangeException.ThrowIfNegative(slop);
        Clauses = clauses;
        Slop = slop;
        InOrder = inOrder;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is SpanNearQuery other &&
        Slop == other.Slop && InOrder == other.InOrder && Boost == other.Boost &&
        Clauses.Count == other.Clauses.Count &&
        Clauses.SequenceEqual(other.Clauses);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var h = new HashCode();
        h.Add(nameof(SpanNearQuery));
        h.Add(Slop);
        h.Add(InOrder);
        foreach (var c in Clauses) h.Add(c);
        return CombineBoost(h.ToHashCode());
    }
}
