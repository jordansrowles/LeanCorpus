namespace Rowles.LeanCorpus.Search.Geo;

using Rowles.LeanCorpus.Search;

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
    public static int EncodeLat(double lat) => EncodeLatFloor(lat);

    /// <summary>Encodes longitude (-180 to +180) to a sortable integer.</summary>
    public static int EncodeLon(double lon) => EncodeLonFloor(lon);

    /// <summary>Encodes latitude using outward lower-bound rounding.</summary>
    public static int EncodeLatFloor(double latitude)
        => EncodeFloor(latitude, -90, 90, LatFactor, nameof(latitude));

    /// <summary>Encodes latitude using outward upper-bound rounding.</summary>
    public static int EncodeLatCeil(double latitude)
        => EncodeCeil(latitude, -90, 90, LatFactor, nameof(latitude));

    /// <summary>Encodes longitude using outward lower-bound rounding.</summary>
    public static int EncodeLonFloor(double longitude)
        => EncodeFloor(longitude, -180, 180, LonFactor, nameof(longitude));

    /// <summary>Encodes longitude using outward upper-bound rounding.</summary>
    public static int EncodeLonCeil(double longitude)
        => EncodeCeil(longitude, -180, 180, LonFactor, nameof(longitude));

    /// <summary>Writes an encoded latitude as four big-endian sortable bytes.</summary>
    internal static void WriteLatSortable(double latitude, Span<byte> destination)
        => SortableCoordinateEncoding.WriteSortableInt32(EncodeLat(latitude), destination);

    /// <summary>Writes an encoded longitude as four big-endian sortable bytes.</summary>
    internal static void WriteLonSortable(double longitude, Span<byte> destination)
        => SortableCoordinateEncoding.WriteSortableInt32(EncodeLon(longitude), destination);

    /// <summary>Normalises a longitude to the inclusive [-180, 180] interval.</summary>
    public static double NormalizeLongitude(double longitude)
    {
        if (double.IsNaN(longitude) || double.IsInfinity(longitude))
            throw new ArgumentOutOfRangeException(nameof(longitude));
        longitude %= 360;
        if (longitude < -180) longitude += 360;
        if (longitude > 180) longitude -= 360;
        return longitude;
    }

    private static int EncodeFloor(double value, double min, double max, double factor, string parameterName)
    {
        Validate(value, min, max, parameterName);
        double scaled = Math.Floor((value - min) * factor);
        return unchecked((int)((long)Math.Clamp(scaled, 0, uint.MaxValue) + int.MinValue));
    }

    private static int EncodeCeil(double value, double min, double max, double factor, string parameterName)
    {
        Validate(value, min, max, parameterName);
        double scaled = Math.Ceiling((value - min) * factor);
        return unchecked((int)((long)Math.Clamp(scaled, 0, uint.MaxValue) + int.MinValue));
    }

    private static void Validate(double value, double min, double max, string parameterName)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(parameterName, $"Coordinate must be finite and between {min} and {max}.");
    }

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
        a = Math.Clamp(a, 0, 1);
        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return EarthRadiusMetres * c;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}
