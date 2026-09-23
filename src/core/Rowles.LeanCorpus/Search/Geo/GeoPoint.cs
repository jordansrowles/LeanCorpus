using Rowles.LeanCorpus.Search.Geo.Internal;

namespace Rowles.LeanCorpus.Search.Geo;

/// <summary>An immutable geographic point in latitude/longitude order.</summary>
public readonly record struct GeoPoint : IGeoGeometry
{
    /// <summary>Creates a geographic point.</summary>
    public GeoPoint(double latitude, double longitude)
    {
        GeoGeometryValidation.ValidateCoordinate(latitude, longitude);
        Latitude = latitude;
        Longitude = longitude;
    }

    /// <summary>Gets the latitude in degrees.</summary>
    public double Latitude { get; }

    /// <summary>Gets the longitude in degrees.</summary>
    public double Longitude { get; }
}
