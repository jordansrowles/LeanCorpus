using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.Internal;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Searcher.Internal;

/// <summary>Computes conservative lower bounds for the spatial sort frontier.</summary>
internal static class SpatialDistanceLowerBound
{
    private const double EarthRadiusMetres = 6_371_000.0;
    private const double GeoSafetyMarginMetres = 0.01;

    internal static double GeoMetres(
        double originLatitude,
        double originLongitude,
        uint minimumLongitude,
        uint maximumLongitude,
        uint minimumLatitude,
        uint maximumLatitude)
    {
        int minimumLongitudeCode = DecodeSortableInt(minimumLongitude);
        int maximumLongitudeCode = DecodeSortableInt(maximumLongitude);
        int minimumLatitudeCode = DecodeSortableInt(minimumLatitude);
        int maximumLatitudeCode = DecodeSortableInt(maximumLatitude);

        double minimumLat = Math.Max(-90, Math.BitDecrement(GeoEncodingUtils.DecodeLat(minimumLatitudeCode)));
        double maximumLat = Math.Min(90, Math.BitIncrement(GeoEncodingUtils.DecodeLat(
            maximumLatitudeCode == int.MaxValue ? int.MaxValue : maximumLatitudeCode + 1)));
        double minimumLon = Math.Max(-180, Math.BitDecrement(GeoEncodingUtils.DecodeLon(minimumLongitudeCode)));
        double maximumLon = Math.Min(180, Math.BitIncrement(GeoEncodingUtils.DecodeLon(
            maximumLongitudeCode == int.MaxValue ? int.MaxValue : maximumLongitudeCode + 1)));

        double normalisedOriginLongitude = GeoEncodingUtils.NormaliseLongitude(originLongitude);
        double longitudeDifferenceDegrees = MinimumCircularDistance(
            normalisedOriginLongitude, minimumLon, maximumLon);
        double longitudeDifference = longitudeDifferenceDegrees * (Math.PI / 180.0);

        double originLatitudeRadians = originLatitude * (Math.PI / 180.0);
        double minimumLatitudeRadians = minimumLat * (Math.PI / 180.0);
        double maximumLatitudeRadians = maximumLat * (Math.PI / 180.0);
        double sineOrigin = Math.Sin(originLatitudeRadians);
        double cosineTerm = Math.Cos(originLatitudeRadians) * Math.Cos(longitudeDifference);
        double optimumLatitude = Math.Atan2(sineOrigin, cosineTerm);
        double maximumDot = Math.Max(
            EvaluateLatitudeDot(minimumLatitudeRadians, sineOrigin, cosineTerm),
            EvaluateLatitudeDot(maximumLatitudeRadians, sineOrigin, cosineTerm));
        for (int revolution = -1; revolution <= 1; revolution++)
        {
            double candidateLatitude = optimumLatitude + revolution * (2 * Math.PI);
            if (candidateLatitude >= minimumLatitudeRadians && candidateLatitude <= maximumLatitudeRadians)
                maximumDot = Math.Max(maximumDot, EvaluateLatitudeDot(candidateLatitude, sineOrigin, cosineTerm));
        }

        double centralAngle = Math.Acos(Math.Clamp(maximumDot, -1.0, 1.0));
        return Math.Max(0, centralAngle * EarthRadiusMetres - GeoSafetyMarginMetres);
    }

    internal static double XYSquared(
        float originX,
        float originY,
        uint minimumX,
        uint maximumX,
        uint minimumY,
        uint maximumY)
    {
        float minX = SortableCoordinateEncoding.UnsortableFloatBits(minimumX);
        float maxX = SortableCoordinateEncoding.UnsortableFloatBits(maximumX);
        float minY = SortableCoordinateEncoding.UnsortableFloatBits(minimumY);
        float maxY = SortableCoordinateEncoding.UnsortableFloatBits(maximumY);
        double dx = originX < minX ? (double)minX - originX
            : originX > maxX ? (double)originX - maxX
            : 0;
        double dy = originY < minY ? (double)minY - originY
            : originY > maxY ? (double)originY - maxY
            : 0;
        double squared = dx * dx + dy * dy;
        return squared == 0 ? 0 : Math.Max(0, Math.BitDecrement(squared));
    }

    private static int DecodeSortableInt(uint sortable)
        => unchecked((int)(sortable ^ 0x8000_0000u));

    private static double EvaluateLatitudeDot(double latitude, double sineOrigin, double cosineTerm)
        => sineOrigin * Math.Sin(latitude) + cosineTerm * Math.Cos(latitude);

    private static double MinimumCircularDistance(double longitude, double minimum, double maximum)
    {
        if (longitude >= minimum && longitude <= maximum)
            return 0;

        static double CircularDifference(double left, double right)
        {
            double difference = Math.Abs(left - right);
            return Math.Min(difference, 360.0 - difference);
        }

        return Math.Min(
            CircularDifference(longitude, minimum),
            CircularDifference(longitude, maximum));
    }
}
