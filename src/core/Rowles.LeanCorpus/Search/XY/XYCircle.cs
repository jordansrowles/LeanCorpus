namespace Rowles.LeanCorpus.Search.XY;

/// <summary>An immutable Cartesian circle.</summary>
public readonly record struct XYCircle : IXYGeometry
{
    /// <summary>Creates a Cartesian circle.</summary>
    public XYCircle(float x, float y, float radius)
    {
        XYGeometryValidation.ValidateCoordinate(x, y);
        if (float.IsNaN(radius) || float.IsInfinity(radius) || radius < 0)
            throw new ArgumentOutOfRangeException(nameof(radius), "Circle radius must be finite and non-negative.");
        X = x;
        Y = y;
        Radius = radius;
    }

    /// <summary>Gets the centre x coordinate.</summary>
    public float X { get; }

    /// <summary>Gets the centre y coordinate.</summary>
    public float Y { get; }

    /// <summary>Gets the radius in coordinate units.</summary>
    public float Radius { get; }
}
