using Rowles.LeanCorpus.Search.XY.Internal;

namespace Rowles.LeanCorpus.Search.XY;

/// <summary>An immutable Cartesian rectangle.</summary>
public readonly record struct XYRectangle : IXYGeometry
{
    /// <summary>Creates a Cartesian rectangle.</summary>
    public XYRectangle(float minX, float minY, float maxX, float maxY)
    {
        XYGeometryValidation.ValidateCoordinate(minX, minY);
        XYGeometryValidation.ValidateCoordinate(maxX, maxY);
        if (minX > maxX) throw new ArgumentOutOfRangeException(nameof(minX), "Rectangle minX must not exceed maxX.");
        if (minY > maxY) throw new ArgumentOutOfRangeException(nameof(minY), "Rectangle minY must not exceed maxY.");
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    /// <summary>Gets the minimum x coordinate.</summary>
    public float MinX { get; }

    /// <summary>Gets the minimum y coordinate.</summary>
    public float MinY { get; }

    /// <summary>Gets the maximum x coordinate.</summary>
    public float MaxX { get; }

    /// <summary>Gets the maximum y coordinate.</summary>
    public float MaxY { get; }
}
