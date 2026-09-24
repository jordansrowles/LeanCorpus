namespace Rowles.LeanCorpus.Search.XY;

/// <summary>Matches documents with an XY point within an inclusive Euclidean radius.</summary>
public sealed class XYDistanceQuery : Query
{
    /// <summary>Creates a point filter for the supplied centre and radius.</summary>
    /// <param name="field">The XY point field name.</param>
    /// <param name="centre">The Cartesian centre point.</param>
    /// <param name="radius">The finite, non-negative radius in XY coordinate units.</param>
    public XYDistanceQuery(string field, XYPoint centre, float radius)
    {
        if (!float.IsFinite(radius) || radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "The radius must be finite and non-negative.");

        Field = field ?? throw new ArgumentNullException(nameof(field));
        Centre = centre;
        Radius = radius;
    }

    /// <inheritdoc/>
    public override string Field { get; }

    /// <summary>Gets the Cartesian centre point.</summary>
    public XYPoint Centre { get; }

    /// <summary>Gets the inclusive radius in XY coordinate units.</summary>
    public float Radius { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
        => obj is XYDistanceQuery query
            && query.Field == Field
            && query.Centre == Centre
            && query.Radius == Radius;

    /// <inheritdoc/>
    public override int GetHashCode()
        => CombineBoost(HashCode.Combine(nameof(XYDistanceQuery), Field, Centre, Radius));
}
