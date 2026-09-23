namespace Rowles.LeanCorpus.Search.XY;

/// <summary>An immutable Cartesian point.</summary>
public readonly record struct XYPoint : IXYGeometry
{
    /// <summary>Creates a Cartesian point.</summary>
    public XYPoint(float x, float y)
    {
        XYGeometryValidation.ValidateCoordinate(x, y);
        X = x;
        Y = y;
    }

    /// <summary>Gets the x coordinate.</summary>
    public float X { get; }

    /// <summary>Gets the y coordinate.</summary>
    public float Y { get; }
}
