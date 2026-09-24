namespace Rowles.LeanCorpus.Search.XY;

/// <summary>Matches documents with an XY point inside an inclusive rectangle.</summary>
public sealed class XYBoundingBoxQuery : Query
{
    /// <summary>Creates a point filter for the supplied inclusive rectangle.</summary>
    /// <param name="field">The XY point field name.</param>
    /// <param name="bounds">The inclusive Cartesian bounds.</param>
    public XYBoundingBoxQuery(string field, XYRectangle bounds)
    {
        Field = field ?? throw new ArgumentNullException(nameof(field));
        Bounds = bounds;
    }

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the inclusive Cartesian bounds.</summary>
    public XYRectangle Bounds { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is XYBoundingBoxQuery query
            && query.Field == Field
            && query.Bounds == Bounds;

    /// <inheritdoc/>
    public override int GetHashCode()
        => CombineBoost(HashCode.Combine(nameof(XYBoundingBoxQuery), Field, Bounds));
}
