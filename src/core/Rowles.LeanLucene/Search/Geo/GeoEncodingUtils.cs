namespace Rowles.LeanLucene.Search.Geo;

/// <summary>
/// Utilities for encoding/decoding geographic coordinates to/from numeric values
/// suitable for indexing and range queries.
/// </summary>
public static class GeoEncodingUtils
{
    // Map [-90,+90] to [int.MinValue, int.MaxValue] for latitude
    // Map [-180,+180] to [int.MinValue, int.MaxValue] for longitude
    private const double LatFactor = (double)uint.MaxValue / 180.0;
    private const double LonFactor = (double)uint.MaxValue / 360.0;

    /// <summary>Encodes latitude (-90 to +90) to a sortable integer.</summary>
    public static int EncodeLat(double lat)
        => unchecked((int)((long)((lat + 90.0) * LatFactor) + int.MinValue));

    /// <summary>Encodes longitude (-180 to +180) to a sortable integer.</summary>
    public static int EncodeLon(double lon)
        => unchecked((int)((long)((lon + 180.0) * LonFactor) + int.MinValue));

    /// <summary>Decodes a latitude integer back to degrees.</summary>
    public static double DecodeLat(int encoded)
        => ((long)encoded - (long)int.MinValue) / LatFactor - 90.0;

    /// <summary>Decodes a longitude integer back to degrees.</summary>
    public static double DecodeLon(int encoded)
        => ((long)encoded - (long)int.MinValue) / LonFactor - 180.0;

    /// <summary>
    /// Computes the Haversine distance between two points in metres.
    /// </summary>
    public static double HaversineDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusMetres = 6_371_000.0;
        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMetres * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
