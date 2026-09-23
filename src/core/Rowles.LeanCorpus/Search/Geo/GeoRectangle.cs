using Rowles.LeanCorpus.Search.Geo.Internal;

namespace Rowles.LeanCorpus.Search.Geo;

/// <summary>An immutable geographic rectangle. West greater than east crosses the International Date Line.</summary>
public readonly record struct GeoRectangle : IGeoGeometry
{
    /// <summary>Creates a geographic rectangle.</summary>
    public GeoRectangle(double south, double west, double north, double east)
    {
        GeoGeometryValidation.ValidateCoordinate(south, west);
        GeoGeometryValidation.ValidateCoordinate(north, east);
        if (south > north)
            throw new ArgumentOutOfRangeException(nameof(south), "Rectangle south must not exceed north.");

        South = south;
        West = west;
        North = north;
        East = east;
    }

    /// <summary>Gets the southern latitude.</summary>
    public double South { get; }

    /// <summary>Gets the western longitude.</summary>
    public double West { get; }

    /// <summary>Gets the northern latitude.</summary>
    public double North { get; }

    /// <summary>Gets the eastern longitude.</summary>
    public double East { get; }

    /// <summary>Gets whether the rectangle crosses the International Date Line.</summary>
    public bool CrossesDateline => West > East;
}
