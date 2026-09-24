using System.Globalization;
using System.Text;
using Rowles.LeanCorpus.Search.Geo;
using Rowles.LeanCorpus.Search.XY;

namespace Rowles.LeanCorpus.Search.Spatial.Internal;

/// <summary>Appends a stable value-based fingerprint for built-in spatial geometries.</summary>
internal static class SpatialGeometryFingerprint
{
    internal static void Append(IGeoGeometry geometry, StringBuilder builder)
    {
        switch (geometry)
        {
            case GeoPoint point:
                builder.Append("geo-point|");
                AppendDouble(builder, point.Latitude);
                AppendDouble(builder, point.Longitude);
                break;
            case GeoRectangle rectangle:
                builder.Append("geo-rectangle|");
                AppendDouble(builder, rectangle.South);
                AppendDouble(builder, rectangle.West);
                AppendDouble(builder, rectangle.North);
                AppendDouble(builder, rectangle.East);
                break;
            case GeoCircle circle:
                builder.Append("geo-circle|");
                AppendDouble(builder, circle.Latitude);
                AppendDouble(builder, circle.Longitude);
                AppendDouble(builder, circle.RadiusMetres);
                break;
            case GeoLineString line:
                builder.Append("geo-line|");
                AppendGeoPoints(builder, line.Points);
                break;
            case GeoPolygon polygon:
                builder.Append("geo-polygon|");
                AppendGeoPoints(builder, polygon.Shell);
                builder.Append("holes=").Append(polygon.Holes.Count).Append('|');
                foreach (IReadOnlyList<GeoPoint> hole in polygon.Holes)
                    AppendGeoPoints(builder, hole);
                break;
            case GeoGeometryCollection collection:
                builder.Append("geo-collection|").Append(collection.Geometries.Count).Append('|');
                foreach (IGeoGeometry component in collection.Geometries)
                    Append(component, builder);
                break;
            default:
                throw new ArgumentException("Only built-in geographic geometries have stable fingerprints.", nameof(geometry));
        }
    }

    internal static void Append(IXYGeometry geometry, StringBuilder builder)
    {
        switch (geometry)
        {
            case XYPoint point:
                builder.Append("xy-point|");
                AppendFloat(builder, point.X);
                AppendFloat(builder, point.Y);
                break;
            case XYRectangle rectangle:
                builder.Append("xy-rectangle|");
                AppendFloat(builder, rectangle.MinX);
                AppendFloat(builder, rectangle.MinY);
                AppendFloat(builder, rectangle.MaxX);
                AppendFloat(builder, rectangle.MaxY);
                break;
            case XYCircle circle:
                builder.Append("xy-circle|");
                AppendFloat(builder, circle.X);
                AppendFloat(builder, circle.Y);
                AppendFloat(builder, circle.Radius);
                break;
            case XYLineString line:
                builder.Append("xy-line|");
                AppendXYPoints(builder, line.Points);
                break;
            case XYPolygon polygon:
                builder.Append("xy-polygon|");
                AppendXYPoints(builder, polygon.Shell);
                builder.Append("holes=").Append(polygon.Holes.Count).Append('|');
                foreach (IReadOnlyList<XYPoint> hole in polygon.Holes)
                    AppendXYPoints(builder, hole);
                break;
            case XYGeometryCollection collection:
                builder.Append("xy-collection|").Append(collection.Geometries.Count).Append('|');
                foreach (IXYGeometry component in collection.Geometries)
                    Append(component, builder);
                break;
            default:
                throw new ArgumentException("Only built-in Cartesian geometries have stable fingerprints.", nameof(geometry));
        }
    }

    private static void AppendGeoPoints(StringBuilder builder, IReadOnlyList<GeoPoint> points)
    {
        builder.Append(points.Count).Append('|');
        foreach (GeoPoint point in points)
        {
            AppendDouble(builder, point.Latitude);
            AppendDouble(builder, point.Longitude);
        }
    }

    private static void AppendXYPoints(StringBuilder builder, IReadOnlyList<XYPoint> points)
    {
        builder.Append(points.Count).Append('|');
        foreach (XYPoint point in points)
        {
            AppendFloat(builder, point.X);
            AppendFloat(builder, point.Y);
        }
    }

    private static void AppendDouble(StringBuilder builder, double value)
    {
        if (value == 0)
            value = 0;
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        builder.Append(text.Length).Append(':').Append(text).Append('|');
    }

    private static void AppendFloat(StringBuilder builder, float value)
    {
        if (value == 0)
            value = 0;
        string text = value.ToString("R", CultureInfo.InvariantCulture);
        builder.Append(text.Length).Append(':').Append(text).Append('|');
    }
}
