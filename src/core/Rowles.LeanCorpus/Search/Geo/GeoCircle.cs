using Rowles.LeanCorpus.Search.Geo.Internal;

namespace Rowles.LeanCorpus.Search.Geo;

/// <summary>An immutable geographic circle with a radius in metres.</summary>
public readonly record struct GeoCircle : IGeoGeometry
{
    /// <summary>Creates a geographic circle.</summary>
    public GeoCircle(double latitude, double longitude, double radiusMetres)
    {
        GeoGeometryValidation.ValidateCoordinate(latitude, longitude);
        if (double.IsNaN(radiusMetres) || double.IsInfinity(radiusMetres) || radiusMetres < 0)
            throw new ArgumentOutOfRangeException(nameof(radiusMetres), "Circle radius must be finite and non-negative.");

        Latitude = latitude;
        Longitude = longitude;
        RadiusMetres = radiusMetres;
    }

    /// <summary>Gets the centre latitude.</summary>
    public double Latitude { get; }

    /// <summary>Gets the centre longitude.</summary>
    public double Longitude { get; }

    /// <summary>Gets the radius in metres.</summary>
    public double RadiusMetres { get; }
}
